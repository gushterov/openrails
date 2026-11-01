// COPYRIGHT 2026 by the Open Rails project. GNU GPL version 3 or later.
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using Orts.Common;
using Orts.Parsers.Msts;
using Orts.Simulation;
using Orts.Simulation.Physics;
using Orts.Simulation.RollingStocks;
using Orts.Simulation.RollingStocks.SubSystems;
using ORTS.Common;
using Xunit;

namespace Tests.Orts.Simulation
{
    public class DoorCommandTests : IDisposable
    {
        readonly MSTSWagon PreviousLeftReceiver = ToggleDoorsLeftCommand.Receiver;
        readonly MSTSWagon PreviousRightReceiver = ToggleDoorsRightCommand.Receiver;
        readonly MSTSWagon PreviousCloseReceiver = CloseDoorsCommand.Receiver;

        static T Uninitialized<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));

        class TestLocomotive : MSTSLocomotive
        {
            public TestLocomotive() : base(null, null) { }
            public override void SignalEvent(Event evt) { }
        }

        static MSTSLocomotive CreateLocomotive()
        {
            var simulator = Uninitialized<Simulator>();
            var locomotive = Uninitialized<TestLocomotive>();
            typeof(TrainCar).GetField(nameof(TrainCar.Simulator)).SetValue(locomotive, simulator);
            locomotive.Doors = new Doors(locomotive);
            locomotive.Train = Uninitialized<Train>();
            typeof(Train).GetProperty(nameof(Train.Simulator)).SetValue(locomotive.Train, simulator);
            locomotive.Train.Cars = new List<TrainCar> { locomotive };
            ToggleDoorsLeftCommand.Receiver = locomotive;
            ToggleDoorsRightCommand.Receiver = locomotive;
            CloseDoorsCommand.Receiver = locomotive;
            return locomotive;
        }

        static void ParseOption(MSTSWagon wagon, string value)
        {
            using var stream = new MemoryStream(Encoding.ASCII.GetBytes("( " + value + " )"));
            using var reader = new STFReader(stream, "test.eng", Encoding.ASCII, false);
            wagon.Parse("wagon(ortsdoors(separateclosecommand", reader);
        }

        static void Toggle(bool right)
        {
            // Redo executes the same path during initial input and command replay.
            if (right) Uninitialized<ToggleDoorsRightCommand>().Redo();
            else Uninitialized<ToggleDoorsLeftCommand>().Redo();
        }

        [Theory]
        [InlineData("1", true)]
        [InlineData("0", false)]
        [InlineData("true", true)]
        [InlineData("false", false)]
        public void OptionParsesAndCopies(string value, bool expected)
        {
            var locomotive = CreateLocomotive();
            Assert.False(locomotive.Doors.SeparateCloseCommand);
            ParseOption(locomotive, value);
            Assert.Equal(expected, locomotive.Doors.SeparateCloseCommand);
            var copy = new Doors(locomotive);
            copy.Copy(locomotive.Doors);
            Assert.Equal(expected, copy.SeparateCloseCommand);
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public void SeparateCloseKeepsSelectedSideOpenAndCloseCommandClosesBoth(bool right, bool rearCab)
        {
            var locomotive = CreateLocomotive();
            locomotive.UsingRearCab = rearCab;
            ParseOption(locomotive, "1");
            var selected = right ^ rearCab ? locomotive.Doors.RightDoor : locomotive.Doors.LeftDoor;
            var other = right ^ rearCab ? locomotive.Doors.LeftDoor : locomotive.Doors.RightDoor;

            Toggle(right);
            Assert.Equal(DoorState.Opening, selected.State);
            Assert.Equal(DoorState.Closed, other.State);
            Toggle(right);
            Assert.Equal(DoorState.Opening, selected.State);
            typeof(Door).GetProperty(nameof(Door.State)).SetValue(selected, DoorState.Open);
            Toggle(right);
            Assert.Equal(DoorState.Open, selected.State);

            Toggle(!right);
            Assert.Equal(DoorState.Opening, other.State);
            Uninitialized<CloseDoorsCommand>().Redo();
            Assert.Equal(DoorState.Closing, selected.State);
            Assert.Equal(DoorState.Closing, other.State);
            Toggle(right);
            Assert.Equal(DoorState.Opening, selected.State);
            Assert.Equal(DoorState.Closing, other.State);
            locomotive.Train.SetDoors(DoorSide.Both, false);
            Assert.Equal(DoorState.Closing, selected.State);
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public void DefaultAndExplicitFalseRetainToggleBehavior(bool right, bool explicitFalse)
        {
            var locomotive = CreateLocomotive();
            if (explicitFalse) ParseOption(locomotive, "0");
            var selected = right ? locomotive.Doors.RightDoor : locomotive.Doors.LeftDoor;
            Toggle(right);
            Assert.Equal(DoorState.Opening, selected.State);
            Toggle(right);
            Assert.Equal(DoorState.Closing, selected.State);
        }

        public void Dispose()
        {
            ToggleDoorsLeftCommand.Receiver = PreviousLeftReceiver;
            ToggleDoorsRightCommand.Receiver = PreviousRightReceiver;
            CloseDoorsCommand.Receiver = PreviousCloseReceiver;
        }
    }
}
