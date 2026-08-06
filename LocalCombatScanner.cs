using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace FoodGuard
{
    /// <summary>
    ///   Answers one question for ReminderEngine: is the LOCAL player currently in combat?
    ///
    ///   Adapted from sleepguard/CombatScanner.cs (which gates the SleepSkip vote for ANY player).
    ///   The difference: sleepguard returns a 3-state verdict for the whole server; this returns a
    ///   bool for the local player only, because FoodGuard is client-side and only cares about the
    ///   person seeing the popup.
    ///
    ///   APPROACH
    ///   Character.GetAllCharacters() returns every loaded character (players + creatures). We walk it
    ///   looking for any creature whose BaseAI has the LOCAL player as its current target. If found,
    ///   the local player is in combat. We add a short cooldown window (CombatClearSeconds) after the
    ///   last aggro so the "in combat" signal doesn't flicker off the instant a creature retargets.
    ///
    ///   API SURFACE (verified via Cecil -- same as sleepguard/CombatScanner.cs):
    ///     Character.GetAllCharacters()  static, public  -> List&lt;Character&gt;
    ///     Character.m_baseAI            protected field  -> BaseAI   (players/critters have null)
    ///     BaseAI.GetTargetCreature()    public           -> Character (null if no target)
    ///   Plus a local-player identity check: a Character is the local player if it IS Player.m_localPlayer.
    /// </summary>
    internal static class LocalCombatScanner
    {
        private static readonly FieldInfo BaseAIField =
            AccessTools.Field(typeof(Character), "m_baseAI");

        // Wall-clock seconds at which local-player combat was last observed. Null = never.
        private static float? _lastLocalCombatTime;

        // Brief clear-window so "in combat" doesn't flicker on retargeting. Matches sleepguard's model.
        private const float CombatClearSeconds = 5f;

        /// <summary>
        ///   True if the local player is currently in combat (or was within the last CombatClearSeconds).
        /// </summary>
        public static bool IsLocalPlayerInCombat(Player localPlayer)
        {
            float now = UnityEngine.Time.realtimeSinceStartup;

            List<Character> characters = Character.GetAllCharacters();
            if (characters == null) return InCombatWithCooldown(now);

            bool inCombatNow = false;

            for (int i = 0; i < characters.Count; i++)
            {
                Character c = characters[i];
                if (c == null) continue;

                BaseAI ai = BaseAIField?.GetValue(c) as BaseAI;
                if (ai == null) continue;                 // players and some critters have no BaseAI

                Character target = ai.GetTargetCreature();
                if (target == null) continue;              // not aggro on anything

                // Only counts if THIS creature is targeting the LOCAL player specifically.
                if (ReferenceEquals(target, localPlayer))
                {
                    inCombatNow = true;
                    break;
                }
            }

            if (inCombatNow)
            {
                _lastLocalCombatTime = now;
                return true;
            }

            return InCombatWithCooldown(now);
        }

        /// <summary>Forgets stale combat history (call on world load / respawn).</summary>
        public static void Reset() => _lastLocalCombatTime = null;

        private static bool InCombatWithCooldown(float now)
        {
            if (!_lastLocalCombatTime.HasValue) return false;
            return (now - _lastLocalCombatTime.Value) < CombatClearSeconds;
        }
    }
}
