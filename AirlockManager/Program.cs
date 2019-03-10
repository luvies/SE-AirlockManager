using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage.Game.ObjectBuilders.Definitions;
using VRageMath;

namespace IngameScript
{
    internal partial class Program : MyGridProgram
    {
        private const bool ChangeEnabledState = false;

        private System.Text.RegularExpressions.Regex _airlockInnerRe = new System.Text.RegularExpressions.Regex(
            @"\[Airlock ([0-9]+) Inner\]",
            System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.IgnoreCase
        );
        private System.Text.RegularExpressions.Regex _airlockOuterRe = new System.Text.RegularExpressions.Regex(
            @"\[Airlock ([0-9]+) Outer\]",
            System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.IgnoreCase
        );
        private System.Text.RegularExpressions.Regex _airlockVentRe = new System.Text.RegularExpressions.Regex(
            @"\[Airlock ([0-9]+) Vent\]",
            System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.IgnoreCase
        );

        private BadArgumentException _argumentException = null;

        public Program()
        {
            Runtime.UpdateFrequency = UpdateFrequency.Update10;
        }

        public void Main(string argument, UpdateType updateSource)
        {
            var airlocks = FindAirlocks();

            if (updateSource != UpdateType.Update10 && !string.IsNullOrWhiteSpace(argument))
            {
                try
                {
                    HandleInput(argument, airlocks);
                    _argumentException = null;
                }
                catch (BadArgumentException ex)
                {
                    _argumentException = ex;
                    DisplayBadArgument();
                }
            }
            else
            {
                DisplayBadArgument();

                foreach (var airlock in airlocks.Values)
                {
                    airlock.Process();
                }
            }
        }

        private void HandleInput(string argument, Dictionary<int, Airlock> airlocks)
        {
            var split = argument.Trim().ToLower().Split(':');
            if (split.Length != 2)
            {
                throw new BadArgumentException(argument, "Argument must be in the form '{toggle|inner|outer}:{number}'");
            }

            int airlockNumber;

            if (!int.TryParse(split[1], out airlockNumber))
            {
                throw new BadArgumentException(argument, "Second parameter must be a number");
            }

            if (!airlocks.ContainsKey(airlockNumber))
            {
                throw new BadArgumentException(argument, $"Airlock {airlockNumber} cannot be found or is not complete");
            }

            var airlock = airlocks[airlockNumber];
            switch (split[0])
            {
                case "toggle":
                    airlock.Toggle();
                    break;
                case "inner":
                    airlock.OpenInner();
                    break;
                case "outer":
                    airlock.OpenOuter();
                    break;
                default:
                    throw new BadArgumentException(argument, $"First parameter '{split[0]}' is not valid, it must be one of 'toggle', 'inner', or 'outer'");
            }
        }

        private void DisplayBadArgument()
        {
            if (_argumentException != null)
            {
                Echo(_argumentException.Message);
            }
        }

        private Dictionary<int, Airlock> FindAirlocks()
        {
            var inner = new Dictionary<int, List<IMyDoor>>();
            var outer = new Dictionary<int, List<IMyDoor>>();
            var vents = new Dictionary<int, List<IMyAirVent>>();
            var scan = new List<IMyTerminalBlock>();

            GridTerminalSystem.GetBlocks(scan);
            foreach (var block in scan)
            {
                var door = block as IMyDoor;
                var vent = block as IMyAirVent;

                if (door != null)
                {
                    if (!TryAddBlock(door, inner, _airlockInnerRe))
                    {
                        TryAddBlock(door, outer, _airlockOuterRe);
                    }
                }
                else if (vent != null)
                {
                    TryAddBlock(vent, vents, _airlockVentRe);
                }
            }

            // Go over all inner doors and attempt to build the full airlock for it.
            var airlocks = new Dictionary<int, Airlock>();
            foreach (var kv in inner)
            {
                var missing = new List<string>();
                if (!outer.ContainsKey(kv.Key))
                {
                    missing.Add("outer door");
                }
                if (!vents.ContainsKey(kv.Key))
                {
                    missing.Add("air vent");
                }
                if (missing.Count > 0)
                {
                    if (outer.ContainsKey(kv.Key))
                    {
                        outer.Remove(kv.Key);
                    }
                    if (vents.ContainsKey(kv.Key))
                    {
                        vents.Remove(kv.Key);
                    }

                    NotifyIncompleteAirlock(kv.Key, missing);
                    continue;
                }

                airlocks.Add(kv.Key, new Airlock(kv.Value, outer[kv.Key], vents[kv.Key]));

                // Remove the outer and vents to make sure we know we found them.
                outer.Remove(kv.Key);
                vents.Remove(kv.Key);
            }

            // Go over all remaining outer doors and vents to notify incompleteness.
            foreach (var number in outer.Keys)
            {
                var missing = new List<string> { "inner door" };
                if (vents.ContainsKey(number))
                {
                    vents.Remove(number);
                }
                else
                {
                    missing.Add("air vent");
                }
                NotifyIncompleteAirlock(number, missing);
            }
            foreach (var number in vents.Keys)
            {
                NotifyIncompleteAirlock(number, new[] { "inner door", "outer door" });
            }

            return airlocks;
        }

        private bool TryAddBlock<T>(T block, Dictionary<int, List<T>> dict, System.Text.RegularExpressions.Regex re) where T : IMyTerminalBlock
        {
            var match = re.Match(block.CustomName);
            if (match.Success)
            {
                var number = int.Parse(match.Groups[1].Value);
                if (dict.ContainsKey(number))
                {
                    dict[number].Add(block);
                }
                else
                {
                    dict[number] = new List<T> { block };
                }

                return true;
            }

            return false;
        }

        private void NotifyIncompleteAirlock(int number, IEnumerable<string> missing)
        {
            Echo($"Airlock {number} is incomplete, missing: {string.Join(", ", missing)}");
        }
    }
}