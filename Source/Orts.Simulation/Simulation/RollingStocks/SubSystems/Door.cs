// COPYRIGHT 2022 by the Open Rails project.
// 
// This file is part of Open Rails.
// 
// Open Rails is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
// 
// Open Rails is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
// 
// You should have received a copy of the GNU General Public License
// along with Open Rails.  If not, see <http://www.gnu.org/licenses/>.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Orts.Common;
using Orts.Parsers.Msts;
using ORTS.Common;
using ORTS.Scripting.Api;

namespace Orts.Simulation.RollingStocks.SubSystems
{
    // Door state: do not change the order of this enum
    public enum DoorState
    {
        [GetParticularString("Door", "Closed")] Closed,
        [GetParticularString("Door", "Closing")] Closing,
        [GetParticularString("Door", "Opening")] Opening,
        [GetParticularString("Door", "Open")] Open,
    }
    public enum DoorSide
    {
        Left,
        Right,
        Both
    }
    public class Doors : ISubSystem<Doors>
    {
        public Door RightDoor;
        public Door LeftDoor;
        public bool SeparateCloseCommand { get; private set; }

        public Doors(MSTSWagon wagon)
        {
            LeftDoor = new Door(wagon, DoorSide.Left);
            RightDoor = new Door(wagon, DoorSide.Right);
        }

        public virtual void Parse(string lowercasetoken, STFReader stf)
        {
            switch (lowercasetoken)
            {
                case "wagon(ortsdoors(separateclosecommand":
                    SeparateCloseCommand = stf.ReadBoolBlock(false);
                    break;
                case "wagon(ortsdoors(closingdelay": 
                {
                    float delayS = stf.ReadFloatBlock(STFReader.UNITS.Time, 0f);
                    LeftDoor.ClosingDelayS = delayS;
                    RightDoor.ClosingDelayS = delayS;
                    break;
                }
                case "wagon(ortsdoors(openingdelay": 
                {
                    float delayS = stf.ReadFloatBlock(STFReader.UNITS.Time, 0f);
                    LeftDoor.OpeningDelayS = delayS;
                    RightDoor.OpeningDelayS = delayS;
                    break;
                }
                case "wagon(ortsdoors(releasemode":
                    LeftDoor.ReleaseMode = RightDoor.ReleaseMode = stf.ReadBoolBlock(false);
                    break;
                case "wagon(ortsdoors(randomopendelaymin":
                    LeftDoor.RandomOpenDelayMinS = RightDoor.RandomOpenDelayMinS = stf.ReadFloatBlock(STFReader.UNITS.Time, 2f);
                    break;
                case "wagon(ortsdoors(randomopendelaymax":
                    LeftDoor.RandomOpenDelayMaxS = RightDoor.RandomOpenDelayMaxS = stf.ReadFloatBlock(STFReader.UNITS.Time, 15f);
                    break;
                case "wagon(ortsdoors(openduration":
                    LeftDoor.OpenDurationS = RightDoor.OpenDurationS = stf.ReadFloatBlock(STFReader.UNITS.Time, 8f);
                    break;
                case "wagon(ortsdoors(doorgroups":
                    var groups = new List<string>();
                    stf.MustMatch("(");
                    while (!stf.EndOfBlock())
                    {
                        string group = stf.ReadString().ToUpperInvariant();
                        if (group.Length == 1 && group[0] >= 'A' && group[0] <= 'F') groups.Add(group);
                        else STFException.TraceWarning(stf, "DoorGroups accepts doorway letters A through F.");
                    }
                    LeftDoor.ConfigureGroups(groups.Where(group => group[0] >= 'D'));
                    RightDoor.ConfigureGroups(groups.Where(group => group[0] < 'D'));
                    break;
            }
        }

        // A1 and A2 are the leaves of doorway A and must move together.
        public static string GetDoorGroup(string matrixName) =>
            matrixName.StartsWith("DOOR_", StringComparison.Ordinal) && matrixName.Length >= 6
                && matrixName[5] >= 'A' && matrixName[5] <= 'F' ? matrixName.Substring(5, 1) : null;

        public Door GetDoorForGroup(string group) => group[0] >= 'D' ? LeftDoor : RightDoor;

        public void Copy(Doors other)
        {
            SeparateCloseCommand = other.SeparateCloseCommand;
            LeftDoor.Copy(other.LeftDoor);
            RightDoor.Copy(other.RightDoor);
        }

        public virtual void Initialize()
        {
            LeftDoor.Initialize();
            RightDoor.Initialize();
        }

        /// <summary>
        /// Initialization when simulation starts with moving train
        /// </summary>
        public virtual void InitializeMoving()
        {
        }

        public virtual void Save(BinaryWriter outf)
        {
            LeftDoor.Save(outf);
            RightDoor.Save(outf);
        }

        public virtual void Restore(BinaryReader inf)
        {
            LeftDoor.Restore(inf);
            RightDoor.Restore(inf);
        }

        public virtual void Update(float elapsedClockSeconds)
        {
            LeftDoor.Update(elapsedClockSeconds);
            RightDoor.Update(elapsedClockSeconds);
        }

        public static DoorSide FlippedDoorSide(DoorSide trainSide)
        {
            switch(trainSide)
            {
                case DoorSide.Left:
                    return DoorSide.Right;
                case DoorSide.Right:
                    return DoorSide.Left;
                default:
                    return DoorSide.Both;
            }
        }
    }
    public class Door : ISubSystem<Door>
    {
        
        // Parameters
        public float OpeningDelayS { get; set; } = 0f;
        public float ClosingDelayS { get; set; } = 0f;
        public bool ReleaseMode { get; set; }
        public float RandomOpenDelayMinS { get; set; } = 2f;
        public float RandomOpenDelayMaxS { get; set; } = 15f;
        public float OpenDurationS { get; set; } = 8f;
        public bool Released { get; private set; }
        public bool CommandOpen => ReleaseMode ? Released : State >= DoorState.Opening;

        // Omitting DoorGroups keeps a single shared timer for this wagon side.
        readonly List<Doorway> DoorwayList = new List<Doorway> { new Doorway("") };
        public IReadOnlyList<Doorway> Doorways => DoorwayList;
        uint ReleaseSequence;

        // Variables
        readonly MSTSWagon Wagon;
        protected Simulator Simulator => Wagon.Simulator;
        public readonly DoorSide Side;
        protected Timer OpeningTimer;
        protected Timer ClosingTimer;
        
        DoorState LegacyState = DoorState.Closed;
        public DoorState State
        {
            get
            {
                if (!ReleaseMode) return LegacyState;
                var state = DoorState.Closed;
                foreach (var door in DoorwayList)
                    if (door.State > state) state = door.State;
                return state;
            }
            protected set => LegacyState = value;
        }
        public bool Locked {get; protected set; }

        public Door(MSTSWagon wagon, DoorSide side)
        {
            Wagon = wagon;
            Side = side;

            OpeningTimer = new Timer(wagon);
            ClosingTimer = new Timer(wagon);
        }

        public virtual void Parse(string lowercasetoken, STFReader stf)
        {
        }

        public void Copy(Door other)
        {
            ClosingDelayS = other.ClosingDelayS;
            OpeningDelayS = other.OpeningDelayS;
            ReleaseMode = other.ReleaseMode;
            RandomOpenDelayMinS = other.RandomOpenDelayMinS;
            RandomOpenDelayMaxS = other.RandomOpenDelayMaxS;
            OpenDurationS = other.OpenDurationS;
            ConfigureGroups(other.Doorways.Select(door => door.Group));
        }

        public void ConfigureGroups(IEnumerable<string> groups)
        {
            var configured = groups.Distinct().Select(group => new Doorway(group)).ToArray();
            DoorwayList.Clear();
            DoorwayList.AddRange(configured);
        }

        public DoorState StateForGroup(string group)
        {
            if (!ReleaseMode) return State;
            foreach (var door in DoorwayList)
                if (door.Group == group || door.Group == "") return door.State;
            return DoorState.Closed;
        }

        static float ValidDuration(float value, float minimum = 0f) =>
            float.IsNaN(value) || float.IsInfinity(value) ? minimum : Math.Max(minimum, value);

        public virtual void Initialize()
        {
            OpeningDelayS = ValidDuration(OpeningDelayS);
            ClosingDelayS = ValidDuration(ClosingDelayS);
            RandomOpenDelayMinS = ValidDuration(RandomOpenDelayMinS, 0.1f);
            RandomOpenDelayMaxS = Math.Max(RandomOpenDelayMinS, ValidDuration(RandomOpenDelayMaxS));
            OpenDurationS = ValidDuration(OpenDurationS, 0.1f);
            ClosingTimer.Setup(ClosingDelayS);
            OpeningTimer.Setup(OpeningDelayS);
        }

        /// <summary>
        /// Initialization when simulation starts with moving train
        /// </summary>
        public virtual void InitializeMoving()
        {
        }

        public virtual void Save(BinaryWriter outf)
        {
            if (ReleaseMode)
            {
                // Negative markers leave the original manual-door layout unchanged.
                outf.Write(-3);
                outf.Write(Locked);
                outf.Write(Released);
                outf.Write(ReleaseSequence);
                outf.Write(DoorwayList.Count);
                foreach (var door in DoorwayList)
                {
                    outf.Write(door.Group);
                    outf.Write((int)door.State);
                    outf.Write(door.RemainingS);
                    outf.Write(door.RandomState);
                    outf.Write(door.OpenedThisRelease);
                    outf.Write(door.SelectedThisRelease);
                }
                return;
            }
            outf.Write((int)State);
            outf.Write(Locked);
        }

        public virtual void Restore(BinaryReader inf)
        {
            int state = inf.ReadInt32();
            Locked = inf.ReadBoolean();
            if (state >= -3 && state <= -1)
            {
                ReleaseMode = true;
                Released = inf.ReadBoolean();
                ReleaseSequence = inf.ReadUInt32();
                int count = inf.ReadInt32();
                if (count < 0 || count > 3) throw new InvalidDataException("Invalid doorway count.");
                DoorwayList.Clear();
                for (int i = 0; i < count; i++)
                {
                    var door = new Doorway(inf.ReadString())
                    {
                        State = (DoorState)inf.ReadInt32(),
                        RemainingS = inf.ReadSingle(),
                        RandomState = inf.ReadUInt32(),
                    };
                    door.OpenedThisRelease = state <= -2 ? inf.ReadBoolean() : door.State != DoorState.Closed;
                    door.SelectedThisRelease = state == -3 ? inf.ReadBoolean() : true;
                    if (door.State < DoorState.Closed || door.State > DoorState.Open
                        || float.IsNaN(door.RemainingS) || float.IsInfinity(door.RemainingS) || door.RemainingS < 0)
                        throw new InvalidDataException("Invalid saved doorway state.");
                    DoorwayList.Add(door);
                }
            }
            else
            {
                if (state < (int)DoorState.Closed || state > (int)DoorState.Open)
                    throw new InvalidDataException("Invalid saved door state.");
                State = (DoorState)state;
                Released = false;
                foreach (var door in DoorwayList)
                {
                    door.State = (DoorState)state;
                    door.OpenedThisRelease = door.State != DoorState.Closed;
                    door.SelectedThisRelease = false;
                    door.RemainingS = door.State == DoorState.Opening ? OpeningDelayS
                        : door.State == DoorState.Closing ? ClosingDelayS : OpenDurationS;
                }
            }
        }

        public virtual void Update(float elapsedClockSeconds)
        {
            if (ReleaseMode)
            {
                foreach (var door in DoorwayList) UpdateDoorway(door, Math.Max(0, elapsedClockSeconds));
                return;
            }
            switch(State)
            {
                case DoorState.Opening:
                    if (!OpeningTimer.Started) OpeningTimer.Start();
                    if (OpeningTimer.Triggered)
                    {
                        State = DoorState.Open;
                        OpeningTimer.Stop();
                    }
                    break;
                case DoorState.Closing:
                    if (!ClosingTimer.Started) ClosingTimer.Start();
                    if (ClosingTimer.Triggered)
                    {
                        State = DoorState.Closed;
                        ClosingTimer.Stop();
                    }
                    break;
            }
        }
        public void SetDoorLock(bool lck)
        {
            Locked = lck;
            if (lck) SetDoor(false);
        }
        public void SetDoor(bool open)
        {
            if (ReleaseMode)
            {
                if (open) ReleaseDoors(new[] { this });
                else
                {
                    Released = false;
                    foreach (var door in DoorwayList)
                    {
                        door.SelectedThisRelease = false;
                        if (door.State == DoorState.Open || door.State == DoorState.Opening) StartClosing(door);
                    }
                    ConfirmCommand(false);
                }
                return;
            }
            switch (State)
            {
                case DoorState.Closed:
                case DoorState.Closing:
                    if (!Locked && open)
                    {
                        ClosingTimer.Stop();
                        State = DoorState.Opening;
                        Wagon.SignalEvent(Event.DoorOpen);
                        bool driverRightSide = (Side == DoorSide.Right) ^ Wagon.GetCabFlipped();
                        Confirm(driverRightSide ? CabControl.DoorsRight : CabControl.DoorsLeft, CabSetting.On);
                    }
                    break;
                case DoorState.Open:
                case DoorState.Opening:
                    if (!open)
                    {
                        OpeningTimer.Stop();
                        State = DoorState.Closing;
                        Wagon.SignalEvent(Event.DoorClose);
                        bool driverRightSide = (Side == DoorSide.Right) ^ Wagon.GetCabFlipped();
                        Confirm(driverRightSide ? CabControl.DoorsRight : CabControl.DoorsLeft, CabSetting.Off);
                    }
                    break;
            }
        }

        // Called once for each requested train side, so the guarantee is across the
        // whole side of the consist rather than forcing an opening on every car.
        internal static void ReleaseDoors(IEnumerable<Door> doors)
        {
            var releasing = doors.Where(door => door.ReleaseMode && !door.Locked && !door.Released
                && door.DoorwayList.Count > 0).ToArray();
            var candidates = new List<(Door Owner, Doorway Doorway)>();
            uint selectionSeed = 2166136261;
            foreach (var owner in releasing)
            {
                owner.Released = true;
                owner.ReleaseSequence++;
                foreach (var door in owner.DoorwayList)
                {
                    uint seed = 2166136261;
                    // CarID is preserved by multiplayer even when numeric UiD differs.
                    string identity = owner.Wagon.CarID + ":" + (int)owner.Side + ":" + door.Group;
                    if (string.IsNullOrEmpty(owner.Wagon.CarID) || owner.Wagon.CarID == "AI")
                        identity += ":" + owner.Wagon.UiD + ":" + owner.Wagon.Train?.Cars.IndexOf(owner.Wagon);
                    foreach (char c in identity) seed = unchecked((seed ^ c) * 16777619);
                    door.RandomState = seed ^ unchecked(owner.ReleaseSequence * 2654435761u);
                    if (door.RandomState == 0) door.RandomState = 1;
                    selectionSeed = unchecked((selectionSeed ^ door.RandomState) * 16777619);
                    door.SelectedThisRelease = false;
                    door.OpenedThisRelease = door.State == DoorState.Opening || door.State == DoorState.Open;
                    candidates.Add((owner, door));
                }
            }
            if (candidates.Count == 0) return; // Locked, already released, or no configured doorways.
            if (selectionSeed == 0) selectionSeed = 1;
            int count = 1 + (int)(NextRandom(ref selectionSeed) % (uint)candidates.Count);
            for (int i = 0; i < count; i++)
            {
                int index = i + (int)(NextRandom(ref selectionSeed) % (uint)(candidates.Count - i));
                var selected = candidates[index];
                candidates[index] = candidates[i];
                candidates[i] = selected;
                selected.Doorway.SelectedThisRelease = true;
                if (selected.Doorway.State == DoorState.Closed)
                    selected.Doorway.RemainingS = selected.Owner.NextOpenDelay(selected.Doorway);
            }
            foreach (var owner in releasing) owner.ConfirmCommand(true);
        }

        static uint NextRandom(ref uint value)
        {
            value ^= value << 13;
            value ^= value >> 17;
            value ^= value << 5;
            return value;
        }

        float NextOpenDelay(Doorway door) => RandomOpenDelayMinS
            + (RandomOpenDelayMaxS - RandomOpenDelayMinS) * (NextRandom(ref door.RandomState) / 4294967296f);

        bool OpeningAllowed => !Locked && !(Wagon.Train?.LeadLocomotive is MSTSLocomotive lead
            && lead.DoorsOpeningBlockedWhenMoving && Math.Abs(Wagon.Train.SpeedMpS) > Orts.Simulation.Simulator.MaxStoppedMpS);

        void StartClosing(Doorway door)
        {
            door.State = DoorState.Closing;
            door.RemainingS = ClosingDelayS;
            Wagon.SignalEvent(Event.DoorClose);
        }

        void ConfirmCommand(bool open)
        {
            bool right = (Side == DoorSide.Right) ^ Wagon.GetCabFlipped();
            Confirm(right ? CabControl.DoorsRight : CabControl.DoorsLeft, open ? CabSetting.On : CabSetting.Off);
        }

        void UpdateDoorway(Doorway door, float elapsedSeconds)
        {
            if (door.State == DoorState.Closed && (!Released || !door.SelectedThisRelease || door.OpenedThisRelease)) return;
            door.RemainingS -= elapsedSeconds;
            // Carry overshoot across transitions to keep timing independent of frame rate.
            while (door.RemainingS <= 0)
            {
                float overshoot = -door.RemainingS;
                switch (door.State)
                {
                    case DoorState.Closed:
                        if (!Released || !door.SelectedThisRelease || door.OpenedThisRelease || !OpeningAllowed)
                        {
                            door.RemainingS = 0;
                            return;
                        }
                        door.State = DoorState.Opening;
                        door.OpenedThisRelease = true;
                        door.RemainingS = OpeningDelayS;
                        Wagon.SignalEvent(Event.DoorOpen);
                        break;
                    case DoorState.Opening:
                        door.State = DoorState.Open;
                        door.RemainingS = OpenDurationS;
                        break;
                    case DoorState.Open:
                        StartClosing(door);
                        break;
                    case DoorState.Closing:
                        door.State = DoorState.Closed;
                        if (!Released || !door.SelectedThisRelease || door.OpenedThisRelease)
                        {
                            door.RemainingS = 0;
                            return;
                        }
                        // A fresh release may have arrived while an earlier cycle was closing.
                        door.RemainingS = NextOpenDelay(door);
                        break;
                }
                door.RemainingS -= overshoot;
            }
        }
        protected void Confirm(CabControl control, CabSetting setting)
        {
            if (Wagon == Simulator.PlayerLocomotive)
            {
                Simulator.Confirmer?.Confirm(control, setting);
            }
        }
    }

    public sealed class Doorway
    {
        public string Group { get; }
        public DoorState State { get; internal set; } = DoorState.Closed;
        public bool SelectedThisRelease { get; internal set; }
        internal float RemainingS;
        internal uint RandomState;
        internal bool OpenedThisRelease;

        public Doorway(string group) { Group = group; }
    }
}
