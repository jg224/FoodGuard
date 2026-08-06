using System.Text;
using UnityEngine;

namespace FoodGuard
{
    /// <summary>
    ///   On-demand diagnostic dump, triggered by the DebugHotkey (default F8). Shows the EXACT state
    ///   the mod sees right now -- food slots with their remaining %, base-zone verdict, combat flag,
    ///   cooldowns -- both as a center-screen popup and a BepInEx log line.
    ///
    ///   WHY THIS EXISTS
    ///   Field reports of "it fired when it shouldn't" or "it didn't fire when it should" are hard to
    ///   reproduce blind. Press F8 at the moment the behavior happens (e.g. while sailing with low food)
    ///   and the dump says precisely what the mod read, so the cause is obvious without guessing.
    /// </summary>
    internal static class Diagnostics
    {
        public static void DumpState(Player local)
        {
            FoodMonitor.FoodState food = FoodMonitor.Evaluate(local);
            BaseZoneChecker.BaseState zone = BaseZoneChecker.Evaluate(local);
            bool inCombat = LocalCombatScanner.IsLocalPlayerInCombat(local);
            bool transitioning = IsTransitioning(local);

            // Build a compact one-screen summary.
            string mode = (Plugin.BaseZoneMode.Value ?? "?").Trim();
            var sb = new StringBuilder();
            sb.Append("[FoodGuard] active=").Append(food.ActiveFoodCount).Append("/3");
            sb.Append(" lowest=").Append(food.LowestRemainingPct).Append("%");
            sb.Append(food.NeedsFood ? " NEEDS" : " ok");
            sb.Append(" | mode=").Append(mode);
            sb.Append(" base=").Append(zone.IsInBase ? "IN" : "out");
            if (zone.DistanceToBase >= 0f)
                sb.Append(" (").Append(zone.DistanceToBase.ToString("F0")).Append("m/").Append(Plugin.MarkedBaseRadius.Value.ToString("F0")).Append("m)");
            else if (mode == "Marked")
                sb.Append(" (no mark set)");
            sb.Append(" | rested=").Append(RestStatusChecker.IsRested(local) ? "yes" : "NO");
            sb.Append(" | combat=").Append(inCombat ? "yes" : "no");
            sb.Append(" port=").Append(transitioning ? "yes" : "no");
            // Death signal: Character.IsDead() is a stripped stub in this build, so we use
            // m_timeSinceDeath -- if it's small, we died recently. Grace window matches the engine.
            float? tsd = GetTimeSinceDeath(local);
            if (tsd.HasValue)
            {
                bool inGrace = tsd.Value < Plugin.RespawnGraceSeconds.Value;
                sb.Append(" tDth=").Append(tsd.Value.ToString("F0")).Append("s");
                if (inGrace) sb.Append(" DEAD-GRACE");
            }

            string summary = sb.ToString();

            // Log it (always, at Info, so it's visible without enabling DebugMode).
            Plugin.Log.LogInfo(summary);
            // And show it center-screen so the player sees it immediately.
            ShowPopup(summary);

            // Also log the per-slot breakdown (log only -- too long for a popup).
            Plugin.Log.LogInfo(PerSlotBreakdown(local));

            // And the base-mode details (log only): which mode, the raw mark string, your coords.
            Vector3 p = local.transform.position;
            Plugin.Log.LogInfo($"[FoodGuard] base mode='{Plugin.BaseZoneMode.Value}' " +
                               $"mark='{Plugin.MarkedBaseCenter.Value}' radius={Plugin.MarkedBaseRadius.Value} " +
                               $"you=({p.x:F1},{p.y:F1},{p.z:F1}) base={(zone.IsInBase ? "IN" : "out")} " +
                               $"dist={(zone.DistanceToBase >= 0 ? zone.DistanceToBase.ToString("F1") : "n/a")}m");
        }

        private static string PerSlotBreakdown(Player local)
        {
            var f = HarmonyLib.AccessTools.Field(typeof(Player), "m_foods");
            if (f == null) return "[FoodGuard] m_foods field unavailable";
            var foods = f.GetValue(local) as System.Collections.Generic.List<Player.Food>;
            if (foods == null) return "[FoodGuard] m_foods list null";

            var sb = new StringBuilder();
            sb.Append("[FoodGuard] slots: ");
            for (int i = 0; i < foods.Count; i++)
            {
                Player.Food food = foods[i];
                if (food == null || food.m_item == null || food.m_item.m_shared == null)
                {
                    sb.Append("[empty] ");
                    continue;
                }
                float burn = food.m_item.m_shared.m_foodBurnTime;
                if (burn <= 0f) burn = 600f;
                int pct = UnityEngine.Mathf.RoundToInt(UnityEngine.Mathf.Clamp01(food.m_time / burn) * 100f);
                string name = food.m_item.m_shared.m_name;
                if (name == null) name = "?";
                sb.Append("[").Append(name).Append(" ").Append(pct).Append("%] ");
            }
            return sb.ToString().TrimEnd();
        }

        private static bool IsTransitioning(Player local)
        {
            try { if (local.IsTeleporting()) return true; } catch { }
            var f = HarmonyLib.AccessTools.Field(typeof(Player), "m_isLoading");
            if (f != null && f.GetValue(local) is bool b && b) return true;
            return false;
        }

        private static float? GetTimeSinceDeath(Player local)
        {
            var f = HarmonyLib.AccessTools.Field(typeof(Player), "m_timeSinceDeath");
            if (f != null && f.GetValue(local) is float v) return v;
            return null;
        }

        private static void ShowPopup(string text)
        {
            MessageHud mh = MessageHud.instance;
            if (mh == null) return;
            try { mh.ShowMessage(MessageHud.MessageType.Center, text); }
            catch { /* non-fatal */ }
        }
    }
}
