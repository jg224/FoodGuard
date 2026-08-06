using System.Reflection;
using HarmonyLib;

namespace FoodGuard
{
    /// <summary>
    ///   Answers one question for ReminderEngine: does the local player currently have the Rested buff?
    ///
    ///   WHY IT MATTERS
    ///   Going into combat without Rested means fighting with no health/stamina regen bonus -- a
    ///   common cause of deaths for the forgetful player this mod is built for. The combat-no-rest
    ///   trigger (#5) nudges them to find a fire/shelter before engaging.
    ///
    ///   API SURFACE (verified via Cecil -- see _verify_seman.ps1):
    ///     Character.m_seman           private field  -> SEMan (the per-character status-effect manager)
    ///     SEMan.HaveStatusEffect(int) public          -> bool  (true if the named effect is active)
    ///     SEMan.s_statusEffectRested  public static   -> int   (the Rested effect name-hash)
    ///
    ///   We use SEMan.s_statusEffectRested directly as the hash -- no string guessing, no prefab
    ///   lookup. Valheim itself populates this static in ObjectDB.Awake, so it is valid once the
    ///   player is in-world (which is exactly when ReminderEngine ticks).
    /// </summary>
    internal static class RestStatusChecker
    {
        private static readonly FieldInfo SemanField =
            AccessTools.Field(typeof(Character), "m_seman");

        /// <summary>True if the local player currently has the Rested status effect.</summary>
        public static bool IsRested(Player local)
        {
            if (SemanField == null)
            {
                Plugin.Debug("m_seman field not found via AccessTools; rest check inactive.");
                return true;   // fail-open: if we can't read it, don't nag about rest
            }

            SEMan seman = SemanField.GetValue(local) as SEMan;
            if (seman == null) return true;   // not ready yet; treat as rested to avoid false nags

            // s_statusEffectRested is a public static int set up by ObjectDB. Defensive: if for some
            // reason it's 0, treat as rested (fail-open).
            int restedHash = SEMan.s_statusEffectRested;
            if (restedHash == 0) return true;

            try
            {
                return seman.HaveStatusEffect(restedHash);
            }
            catch (System.Exception e)
            {
                Plugin.Debug($"HaveStatusEffect threw (non-fatal, treated as rested): {e.Message}");
                return true;
            }
        }
    }
}
