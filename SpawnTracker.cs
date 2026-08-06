using HarmonyLib;
using UnityEngine;

namespace FoodGuard
{
    /// <summary>
    ///   Tracks when the local player last spawned into the world, so Plugin.Update can enforce the
    ///   LoginGraceSeconds window (suppress all reminders right after login/respawn).
    ///
    ///   HOW IT WORKS
    ///   Valheim calls Player.OnSpawned(bool) once the player has finished materializing in the world
    ///   -- on initial login AND on respawn after death. We Harmony-postfix that method and stamp the
    ///   wall-clock time. If the field is never read (e.g. the patch fails), we fall back to the first
    ///   frame we see a local player, which is close enough.
    ///
    ///   WHY OnSpawned INSTEAD OF A TIMER FIELD
    ///   I checked the Player fields via Cecil -- there's no 'm_timeSinceSpawn' analog to m_timeSinceDeath.
    ///   The spawn-time candidates are either private update timers (m_nearFireTimer, etc.) that get reset
    ///   constantly, or compile-time constants. OnSpawned is the single authoritative 'you just entered
    ///   the world' callback. Verified it's a real method (19 IL instructions, public, non-virtual).
    /// </summary>
    internal static class SpawnTracker
    {
        /// <summary>
        ///   Wall-clock seconds at which the local player last spawned. Null until we've seen either an
        ///   OnSpawned callback or the first Update with a local player.
        /// </summary>
        public static float? LastSpawnTime;

        /// <summary>True if Plugin.LoginGraceSeconds has NOT yet elapsed since the last spawn.</summary>
        public static bool IsWithinLoginGrace
        {
            get
            {
                if (!LastSpawnTime.HasValue) return true;   // haven't spawned yet -> treat as in-grace
                float elapsed = Time.realtimeSinceStartup - LastSpawnTime.Value;
                return elapsed < Plugin.LoginGraceSeconds.Value;
            }
        }

        /// <summary>
        ///   Fallback: call this the first time we see a local player in Plugin.Update, so there's always
        ///   a spawn stamp even if the OnSpawned patch somehow didn't fire.
        /// </summary>
        public static void EnsureSpawnTime()
        {
            if (!LastSpawnTime.HasValue)
            {
                LastSpawnTime = Time.realtimeSinceStartup;
                Plugin.Debug($"SpawnTracker: fallback spawn stamp set (no OnSpawned yet).");
            }
        }

        /// <summary>
        ///   Harmony patch on Player.OnSpawned. Stamps the spawn time whenever ANY player spawns; the
        ///   postfix filters to only the local player so we don't track other players' spawns.
        /// </summary>
        [HarmonyPatch(typeof(Player), nameof(Player.OnSpawned))]
        private static class OnSpawnedPatch
        {
            private static void Postfix(Player __instance)
            {
                // Only the local player matters for our reminders.
                if (!ReferenceEquals(__instance, Player.m_localPlayer)) return;
                LastSpawnTime = Time.realtimeSinceStartup;
                Plugin.Debug($"SpawnTracker: OnSpawned fired for local player at {LastSpawnTime.Value:F1}.");
            }
        }
    }
}
