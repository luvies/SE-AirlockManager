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
    internal partial class Program
    {
        private enum MovingStatus
        {
            ToInside,
            ToOutside,
        }

        private struct StoredStatus
        {
            public MovingStatus MovingStatus { get; }
            public DateTime? VentStartTime { get; }

            public StoredStatus(MovingStatus movingStatus, DateTime? ventStartTime)
            {
                MovingStatus = movingStatus;
                VentStartTime = ventStartTime;
            }
        }

        private class StoredStatusManager
        {
            private readonly IEnumerable<IMyTerminalBlock> _blocks;

            public StoredStatusManager(IEnumerable<IMyTerminalBlock> blocks)
            {
                _blocks = blocks;
            }

            public StoredStatus Status
            {
                get
                {
                    foreach (var block in _blocks)
                    {
                        var split = block.CustomData.Split(',');

                        if (split.Length == 2)
                        {
                            MovingStatus movingStatus;

                            if (!Enum.TryParse(split[0], out movingStatus))
                            {
                                continue;
                            }

                            if (split[1] == "")
                            {
                                return new StoredStatus(movingStatus, null);
                            }

                            DateTime ventStartTime;

                            if (!DateTime.TryParse(split[1], out ventStartTime))
                            {
                                continue;
                            }

                            return new StoredStatus(movingStatus, ventStartTime);
                        }
                    }

                    return new StoredStatus(MovingStatus.ToInside, DateTime.MinValue);
                }

                set
                {
                    var data = $"{value.MovingStatus.ToString()},{value.VentStartTime?.ToString("O") ?? ""}";
                    foreach (var block in _blocks)
                    {
                        block.CustomData = data;
                    }
                }
            }
        }
    }
}
