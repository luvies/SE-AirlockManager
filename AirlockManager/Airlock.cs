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
        private enum AirlockStatus
        {
            InsideOpening,
            InsideOpen,
            InsideClosing,
            WaitingToDepressurize,
            Depressurizing,
            Depressurized,
            OutsideOpening,
            OutsideOpen,
            OutsideClosing,
            WaitingToPressurize,
            Pressurizing,
            Pressurized,
            Invalid,
        }

        private class Airlock
        {
            private const int VentMaxWaitTime = 15; // Seconds.

            private static AirlockStatus? GetOverallStatusFor(IEnumerable<IMyDoor> doors,
                MovingStatus movingStatus, MovingStatus targetMovingStatus,
                AirlockStatus open, AirlockStatus opening,
                AirlockStatus closing, AirlockStatus waiting,
                VentStatus ventStatus, VentStatus ventDone, VentStatus ventDoing)
            {
                DoorStatus? doorStatus = null;

                foreach (var door in doors)
                {
                    switch (doorStatus)
                    {
                        case null:
                            doorStatus = door.Status;
                            break;
                        case DoorStatus.Open:
                            switch (door.Status)
                            {
                                case DoorStatus.Open:
                                    continue;
                                case DoorStatus.Opening:
                                    doorStatus = DoorStatus.Opening;
                                    break;
                                default:
                                    return AirlockStatus.Invalid;
                            }
                            break;
                        case DoorStatus.Opening:
                            switch (door.Status)
                            {
                                case DoorStatus.Open:
                                case DoorStatus.Opening:
                                    continue;
                                default:
                                    return AirlockStatus.Invalid;
                            }
                        case DoorStatus.Closed:
                            switch (door.Status)
                            {
                                case DoorStatus.Closed:
                                    continue;
                                case DoorStatus.Closing:
                                    doorStatus = DoorStatus.Closing;
                                    break;
                                default:
                                    return AirlockStatus.Invalid;
                            }
                            break;
                        case DoorStatus.Closing:
                            switch (door.Status)
                            {
                                case DoorStatus.Closed:
                                case DoorStatus.Closing:
                                    continue;
                                default:
                                    return AirlockStatus.Invalid;
                            }
                    }
                }

                switch (doorStatus)
                {
                    case DoorStatus.Open:
                        return open;
                    case DoorStatus.Opening:
                        return opening;
                    case DoorStatus.Closing:
                        return closing;
                    case DoorStatus.Closed:
                        if (movingStatus == targetMovingStatus && ventStatus != ventDone && ventStatus != ventDoing)
                        {
                            return waiting;
                        }
                        break;
                }

                return null;
            }

            private readonly IEnumerable<IMyDoor> _inner;
            private readonly IEnumerable<IMyDoor> _outer;
            private readonly IEnumerable<IMyAirVent> _vents;
            private readonly StoredStatusManager _storedStatus;

            private AirlockStatus Status
            {
                get
                {
                    var storedStatus = _storedStatus.Status;

                    VentStatus? ventStatus = null;

                    foreach (var vent in _vents)
                    {
                        // Since vent status doesn't work properly right now,
                        // we do a manual check to work around it.
                        var status = vent.Status;
                        if (vent.GetOxygenLevel() <= 0.0001F)
                        {
                            status = VentStatus.Depressurized;
                        }

                        if (!ventStatus.HasValue)
                        {
                            ventStatus = status;
                        }
                        else if (status != ventStatus)
                        {
                            return AirlockStatus.Invalid;
                        }
                    }

                    var innerStatus = GetOverallStatusFor(_inner,
                        storedStatus.MovingStatus, MovingStatus.ToInside,
                        AirlockStatus.InsideOpen, AirlockStatus.InsideOpening,
                        AirlockStatus.InsideClosing, AirlockStatus.WaitingToPressurize,
                        ventStatus.Value, VentStatus.Pressurized, VentStatus.Pressurizing);

                    var outerStatus = GetOverallStatusFor(_outer,
                        storedStatus.MovingStatus, MovingStatus.ToOutside,
                        AirlockStatus.OutsideOpen, AirlockStatus.OutsideOpening,
                        AirlockStatus.OutsideClosing, AirlockStatus.WaitingToDepressurize,
                        ventStatus.Value, VentStatus.Depressurized, VentStatus.Depressurizing);

                    if (innerStatus.HasValue)
                    {
                        if (outerStatus == AirlockStatus.OutsideClosing)
                        {
                            return outerStatus.Value;
                        }
                        return innerStatus.Value;
                    }

                    if (outerStatus.HasValue)
                    {
                        if (innerStatus == AirlockStatus.InsideClosing)
                        {
                            return innerStatus.Value;
                        }
                        return outerStatus.Value;
                    }

                    var ventDiff = DateTime.Now - storedStatus.VentStartTime;

                    switch (ventStatus)
                    {
                        case VentStatus.Pressurized:
                            return AirlockStatus.Pressurized;
                        case VentStatus.Pressurizing:
                            if (ventDiff?.TotalSeconds >= VentMaxWaitTime)
                            {
                                return AirlockStatus.Pressurized;
                            }
                            return AirlockStatus.Pressurizing;
                        case VentStatus.Depressurized:
                            return AirlockStatus.Depressurized;
                        case VentStatus.Depressurizing:
                            if (ventDiff?.TotalSeconds >= VentMaxWaitTime)
                            {
                                return AirlockStatus.Depressurized;
                            }
                            return AirlockStatus.Depressurizing;
                    }

                    return AirlockStatus.Invalid;
                }
            }

            public Airlock(IEnumerable<IMyDoor> inner, IEnumerable<IMyDoor> outer, IEnumerable<IMyAirVent> vents)
            {
                _inner = inner;
                _outer = outer;
                _vents = vents;
                _storedStatus = new StoredStatusManager(inner.Cast<IMyTerminalBlock>().Concat(outer).Concat(vents));
            }

            public void Process()
            {
                var status = Status;
                switch (status)
                {
                    case AirlockStatus.InsideOpen:
                        if (ChangeEnabledState)
                        {
                            foreach (var door in _inner)
                            {
                                door.Enabled = false;
                            }
                        }
                        break;
                    case AirlockStatus.OutsideOpen:
                        if (ChangeEnabledState)
                        {
                            foreach (var door in _outer)
                            {
                                door.Enabled = false;
                            }
                        }
                        break;
                    case AirlockStatus.WaitingToPressurize:
                        foreach (var vent in _vents)
                        {
                            vent.Depressurize = false;
                        }
                        if (ChangeEnabledState)
                        {
                            foreach (var outer in _outer)
                            {
                                outer.Enabled = false;
                            }
                        }
                        _storedStatus.Status = new StoredStatus(MovingStatus.ToInside, DateTime.Now);
                        break;
                    case AirlockStatus.Pressurized:
                        foreach (var door in _inner)
                        {
                            door.Enabled = true;
                            door.OpenDoor();
                        }
                        break;
                    case AirlockStatus.Pressurizing:
                        if (_storedStatus.Status.VentStartTime == null)
                        {
                            _storedStatus.Status = new StoredStatus(MovingStatus.ToInside, DateTime.Now);
                        }
                        break;
                    case AirlockStatus.WaitingToDepressurize:
                        foreach (var vent in _vents)
                        {
                            vent.Depressurize = true;
                        }
                        if (ChangeEnabledState)
                        {
                            foreach (var inner in _inner)
                            {
                                inner.Enabled = false;
                            }
                        }
                        _storedStatus.Status = new StoredStatus(MovingStatus.ToOutside, DateTime.Now);
                        break;
                    case AirlockStatus.Depressurized:
                        foreach (var door in _outer)
                        {
                            door.Enabled = true;
                            door.OpenDoor();
                        }
                        break;
                    case AirlockStatus.Depressurizing:
                        if (_storedStatus.Status.VentStartTime == null)
                        {
                            _storedStatus.Status = new StoredStatus(MovingStatus.ToOutside, DateTime.Now);
                        }
                        break;
                }
            }

            public void OpenInner()
            {
                DateTime? ventStartTime = null;
                switch (Status)
                {
                    case AirlockStatus.InsideClosing:
                        foreach (var door in _inner)
                        {
                            door.OpenDoor();
                        }
                        break;
                    case AirlockStatus.WaitingToDepressurize:
                    case AirlockStatus.Depressurizing:
                    case AirlockStatus.Depressurized:
                        foreach (var vent in _vents)
                        {
                            vent.Depressurize = false;
                        }
                        ventStartTime = DateTime.Now;
                        break;
                    case AirlockStatus.OutsideOpening:
                    case AirlockStatus.OutsideOpen:
                        foreach (var door in _outer)
                        {
                            door.Enabled = true;
                            door.CloseDoor();
                        }
                        break;
                    case AirlockStatus.Invalid:
                        foreach (var door in _inner)
                        {
                            door.Enabled = true;
                            door.CloseDoor();
                        }
                        foreach (var door in _outer)
                        {
                            door.Enabled = true;
                            door.CloseDoor();
                        }
                        foreach (var vent in _vents)
                        {
                            vent.Depressurize = false;
                        }
                        ventStartTime = DateTime.Now;
                        break;
                }
                _storedStatus.Status = new StoredStatus(MovingStatus.ToInside, ventStartTime);
            }

            public void OpenOuter()
            {
                DateTime? ventStartTime = null;
                switch (Status)
                {
                    case AirlockStatus.InsideOpening:
                    case AirlockStatus.InsideOpen:
                        foreach (var door in _inner)
                        {
                            door.Enabled = true;
                            door.CloseDoor();
                        }
                        break;
                    case AirlockStatus.OutsideClosing:
                        foreach (var door in _outer)
                        {
                            door.OpenDoor();
                        }
                        break;
                    case AirlockStatus.WaitingToPressurize:
                    case AirlockStatus.Pressurizing:
                    case AirlockStatus.Pressurized:
                        foreach (var vent in _vents)
                        {
                            vent.Depressurize = true;
                        }
                        ventStartTime = DateTime.Now;
                        break;
                    case AirlockStatus.Invalid:
                        foreach (var door in _inner)
                        {
                            door.Enabled = true;
                            door.CloseDoor();
                        }
                        foreach (var door in _outer)
                        {
                            door.Enabled = true;
                            door.CloseDoor();
                        }
                        foreach (var vent in _vents)
                        {
                            vent.Depressurize = true;
                        }
                        ventStartTime = DateTime.Now;
                        break;
                }
                _storedStatus.Status = new StoredStatus(MovingStatus.ToOutside, ventStartTime);
            }

            public void Toggle()
            {
                switch (Status)
                {
                    case AirlockStatus.InsideOpen:
                    case AirlockStatus.InsideOpening:
                    case AirlockStatus.OutsideClosing:
                    case AirlockStatus.WaitingToPressurize:
                    case AirlockStatus.Pressurized:
                    case AirlockStatus.Pressurizing:
                        OpenOuter();
                        break;
                    default:
                        OpenInner();
                        break;
                }
            }
        }
    }
}
