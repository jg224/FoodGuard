using System.Globalization;
using UnityEngine;

namespace FoodGuard
{
    /// <summary>
    ///   Answers two questions for ReminderEngine:
    ///     - Is the local player currently "in base"?
    ///     - Did the player just cross the base boundary (in -> out or out -> in)?
    ///
    ///   "BASE" DEFINITION (configurable via BaseZoneMode):
    ///     Marked (default, recommended) = a single location YOU mark with the MarkBaseHotkey (F7).
    ///         'In base' = within MarkedBaseRadius meters of MarkedBaseCenter. Only YOUR marked spot
    ///         counts -- a walled farm in the plains with a workbench will NOT count, which is the
    ///         whole point. The center is parsed from the cfg string "x,y,z" each poll.
    ///     CraftingStation = inside any PlayerBase EffectArea (the workbench/forge crafting radius).
    ///         Broad; trips on any bench, including farms/outposts.
    ///     Building        = inside a WarmCozyArea (a roofed, enclosed interior with a fire).
    ///     Both            = either of the two above.
    ///
    ///   EDGE CROSSINGS
    ///   ReminderEngine's #1 (leave-base) trigger must fire exactly once on crossing out, and rearm on
    ///   crossing back in. We track the previous in-base state and report a transition flag when it
    ///   flips. We also wait one tick of stability (CurrentIsInBase == prev on two consecutive polls)
    ///   before committing a transition, to debounce the radius boundary which can jitter when the
    ///   player walks the exact edge.
    /// </summary>
    internal static class BaseZoneChecker
    {
        // EffectArea.Type bit flags (verified via Cecil). Used by the CraftingStation/Building modes only.
        private const EffectArea.Type CraftingStationFlag = EffectArea.Type.PlayerBase;     // 4
        private const EffectArea.Type BuildingFlag        = EffectArea.Type.WarmCozyArea;  // 64

        // The committed in-base state used for edge-crossing detection. Null until the first stable reading.
        private static bool? _committedInBase;
        // The reading from the previous poll, used for the one-tick debounce.
        private static bool _prevReading;

        /// <summary>
        ///   Poll once. Returns the current in-base state plus whether the base boundary was crossed
        ///   since the last poll (committed, debounced).
        /// </summary>
        public struct BaseState
        {
            public bool IsInBase;
            public bool LeftBase;   // true on the poll where a committed in->out transition completes
            public float DistanceToBase; // meters to the marked center (only meaningful in Marked mode)
        }

        public static BaseState Evaluate(Player localPlayer)
        {
            Vector3 pos = localPlayer.transform.position;
            var state = new BaseState { IsInBase = false };

            string mode = (Plugin.BaseZoneMode.Value ?? "Marked").Trim();
            bool reading;
            float dist = -1f;

            switch (mode)
            {
                case "CraftingStation":
                    reading = InsideAny(pos, CraftingStationFlag);
                    break;
                case "Building":
                    reading = InsideAny(pos, BuildingFlag);
                    break;
                case "Both":
                    reading = InsideAny(pos, CraftingStationFlag) || InsideAny(pos, BuildingFlag);
                    break;
                case "Marked":
                default:
                    reading = InsideMarked(pos, out dist);
                    break;
            }

            state.IsInBase = reading;
            state.DistanceToBase = dist;

            // Debounce: only commit a transition after the new reading has been stable for one poll.
            // Without this, walking the exact radius edge fires the leave-base trigger repeatedly.
            if (reading != _prevReading)
            {
                // Boundary may be jittering; record and wait for the next poll to confirm.
                _prevReading = reading;
                return state;   // LeftBase stays false this poll
            }

            // Reading is stable across two polls. Commit a transition if the committed state differs.
            if (!_committedInBase.HasValue)
            {
                _committedInBase = reading;
            }
            else if (_committedInBase.Value != reading)
            {
                _committedInBase = reading;
                state.LeftBase = !reading;   // committed state just became "out" -> we left base
            }

            return state;
        }

        /// <summary>True if the player is within MarkedBaseRadius of the marked center. dist = distance.</summary>
        private static bool InsideMarked(Vector3 pos, out float dist)
        {
            dist = -1f;
            if (!TryParseCenter(Plugin.MarkedBaseCenter.Value, out Vector3 center)) return false;
            dist = Vector3.Distance(pos, center);
            return dist <= Plugin.MarkedBaseRadius.Value;
        }

        /// <summary>Parses "x,y,z" (invariant culture) into a Vector3. Returns false if blank/malformed.</summary>
        private static bool TryParseCenter(string s, out Vector3 center)
        {
            center = Vector3.zero;
            if (string.IsNullOrWhiteSpace(s)) return false;
            string[] parts = s.Split(',');
            if (parts.Length != 3) return false;
            if (!float.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float x) ||
                !float.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float y) ||
                !float.TryParse(parts[2].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float z))
                return false;
            center = new Vector3(x, y, z);
            return true;
        }

        /// <summary>
        ///   True if a base-flagged EffectArea overlaps the given point. IsPointInsideArea returns the
        ///   area instance if inside, null otherwise. We only need the truthiness.
        /// </summary>
        private static bool InsideAny(Vector3 pos, EffectArea.Type flag)
        {
            // The 3rd arg is a range (extra radius). 0 = exact point test against the area collider.
            return EffectArea.IsPointInsideArea(pos, flag, 0f) != null;
        }

        /// <summary>Reset edge-crossing state (call on world load / respawn / re-mark base).</summary>
        public static void Reset()
        {
            _committedInBase = null;
            _prevReading = false;
        }
    }
}
