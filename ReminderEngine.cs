using UnityEngine;

namespace FoodGuard
{
    /// <summary>
    ///   The trigger matrix. Called once per client frame from Plugin.Update (only when there is a
    ///   local player). Each tick it:
    ///     1. Reads food state, base state, and combat state (cheap; the scanners guard nulls).
    ///     2. Evaluates the four triggers in PRIORITY order (combat first -- highest stakes).
    ///     3. Fires the first applicable trigger that isn't on cooldown, shows its popup (and the
    ///        alert sound for the combat case), and stamps that trigger's cooldown.
    ///
    ///   The order matters: combat beats everything; then leave-base (a one-shot transition); then
    ///   no-food-out; then the plain low-food reminder. Because combat fires first and has its own
    ///   longer cooldown, you won't get a low-food popup layered on top of a combat popup.
    ///
    ///   ANTI-SPAM MODEL
    ///   Each trigger has its own cooldown timestamp (Unity.realtimeSinceStartup). A trigger only
    ///   fires if (now - lastFired) >= its cooldown. The leave-base trigger additionally only fires
    ///   on a real in->out base crossing (BaseZoneChecker.LeftBase), so it cannot repeat until you
    ///   re-enter and leave again. The combat sound has its OWN cooldown separate from the popup,
    ///   so the popup can refresh while the sound is throttled.
    ///
    ///   POPUP CHANNEL
    ///   MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, text) -- the large center-screen
    ///   banner. Local call, always renders, no RPC (proven in nosmokeguard/HoverSelector.cs).
    ///
    ///   SOUND CHANNEL
    ///   Only for trigger #3 (combat + low food), per the user's spec. Resolves a named SFX prefab via
    ///   ZNetScene.GetPrefab and plays its AudioSource at the player. If the prefab is missing or has no
    ///   AudioSource, the sound is skipped silently -- the popup still shows. Throttled by
    ///   CombatSoundCooldown so it can't loop.
    /// </summary>
    internal static class ReminderEngine
    {
        // Per-trigger last-fired timestamps (Unity wall clock seconds). -999 so the first eligible fire isn't blocked.
        private static float _lastLeaveBase = -999f;
        private static float _lastLowFood   = -999f;
        private static float _lastCombat    = -999f;
        private static float _lastNoFoodOut = -999f;
        private static float _lastCombatNoRest = -999f;
        private static float _lastCombatSfx = -999f;
        private static float _lastUnmarkedReminder = -999f;

        // GLOBAL POPUP SPACER: center-screen banners overwrite each other instantly, so if two triggers
        // fire a few tenths of a second apart (common right after a teleport, or when no-food-out and
        // low-food are both true) the player only sees the last one and the rest are unreadable. This
        // enforces a minimum gap between ANY two popups, regardless of which trigger fired. The combat
        // trigger can still take priority (see Tick) -- it just respects the spacer like the others.
        private static float _lastAnyPopup = -999f;

        // SIGNAL STABILITY: a "needs food" reading must persist for this many consecutive polls before
        // any trigger can act on it. This is the real fix for false positives during teleport / load:
        // mid-port the food list and base zone can read transiently empty/wrong for a single sample,
        // but they do not stay wrong across two polls at 0.5s. Require 2 = "confirmed".
        private const int NeedsFoodConfirmPolls = 2;
        private static int _needsFoodStreak;
        private static int _noFoodStreak;
        private static int _notRestedStreak;

        // Cached SFX prefab lookup (resolved lazily, re-resolved if missing in case the scene loaded late).
        private static GameObject _alertSfxPrefab;
        private static bool _alertSfxResolved;

        internal static void Tick(FoodMonitor.FoodState food, bool suppressed)
        {
            Player local = Player.m_localPlayer;
            if (local == null) return;

            float now = Time.realtimeSinceStartup;

            // Post-eat grace: if the player just ate, hold all triggers for PostEatGraceSeconds.
            // We still recompute zone/combat each tick (cheap) and update streaks, but skip firing.
            BaseZoneChecker.BaseState zone = BaseZoneChecker.Evaluate(local);
            bool inCombat = LocalCombatScanner.IsLocalPlayerInCombat(local);
            bool inBase = zone.IsInBase;

            // Track streaks. A trigger only sees a confirmed signal after NeedsFoodConfirmPolls hits.
            _needsFoodStreak = food.NeedsFood ? _needsFoodStreak + 1 : 0;
            _noFoodStreak    = food.HasNoFood ? _noFoodStreak + 1    : 0;
            bool needsFoodConfirmed = _needsFoodStreak >= NeedsFoodConfirmPolls;
            bool noFoodConfirmed    = _noFoodStreak    >= NeedsFoodConfirmPolls;

            // Rest status: also requires the 2-poll confirmation so a one-frame effect flicker (e.g.
            // mid-teleport the SEM reads empty) can't trip the combat-no-rest trigger.
            bool isRested = RestStatusChecker.IsRested(local);
            _notRestedStreak = !isRested ? _notRestedStreak + 1 : 0;
            bool notRestedConfirmed = _notRestedStreak >= NeedsFoodConfirmPolls;

            Plugin.Debug($"tick: needsFood={food.NeedsFood}(streak={_needsFoodStreak}) " +
                         $"noFood={food.HasNoFood}(streak={_noFoodStreak}) " +
                         $"active={food.ActiveFoodCount} lowest={food.LowestRemainingPct}% " +
                         $"rested={isRested}(notRestedStreak={_notRestedStreak}) " +
                         $"inBase={inBase} leftBase={zone.LeftBase} combat={inCombat} " +
                         $"suppressed={suppressed}");

            if (suppressed)
            {
                Plugin.Debug("suppressed (post-eat grace); skipping trigger evaluation.");
                return;
            }

            // TRIGGER EVALUATION (priority order: combat > leave-base > no-food-out > low-food).
            // Each trigger checks its own condition + per-trigger cooldown. A trigger that's ready to
            // fire then goes through TryFire, which enforces the GLOBAL popup spacer so two triggers
            // can't stack back-to-back (center-screen banners overwrite each other -- without the spacer
            // only the last would be readable). Only ONE popup fires per tick; the first eligible wins.

            // ---- #3 COMBAT + low food (highest priority; never suppressed by base) ----
            if (Plugin.CombatEnabled.Value && inCombat && needsFoodConfirmed &&
                CooldownElapsed(now, _lastCombat, Plugin.CombatCooldown.Value) &&
                TryFire(now, Plugin.CombatMessage.Value, nameof(_lastCombat)))
            {
                _lastCombat = now;
                if (Plugin.CombatSoundEnabled.Value &&
                    CooldownElapsed(now, _lastCombatSfx, Plugin.CombatSoundCooldown.Value))
                {
                    PlayAlertSfx(local);
                    _lastCombatSfx = now;
                }
                Plugin.Debug($"#3 combat trigger fired.");
                return;
            }

            // ---- #5 COMBAT + not Rested (shares the alert sound channel with #3) ----
            // Going into combat without Rested means no health/stamina regen bonus. Triggers after #3 so
            // that if you're BOTH low on food AND unrested, the more urgent food popup wins. Respects the
            // same death/teleport/eat grace (via `suppressed`) and the global popup spacer (via TryFire).
            if (Plugin.CombatNoRestEnabled.Value && inCombat && notRestedConfirmed &&
                CooldownElapsed(now, _lastCombatNoRest, Plugin.CombatNoRestCooldown.Value) &&
                TryFire(now, Plugin.CombatNoRestMessage.Value, nameof(_lastCombatNoRest)))
            {
                _lastCombatNoRest = now;
                if (Plugin.CombatSoundEnabled.Value &&
                    CooldownElapsed(now, _lastCombatSfx, Plugin.CombatSoundCooldown.Value))
                {
                    PlayAlertSfx(local);
                    _lastCombatSfx = now;
                }
                Plugin.Debug($"#5 combat-no-rest trigger fired.");
                return;
            }

            // ---- #1 LEAVE BASE while needing food (one-shot per base-exit) ----
            if (Plugin.LeaveBaseEnabled.Value && zone.LeftBase && needsFoodConfirmed &&
                CooldownElapsed(now, _lastLeaveBase, Plugin.LeaveBaseCooldown.Value) &&
                TryFire(now, Plugin.LeaveBaseMessage.Value, nameof(_lastLeaveBase)))
            {
                _lastLeaveBase = now;
                Plugin.Debug($"#1 leave-base trigger fired.");
                return;
            }

            // ---- #4 NO FOOD at all while OUT of base ----
            if (Plugin.NoFoodOutEnabled.Value && noFoodConfirmed && !inBase &&
                CooldownElapsed(now, _lastNoFoodOut, Plugin.NoFoodOutCooldown.Value) &&
                TryFire(now, Plugin.NoFoodOutMessage.Value, nameof(_lastNoFoodOut)))
            {
                _lastNoFoodOut = now;
                Plugin.Debug($"#4 no-food-out trigger fired.");
                return;
            }

            // ---- #2 plain LOW FOOD (suppressed in base if configured) ----
            if (Plugin.LowFoodEnabled.Value && needsFoodConfirmed &&
                !(inBase && Plugin.SuppressLowFoodInBase.Value) &&
                CooldownElapsed(now, _lastLowFood, Plugin.LowFoodCooldown.Value))
            {
                string text = Plugin.LowFoodMessage.Value.Replace("{pct}",
                    food.LowestRemainingPct.ToString());
                if (TryFire(now, text, nameof(_lastLowFood)))
                {
                    _lastLowFood = now;
                    Plugin.Debug($"#2 low-food trigger fired.");
                    return;
                }
            }
        }

        /// <summary>
        ///   Shows a popup only if the GLOBAL popup spacer has elapsed since the last popup of ANY
        ///   trigger, then stamps the spacer. Returns true if shown, false if suppressed by the spacer.
        ///   This is the single chokepoint that prevents back-to-back stacking.
        /// </summary>
        private static bool TryFire(float now, string text, string triggerName)
        {
            float spacing = Plugin.PopupSpacingSeconds.Value;
            if (!CooldownElapsed(now, _lastAnyPopup, spacing))
            {
                Plugin.Debug($"{triggerName} ready but blocked by global popup spacer " +
                             $"({(_lastAnyPopup + spacing - now):F1}s until next allowed).");
                return false;
            }
            ShowPopup(text);
            _lastAnyPopup = now;
            return true;
        }

        /// <summary>
        ///   Periodic reminder to mark your base (called from Plugin.Update when on Marked mode with no
        ///   mark set). Has its own cooldown (UnmarkedReminderCooldown) and goes through TryFire so it
        ///   respects the global popup spacer. The message tells the player exactly what to do (press F7).
        /// </summary>
        public static void TryUnmarkedReminder(float now)
        {
            if (!CooldownElapsed(now, _lastUnmarkedReminder, Plugin.UnmarkedReminderCooldown.Value)) return;

            string key = Plugin.MarkBaseHotkey.Value.ToString();
            string text = $"FoodGuard: no base marked -- press {key} at your base so 'in base' works.";
            if (TryFire(now, text, nameof(_lastUnmarkedReminder)))
            {
                _lastUnmarkedReminder = now;
                Plugin.Debug("unmarked-base reminder fired.");
            }
        }

        /// <summary>Reset all cooldowns + scanner state + signal streaks (call on world load / teleport / respawn).</summary>
        public static void Reset()
        {
            _lastLeaveBase = -999f;
            _lastLowFood   = -999f;
            _lastCombat    = -999f;
            _lastNoFoodOut = -999f;
            _lastCombatNoRest = -999f;
            _lastCombatSfx = -999f;
            _lastUnmarkedReminder = -999f;
            _lastAnyPopup  = -999f;
            _needsFoodStreak = 0;
            _noFoodStreak = 0;
            _notRestedStreak = 0;
            _alertSfxPrefab = null;
            _alertSfxResolved = false;
            LocalCombatScanner.Reset();
            BaseZoneChecker.Reset();
        }

        private static bool CooldownElapsed(float now, float last, float cooldown)
            => (now - last) >= cooldown;

        // ---- Popup (the "new text popup" -- large center-screen banner) ----
        private static void ShowPopup(string text)
        {
            MessageHud mh = MessageHud.instance;
            if (mh == null) { Plugin.Debug("MessageHud not ready; popup skipped."); return; }
            try
            {
                // MessageType.Center = 2 = the large center-screen banner vanilla uses for system notices.
                mh.ShowMessage(MessageHud.MessageType.Center, text);
            }
            catch (System.Exception e)
            {
                Plugin.Debug($"ShowMessage threw (non-fatal): {e.Message}");
            }
        }

        // ---- Alert sound (combat + low-food only) ----
        // Resolves the configured prefab name through ZNetScene (the networked prefab table) and plays
        // its AudioSource at the player. If the prefab can't be found or has no AudioSource, we skip
        // silently -- the popup still showed. This avoids bundling any audio asset.
        private static void PlayAlertSfx(Player local)
        {
            GameObject prefab = ResolveAlertSfxPrefab();
            if (prefab == null)
            {
                Plugin.Debug($"Alert SFX prefab '{Plugin.AlertSfxName.Value}' not found in ZNetScene; sound skipped.");
                return;
            }

            try
            {
                // Spawn at the player so a 3D AudioSource attenuates naturally. parent=false so the
                // instance isn't tied to the player transform; it self-destructs via the SFX component
                // (vanilla sfx_* prefabs carry a ZSFX/timed destroy). If it lacks that, we clean up below.
                GameObject sfx = Object.Instantiate(prefab, local.transform.position, Quaternion.identity);
                AudioSource src = sfx.GetComponentInChildren<AudioSource>();
                if (src == null)
                {
                    Plugin.Debug($"SFX prefab '{prefab.name}' has no AudioSource; sound skipped.");
                    Object.Destroy(sfx);
                    return;
                }

                // Safety net: if the prefab has no built-in self-destroy (some modded SFX don't), make
                // sure it doesn't leak. Lifetime = clip length + slack, or 5s if no clip.
                float life = src.clip != null ? src.clip.length + 0.5f : 5f;
                Object.Destroy(sfx, life);
            }
            catch (System.Exception e)
            {
                Plugin.Debug($"PlayAlertSfx threw (non-fatal): {e.Message}");
            }
        }

        private static GameObject ResolveAlertSfxPrefab()
        {
            if (_alertSfxResolved) return _alertSfxPrefab;

            string name = Plugin.AlertSfxName.Value;
            _alertSfxResolved = true;       // only attempt the lookup once per scene load
            if (string.IsNullOrEmpty(name)) return null;

            ZNetScene znet = ZNetScene.instance;
            if (znet == null) { _alertSfxResolved = false; return null; }   // not in-world yet; retry later

            // ZNetScene.GetPrefab has a string overload (verified via Cecil) -- cleaner than hashing.
            _alertSfxPrefab = znet.GetPrefab(name);
            return _alertSfxPrefab;
        }
    }
}
