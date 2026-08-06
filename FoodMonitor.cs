using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace FoodGuard
{
    /// <summary>
    ///   Reads the local player's food state and answers the questions ReminderEngine needs answered:
    ///     - Does the player "need food" right now? (any active food at/under threshold, or an empty slot)
    ///     - Does the player have NO food eaten at all?
    ///     - What is the lowest remaining-food percentage (for the {pct} message token)?
    ///
    ///   API SURFACE (verified via Cecil -- see _verify_apis.ps1 and _dump_updatefood.ps1):
    ///     Player.m_foods        private field  -> List&lt;Player.Food&gt;     (Food is a CLASS)
    ///     Player.Food.m_time    public field   -> float   (REMAINING seconds; counts DOWN to 0)
    ///     Player.Food.m_item    public field   -> ItemDrop.ItemData
    ///     ItemData.m_shared     public field   -> SharedData
    ///     SharedData.m_foodBurnTime public field -> float (TOTAL duration in seconds)
    ///
    ///   REMAINING-FRACTION MATH (matches vanilla UpdateFood exactly)
    ///     UpdateFood does: m_time -= dt; and on each tick computes fraction = m_time / m_foodBurnTime,
    ///     then removes the food when m_time hits 0. So m_time is REMAINING seconds (counts DOWN).
    ///     We use the same fraction: fractionRemaining = m_time / m_foodBurnTime, clamped to [0,1].
    ///     "20% remaining" = fractionRemaining <= 0.20.
    ///
    ///   HISTORY NOTE: an earlier version treated m_time as elapsed and used (burnTime - m_time)/burnTime.
    ///   That inversion caused exactly the wrong behavior -- freshly eaten food (~100% remaining) read as
    ///   ~0% and fired popups right after eating, while genuinely low food (~0% remaining) read as ~100%
    ///   and never fired. The full UpdateFood IL (sub at IL_005e, remove at IL_0107) confirms DOWN.
    ///
    ///   m_foods is private, so we read it through AccessTools (the same reflection-by-name pattern
    ///   sleepguard uses for Character.m_baseAI). The list length is always Player.m_maxFoods (=3 on
    ///   this build); an empty slot is null in the list. We treat null per EmptySlotCountsAsLow.
    /// </summary>
    internal static class FoodMonitor
    {
        private static readonly FieldInfo FoodsField =
            AccessTools.Field(typeof(Player), "m_foods");

        /// <summary>
        ///   Snapshot of the food state the reminder engine cares about. Computed once per poll tick.
        /// </summary>
        public struct FoodState
        {
            public bool NeedsFood;          // any food at/under threshold, or an empty slot (if config says so)
            public bool HasNoFood;          // every slot empty (nothing eaten at all)
            public int LowestRemainingPct;  // 0-100, lowest remaining% among active foods; 0 if none active
            public int ActiveFoodCount;     // number of slots currently holding a readable food item
            public int EmptyFoodSlots;      // number of empty/expired slots (= m_maxFoods - ActiveFoodCount)
        }

        /// <summary>Reads the local player's foods and returns the verdict. Safe if m_foods can't be read.</summary>
        public static FoodState Evaluate(Player player)
        {
            var state = new FoodState();

            if (FoodsField == null)
            {
                Plugin.Debug("m_foods field not found via AccessTools; FoodMonitor inactive.");
                return state;
            }

            List<Player.Food> foods = FoodsField.GetValue(player) as List<Player.Food>;
            if (foods == null || foods.Count == 0)
            {
                // No food list / nothing eaten.
                state.HasNoFood = true;
                state.NeedsFood = true;
                return state;
            }

            float thresholdFraction = Plugin.FoodThresholdPercent.Value / 100f;
            float lowestFraction = 2f;          // >1 so "no active food" stays distinguishable from "0% left"
            bool anyActiveFood = false;
            bool anyLow = false;
            int activeCount = 0;

            for (int i = 0; i < foods.Count; i++)
            {
                Player.Food food = foods[i];
                if (food == null || food.m_item == null || food.m_item.m_shared == null)
                {
                    // Empty slot. Counts as "low" if the user opted into that.
                    if (Plugin.EmptySlotCountsAsLow.Value) anyLow = true;
                    continue;
                }

                float burn = food.m_item.m_shared.m_foodBurnTime;
                if (burn <= 0f) burn = 600f;    // defensive: should never be 0, but avoid div-by-zero
                // m_time counts DOWN (remaining seconds), per UpdateFood IL. fraction = remaining/total.
                float remaining = food.m_time;
                if (remaining < 0f) remaining = 0f;
                float fraction = remaining / burn;
                if (fraction > 1f) fraction = 1f;

                anyActiveFood = true;
                activeCount++;
                if (fraction < lowestFraction) lowestFraction = fraction;
                if (fraction <= thresholdFraction) anyLow = true;
            }

            state.HasNoFood = !anyActiveFood;
            state.NeedsFood = anyLow || (Plugin.EmptySlotCountsAsLow.Value && !anyActiveFood);
            state.ActiveFoodCount = activeCount;
            // Empty slots = list capacity minus active. The m_foods list length IS the slot capacity
            // (Valheim keeps it at Player.m_maxFoods = 3); an empty/expired slot stays in the list as
            // a null or unreadable entry. So empty = list.Count - activeCount.
            int capacity = foods.Count;
            if (capacity < activeCount) capacity = activeCount;   // defensive: never report negative
            state.EmptyFoodSlots = capacity - activeCount;
            state.LowestRemainingPct = anyActiveFood
                ? Mathf.RoundToInt(lowestFraction * 100f)
                : 0;
            return state;
        }
    }
}
