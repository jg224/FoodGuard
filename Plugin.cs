using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;

namespace FoodGuard
{
    /// <summary>
    ///   FoodGuard -- a standalone, CLIENT-SIDE BepInEx mod that reminds a forgetful player to eat.
    ///
    ///   WHY THIS EXISTS
    ///   One player on the server repeatedly forgets to keep food up, especially when leaving base or
    ///   going into combat. This mod nudges them with the large center-screen popup Valheim already
    ///   uses for system messages (MessageHud.ShowMessage, MessageType.Center), plus an optional alert
    ///   sound for the highest-stakes case (combat with low food).
    ///
    ///   SCOPE -- CLIENT ONLY
    ///   This DLL runs entirely on the player's own client. There is no server half, no routed RPC,
    ///   no ServerSync. The dedicated server has no local player and no MessageHud, so every check
    ///   no-ops there via the Player.m_localPlayer == null guard. Build it here (the project has the
    ///   game DLL paths and reference patterns), then drop the built DLL into one user's r2modman
    ///   profile. Only the user who installs it sees the reminders -- it is private to them.
    ///
    ///   THE FOUR TRIGGERS (see ReminderEngine for the matrix)
    ///     #1 Leave base while needing food      -> popup, once per base-exit (rearms on re-enter)
    ///     #2 Food <= FoodThresholdPercent        -> popup, SUPPRESSED while in base (quiet at home)
    ///     #3 In combat AND food <= threshold     -> popup + alert SFX (sound reserved for this case)
    ///     #4 No food at all while out of base    -> popup
    ///   "Needing food" = any active food at/under the threshold OR a food slot empty. Each trigger has
    ///   its own cooldown so it never loops/spams. In-base suppression only applies to #2 -- #1 and #3
    ///   still fire because leaving/combat are exactly when it matters.
    ///
    ///   POPUP MECHANISM (the "new text popup" already proven in this project)
    ///   MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, text) -- a local client call,
    ///   no RPC. Renders the large center-screen banner vanilla uses for world-save notices. Verified
    ///   working in nosmokeguard/HoverSelector.cs ShowLocal(). MessageType.Center = 2.
    ///
    ///   WHY Update() TICKS HERE (but not on restartguard's server-only mod)
    ///   BaseUnityPlugin.Update() DOES tick on a real client (a player's machine with a camera and a
    ///   local player). It does NOT tick on the headless dedicated server -- but this mod is
    ///   client-only, so that's irrelevant; the m_localPlayer guard makes it a no-op server-side.
    /// </summary>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "jg224.FoodGuard";
        public const string PluginName = "FoodGuard";
        public const string PluginVersion = "0.1.0";

        internal static ManualLogSource Log;

        // ---- General ----
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<bool> DebugLogging;
        internal static ConfigEntry<int> FoodThresholdPercent;     // a food is "low" at/under this % remaining
        internal static ConfigEntry<bool> EmptySlotCountsAsLow;    // an empty/expired slot also means "needs food"
        internal static ConfigEntry<float> PollInterval;           // seconds between evaluation passes
        internal static ConfigEntry<float> PostEatGraceSeconds;    // suppress reminders for N s after eating
        internal static ConfigEntry<float> RespawnGraceSeconds;    // suppress reminders for N s after death/respawn
        internal static ConfigEntry<float> LoginGraceSeconds;      // suppress reminders for N s after login/spawn
        internal static ConfigEntry<KeyCode> DebugHotkey;          // press to dump live state to screen
        internal static ConfigEntry<KeyCode> SfxDumpHotkey;        // press to dump all sfx_* prefab names to log

        // ---- Base zone ----
        internal static ConfigEntry<string> BaseZoneMode;          // Marked | CraftingStation | Building | Both
        internal static ConfigEntry<bool> SuppressLowFoodInBase;   // trigger #2 stays quiet in base
        internal static ConfigEntry<KeyCode> MarkBaseHotkey;       // press at base to save its location
        internal static ConfigEntry<string> MarkedBaseCenter;      // "x,y,z" auto-filled by the hotkey
        internal static ConfigEntry<float> MarkedBaseRadius;       // meters from the marked center = base
        internal static ConfigEntry<bool> RemindIfUnmarked;        // nag when Marked mode has no mark set
        internal static ConfigEntry<float> UnmarkedReminderCooldown; // seconds between "mark your base" popups

        // ---- Triggers (master switches) ----
        internal static ConfigEntry<bool> LeaveBaseEnabled;
        internal static ConfigEntry<bool> LowFoodEnabled;
        internal static ConfigEntry<bool> CombatEnabled;
        internal static ConfigEntry<bool> NoFoodOutEnabled;
        internal static ConfigEntry<bool> CombatNoRestEnabled;   // #5: in combat AND not Rested

        // ---- Sound (reserved for combat + low-food per the user's spec) ----
        internal static ConfigEntry<bool> CombatSoundEnabled;
        internal static ConfigEntry<float> CombatSoundCooldown;    // seconds between repeated combat SFX
        internal static ConfigEntry<string> AlertSfxName;          // vanilla prefab name (e.g. "sfx_alert")

        // ---- Cooldowns (per trigger, seconds) ----
        internal static ConfigEntry<float> LeaveBaseCooldown;
        internal static ConfigEntry<float> LowFoodCooldown;
        internal static ConfigEntry<float> CombatCooldown;
        internal static ConfigEntry<float> NoFoodOutCooldown;
        internal static ConfigEntry<float> CombatNoRestCooldown;  // #5
        internal static ConfigEntry<float> PopupSpacingSeconds;  // global min gap between any two popups

        // ---- Messages ({pct} substituted with the lowest remaining-food percentage) ----
        internal static ConfigEntry<string> LeaveBaseMessage;
        internal static ConfigEntry<string> LowFoodMessage;
        internal static ConfigEntry<string> CombatMessage;
        internal static ConfigEntry<string> NoFoodOutMessage;
        internal static ConfigEntry<string> CombatNoRestMessage;  // #5

        private void Awake()
        {
            Log = Logger;

            Enabled = Config.Bind("General", "Enabled", true,
                "Master switch. When false, FoodGuard does nothing.");

            DebugLogging = Config.Bind("General", "DebugMode", false,
                "Verbose logging for diagnosing triggers. Leave off in normal play.");

            FoodThresholdPercent = Config.Bind("General", "FoodThresholdPercent", 25,
                "A food counts as 'low' when its remaining time is at or under this percent of its " +
                "total duration. Default 25 = remind when a food has 25% time left. Range 1-99.");

            EmptySlotCountsAsLow = Config.Bind("General", "EmptySlotCountsAsLow", false,
                "When true, an empty food slot (you only ate 2 of 3, or a food just expired) also counts " +
                "as 'needing food'. Default FALSE: an empty slot is usually a deliberate choice, and the " +
                "reliable signal is a food actually expiring (caught by FoodThresholdPercent). Set true " +
                "only if you want nagged for not running a full 3 foods at all times.");

            PollInterval = Config.Bind("General", "PollInterval", 0.5f,
                "Seconds between evaluation passes. Lower = more responsive, higher = cheaper. " +
                "0.5s is a good balance and avoids catching transient states (e.g. mid-teleport) " +
                "where food or base-zone reads are momentarily unsettled.");

            PostEatGraceSeconds = Config.Bind("General", "PostEatGraceSeconds", 15f,
                "After you eat any food, suppress all reminders for this many seconds. Prevents a " +
                "popup the instant a slot refreshes (e.g. a 1% food expiring just as you eat a new one). " +
                "The grace only matters if you're still 'needing food' after eating; if everything's " +
                "fine, no popup would have fired anyway.");

            RespawnGraceSeconds = Config.Bind("General", "RespawnGraceSeconds", 60f,
                "After you die (and respawn), suppress ALL reminders for this many seconds. On death " +
                "Valheim wipes your food, so 'needs food' is instantly true -- without this the mod spams " +
                "you the moment you respawn, before you've had a chance to loot your body and eat. " +
                "Measured from the moment of death. 60s is usually enough to get reoriented; raise it " +
                "if you want more breathing room after a death.");

            LoginGraceSeconds = Config.Bind("General", "LoginGraceSeconds", 20f,
                "After you log in / spawn into the world, suppress ALL reminders (including the 'mark your " +
                "base' nudge) for this many seconds. Gives you time to load in, get oriented, and -- if you " +
                "haven't yet -- mark your base with F7 before any popups appear. Fires on both initial login " +
                "and post-respawn. Set 0 to disable the grace entirely.");

            DebugHotkey = Config.Bind("General", "DebugHotkey", KeyCode.F8,
                "Press this key in-game to dump the live food/base/combat state the mod sees as a " +
                "center-screen popup + BepInEx log line. Use it to diagnose 'why didn't it fire' or " +
                "'why did it fire' moments. Set to KeyCode.None to disable.");

            SfxDumpHotkey = Config.Bind("General", "SfxDumpHotkey", KeyCode.F9,
                "Press this key in-game to dump every sfx_* (and vfx_*) prefab name your game has " +
                "loaded to the BepInEx log. Use it to find a valid AlertSfxName for the combat alert " +
                "sound -- any name in the dump is guaranteed to work. Names go to the log only (not the " +
                "screen, since there are hundreds). Set to KeyCode.None to disable.");

            BaseZoneMode = Config.Bind("Base", "BaseZoneMode", "Marked",
                "What counts as 'base' for the quiet zone and the leave-base trigger. " +
                "Marked (default, recommended) = a single location YOU mark with the MarkBaseHotkey. " +
                "Only that spot counts -- farms, outposts, and other players' bases never will. " +
                "CraftingStation = inside any workbench/forge effect area (broad; trips on farms). " +
                "Building = inside a warm/roofed interior (WarmCozyArea). " +
                "Both = either of the two above.");

            MarkBaseHotkey = Config.Bind("Base", "MarkBaseHotkey", KeyCode.F7,
                "Stand at the center of your main base and press this key once to mark it. Your current " +
                "position is saved to MarkedBaseCenter and used as the base center (only matters when " +
                "BaseZoneMode = Marked). Re-mark any time you move base.");

            MarkedBaseCenter = Config.Bind("Base", "MarkedBaseCenter", "",
                "The marked base center as 'x,y,z'. Auto-filled by the MarkBaseHotkey; you usually don't " +
                "edit this by hand. Empty (default) means no base is marked yet -- nothing counts as base " +
                "until you press the hotkey once at your base.");

            MarkedBaseRadius = Config.Bind("Base", "MarkedBaseRadius", 30f,
                "Radius in meters around the marked base center that counts as 'base'. 30 covers a typical " +
                "main base. Increase for a sprawling base, decrease for a compact one.");

            RemindIfUnmarked = Config.Bind("Base", "RemindIfUnmarked", true,
                "When true (and BaseZoneMode = Marked), periodically show a popup reminding you to press " +
                "the MarkBaseHotkey at your base if you haven't marked one yet. Without a mark, NOTHING " +
                "counts as base -- so the low-food nudge won't be suppressed at home. This nudge stops the " +
                "moment you mark a base. Set false to silence it.");

            UnmarkedReminderCooldown = Config.Bind("Base", "UnmarkedReminderCooldown", 300f,
                "Seconds between 'mark your base' reminder popups (only when RemindIfUnmarked is true and " +
                "no base is marked). Default 300s = once every 5 minutes. Raise to nag less, lower to nag more.");

            SuppressLowFoodInBase = Config.Bind("Base", "SuppressLowFoodInBase", true,
                "When true, the plain low-food reminder (#2) stays quiet while you are in base. " +
                "The leave-base and combat triggers still fire -- only the idle 'food is low' nudge " +
                "is suppressed at home.");

            LeaveBaseEnabled = Config.Bind("Triggers", "LeaveBaseEnabled", true,
                "Remind once when you leave base while needing food. Fires once per base-exit and " +
                "rearms when you re-enter base.");

            LowFoodEnabled = Config.Bind("Triggers", "LowFoodEnabled", true,
                "Remind when any food is at/under FoodThresholdPercent remaining. Suppressed in base " +
                "if SuppressLowFoodInBase is true.");

            CombatEnabled = Config.Bind("Triggers", "CombatEnabled", true,
                "Remind (and play the alert sound) when you are in combat AND food is at/under the " +
                "threshold. Never suppressed by base.");

            NoFoodOutEnabled = Config.Bind("Triggers", "NoFoodOutEnabled", true,
                "Remind when you have NO food eaten at all and you are away from base.");

            CombatNoRestEnabled = Config.Bind("Triggers", "CombatNoRestEnabled", true,
                "Remind (and play the alert sound) when you are in combat AND do not have the Rested " +
                "buff. Going into combat without Rested means no health/stamina regen bonus. Never " +
                "suppressed by base, death/teleport/eat grace still apply.");

            CombatSoundEnabled = Config.Bind("Sound", "CombatSoundEnabled", true,
                "Play an alert sound ONLY for the combat + low-food case (#3), per design. " +
                "All other triggers are text-only. Set false for silent operation.");

            CombatSoundCooldown = Config.Bind("Sound", "CombatSoundCooldown", 60f,
                "Minimum seconds between repeated combat alert sounds while the condition persists. " +
                "Prevents an audio loop if you stay in combat with low food.");

            AlertSfxName = Config.Bind("Sound", "AlertSfxName", "sfx_perfectblock",
                "Valheim prefab name of the alert sound effect. Resolved via ZNetScene.GetPrefab. " +
                "Must be a networked prefab that contains an AudioSource. Default sfx_perfectblock = the " +
                "sharp metallic perfect-block ring. If it can't be found, the sound is skipped silently " +
                "(the popup still shows). Press F9 to dump every valid sound name to the log.");

            LeaveBaseCooldown = Config.Bind("Cooldowns", "LeaveBaseCooldown", 30f,
                "Seconds between repeated leave-base popups (the trigger also rearms on re-enter).");

            LowFoodCooldown = Config.Bind("Cooldowns", "LowFoodCooldown", 30f,
                "Seconds between repeated plain low-food popups.");

            CombatCooldown = Config.Bind("Cooldowns", "CombatCooldown", 30f,
                "Seconds between repeated combat popups (sound uses CombatSoundCooldown separately).");

            NoFoodOutCooldown = Config.Bind("Cooldowns", "NoFoodOutCooldown", 45f,
                "Seconds between repeated 'no food at all' popups while out of base.");

            CombatNoRestCooldown = Config.Bind("Cooldowns", "CombatNoRestCooldown", 30f,
                "Seconds between repeated combat-no-rest popups (sound uses the shared CombatSoundCooldown).");

            PopupSpacingSeconds = Config.Bind("Cooldowns", "PopupSpacingSeconds", 10f,
                "MINIMUM seconds between ANY two popups, across all triggers. Center-screen banners " +
                "overwrite each other instantly, so without this, two triggers firing a few tenths of " +
                "a second apart (e.g. right after a teleport) would stack and you'd only see the last. " +
                "5s gives you time to read each one. Set 0 to allow stacking (not recommended).");

            LeaveBaseMessage = Config.Bind("Messages", "LeaveBaseMessage",
                "You left base without food -- EAT NOW before you head out!",
                "Popup text. Plain text; no substitution tokens.");

            LowFoodMessage = Config.Bind("Messages", "LowFoodMessage",
                "Food at {pct}% -- time to eat!",
                "Popup text. '{pct}' is replaced with the lowest remaining-food percentage (integer).");

            CombatMessage = Config.Bind("Messages", "CombatMessage",
                "COMBAT with low food -- EAT NOW!",
                "Popup text. Shown alongside the alert sound.");

            NoFoodOutMessage = Config.Bind("Messages", "NoFoodOutMessage",
                "You have no food eaten and you're away from base -- EAT!",
                "Popup text.");

            CombatNoRestMessage = Config.Bind("Messages", "CombatNoRestMessage",
                "COMBAT without Rested -- get to a fire/shelter first!",
                "Popup text. Shown alongside the alert sound (same channel as combat+low-food).");

            Log.LogInfo($"{PluginName} v{PluginVersion} loaded. BaseZoneMode={BaseZoneMode.Value}, " +
                        $"Threshold={FoodThresholdPercent.Value}%, CombatSound={CombatSoundEnabled.Value}.");

            // Config-version hint: if BaseZoneMode is a legacy value (CraftingStation/Building/Both) the
            // user carried over from an earlier build, surface a one-line nudge in the log so the new
            // Marked default isn't a silent surprise. We do NOT overwrite their choice.
            string initialMode = (BaseZoneMode.Value ?? "").Trim();
            if (initialMode == "Both" || initialMode == "CraftingStation" || initialMode == "Building")
            {
                Log.LogInfo($"[hint] BaseZoneMode is '{initialMode}' (carried from an earlier FoodGuard build). " +
                            $"Set it to 'Marked' and press F7 at your base for strict per-base detection " +
                            $"(farms/outposts won't count). Current mode treats any matching area as base.");
            }

            // Apply Harmony patches (SpawnTracker.OnSpawnedPatch, etc.). Wrapped so a patch failure
            // can't keep the whole mod from loading -- the spawn tracker has a fallback path.
            try
            {
                var harmony = new HarmonyLib.Harmony(PluginGuid);
                harmony.PatchAll();
                Log.LogInfo("Harmony patches applied.");
            }
            catch (System.Exception e)
            {
                Log.LogWarning($"Harmony patchall failed (non-fatal; spawn-time tracking falls back): {e.Message}");
            }
        }

        // Throttle: evaluate at PollInterval, not every frame. Cuts transient-state false positives
        // (mid-teleport reads) and is far cheaper than a per-frame scan of all characters/areas.
        private static float _nextPoll;

        // Post-eat grace: the wall-clock time until which all reminders are suppressed. Updated whenever
        // the player eats (detected by watching the food-list count go up).
        internal static float _suppressUntil;

        // Eat-detection: we watch the size of the local player's food list. When it increases, the
        // player just ate something -> arm the grace window.
        private static int _lastFoodCount = -1;

        // Debug-hotkey latch so holding the key doesn't re-fire every frame.
        private static bool _debugKeyDown;
        // SFX-dump hotkey latch.
        private static bool _sfxDumpKeyDown;
        // Mark-base hotkey latch.
        private static bool _markKeyDown;

        /// <summary>
        ///   Saves the player's current position as the marked base center, writes it to the config
        ///   (so it persists across restarts), and confirms on-screen. Re-marking overwrites the prior.
        /// </summary>
        private static void MarkBaseHere(Player local)
        {
            Vector3 pos = local.transform.position;
            string val = $"{pos.x:F1},{pos.y:F1},{pos.z:F1}";
            MarkedBaseCenter.Value = val;          // BepInEx persists this to the .cfg immediately
            BaseZoneChecker.Reset();                // re-read center on next poll; clear stale edge state
            string msg = $"FoodGuard: main base marked here ({val}). Radius {MarkedBaseRadius.Value:F0}m.";
            try { MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, msg); } catch { }
            Log.LogInfo($"[mark] base marked at {val}, radius {MarkedBaseRadius.Value}.");
        }

        /// <summary>
        ///   Client frame tick. No-op on a headless server (no local player). On a client, throttles
        ///   evaluation to PollInterval and also watches for teleport transitions -- when the player
        ///   starts or finishes a teleport, all reminder state is reset so a port can't manufacture a
        ///   fake "left base" / "no food" popup during the load.
        /// </summary>
        private void Update()
        {
            if (!Enabled.Value) return;
            Player local = Player.m_localPlayer;
            if (local == null) return;          // not in-game / server-side

            // On-demand debug hotkey: edge-triggered, dumps live state. Runs every frame so it's
            // responsive, but the latch makes it fire once per press.
            if (DebugHotkey.Value != KeyCode.None)
            {
                bool down = Input.GetKeyDown(DebugHotkey.Value);
                if (down && !_debugKeyDown)
                {
                    _debugKeyDown = true;
                    Diagnostics.DumpState(local);
                }
                else if (!down)
                {
                    _debugKeyDown = false;
                }
            }

            // SFX dump hotkey: lists every sfx_*/vfx_* prefab the game has loaded, to the log. Used to
            // discover valid values for AlertSfxName. No local-player dependency beyond ZNetScene being
            // up, but we still require one to be safe (means the world is loaded).
            if (SfxDumpHotkey.Value != KeyCode.None)
            {
                bool sDown = Input.GetKeyDown(SfxDumpHotkey.Value);
                if (sDown && !_sfxDumpKeyDown)
                {
                    _sfxDumpKeyDown = true;
                    Diagnostics.DumpSfxList();
                }
                else if (!sDown)
                {
                    _sfxDumpKeyDown = false;
                }
            }

            // Mark-base hotkey: save the current position as the base center (persisted to cfg).
            if (MarkBaseHotkey.Value != KeyCode.None)
            {
                bool mDown = Input.GetKeyDown(MarkBaseHotkey.Value);
                if (mDown && !_markKeyDown)
                {
                    _markKeyDown = true;
                    MarkBaseHere(local);
                }
                else if (!mDown)
                {
                    _markKeyDown = false;
                }
            }

            // Detect teleport start/end and reset transient state on each transition. Without this,
            // a port leaves stale base-zone / cooldown state that can fire a popup the instant the
            // player materializes at the destination (before its base zone resolves).
            bool tpNow = IsPlayerTransitioning(local);
            if (tpNow != _wasTransitioning)
            {
                if (tpNow)
                {
                    // Just entered a teleport / load. Clear state and push the next poll out by the
                    // grace window so triggers can't fire until the destination has settled.
                    ReminderEngine.Reset();
                    _nextPoll = Time.realtimeSinceStartup + Plugin.PollInterval.Value;
                    _lastFoodCount = -1;     // re-baseline food count after the port settles
                    Plugin.Debug("Teleport/load transition detected; state reset.");
                }
                _wasTransitioning = tpNow;
            }

            // Hard guard: never evaluate while actively teleporting/loading.
            if (tpNow) { _wasTransitioning = tpNow; return; }

            // DEATH / RESPAWN GUARD. On death Valheim calls Player.OnDeath(), which (per Cecil IL of
            // this build) does m_foods.Clear() and resets m_timeSinceDeath to 0. Without a guard the
            // instant food is cleared, 'needs food' is true and every trigger fires -- hence the
            // "popups the second I died" bug.
            //
            // We do NOT use Character.IsDead(): in this build it's a stripped stub that always returns
            // false (ldc.i4.0; ret), so it can't detect death. Instead we read Player.m_timeSinceDeath
            // directly. It is set to 0 in OnDeath and incremented every frame in UpdateStats -- so it
            // is a reliable 'seconds since you last died' signal, even surviving save/load (Player.Save
            // persists it). Suppress ALL triggers while it is under RespawnGraceSeconds.
            float? timeSinceDeath = GetTimeSinceDeath(local);
            bool deathGraceActive = false;
            if (timeSinceDeath.HasValue && timeSinceDeath.Value < RespawnGraceSeconds.Value)
            {
                deathGraceActive = true;
                // While the grace is active, also reset engine state so no stale streak/cooldown fires
                // the instant the grace elapses (food was just cleared -> streaks would be maxed).
                if (!_inDeathGrace)
                {
                    _inDeathGrace = true;
                    ReminderEngine.Reset();
                    Plugin.Debug($"death grace armed (timeSinceDeath={timeSinceDeath.Value:F1}s); suppressing.");
                }
            }
            else if (_inDeathGrace)
            {
                _inDeathGrace = false;
                ReminderEngine.Reset();
                Plugin.Debug("death grace elapsed; resuming normal evaluation.");
            }

            float now = Time.realtimeSinceStartup;
            if (now < _nextPoll) return;
            _nextPoll = now + Plugin.PollInterval.Value;

            // Eat detection: if the active-food count went up since last poll, the player ate -> arm
            // the post-eat grace window so no trigger fires for PostEatGraceSeconds.
            FoodMonitor.FoodState food = FoodMonitor.Evaluate(local);
            if (_lastFoodCount < 0)
            {
                _lastFoodCount = food.ActiveFoodCount;   // first poll after load: just baseline
            }
            else if (food.ActiveFoodCount > _lastFoodCount)
            {
                _suppressUntil = now + PostEatGraceSeconds.Value;
                Plugin.Debug($"Eat detected (foods {_lastFoodCount} -> {food.ActiveFoodCount}); " +
                             $"suppressing for {PostEatGraceSeconds.Value}s.");
            }
            _lastFoodCount = food.ActiveFoodCount;

            // LOGIN GRACE: for the first LoginGraceSeconds after login/respawn, suppress all reminders
            // (including the unmarked-base nudge). Gives the player time to load in and mark a base.
            // EnsureSpawnTime is a fallback in case the OnSpawned Harmony patch didn't fire.
            SpawnTracker.EnsureSpawnTime();
            bool loginGraceActive = SpawnTracker.IsWithinLoginGrace;

            bool suppressed = now < _suppressUntil || deathGraceActive || loginGraceActive;

            // Unmarked-base reminder: if the user is on Marked mode but hasn't pressed F7 yet (no center
            // set), nothing counts as base -- the low-food nudge won't be suppressed at home. Periodically
            // remind them to mark it, until they do. Uses its own cooldown and respects the global popup
            // spacer + the same suppression windows as the other triggers. Suppressed during login grace
            // so it can't fire before the player has had a chance to mark.
            if (RemindIfUnmarked.Value && !suppressed && IsMarkedModeWithoutMark())
            {
                ReminderEngine.TryUnmarkedReminder(now);
            }

            ReminderEngine.Tick(food, suppressed);
        }

        /// <summary>True when BaseZoneMode is Marked but MarkedBaseCenter is blank/unparseable.</summary>
        private static bool IsMarkedModeWithoutMark()
        {
            string mode = (BaseZoneMode.Value ?? "").Trim();
            if (mode != "Marked") return false;
            return string.IsNullOrWhiteSpace(MarkedBaseCenter.Value);
        }

        /// <summary>
        ///   Returns Player.m_timeSinceDeath if readable (Valheim maintains this continuously, through
        ///   death and respawn, and persists it across save/load). Null if the field isn't available.
        ///   Used as the sole death signal because Character.IsDead() is a stripped stub in this build.
        /// </summary>
        private static float? GetTimeSinceDeath(Player local)
        {
            if (_timeSinceDeathField == null)
                _timeSinceDeathField = HarmonyLib.AccessTools.Field(typeof(Player), "m_timeSinceDeath");
            if (_timeSinceDeathField == null) return null;
            if (_timeSinceDeathField.GetValue(local) is float f) return f;
            return null;
        }

        /// <summary>
        ///   True while the player is mid-teleport or the world is loading. Verified public signals:
        ///   Player.IsTeleporting() and Player.m_isLoading. Both are transient; once either flips
        ///   false the destination is settled enough to read food + base zone reliably.
        /// </summary>
        private static bool IsPlayerTransitioning(Player local)
        {
            try
            {
                if (local.IsTeleporting()) return true;
            }
            catch { /* older build -- fall through to the field */ }
            // m_isLoading is private; read it defensively via reflection cache. Covers world-load
            // (login) in addition to in-game portals/teleports.
            if (_isLoadingField == null)
                _isLoadingField = HarmonyLib.AccessTools.Field(typeof(Player), "m_isLoading");
            if (_isLoadingField != null)
            {
                object v = _isLoadingField.GetValue(local);
                if (v is bool b && b) return true;
            }
            return false;
        }
        private static System.Reflection.FieldInfo _isLoadingField;
        private static bool _wasTransitioning;
        private static System.Reflection.FieldInfo _timeSinceDeathField;
        private static bool _inDeathGrace;

        internal static void Debug(string msg)
        {
            // LogDebug is dropped at the default Info log level; use LogInfo so diagnostics are visible.
            if (DebugLogging.Value) Log.LogInfo($"[debug] {msg}");
        }
    }
}
