// COPYRIGHT 2026 by the Open Rails project. GNU GPL version 3 or later.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
            public List<Event> SoundEvents;
            public TestLocomotive() : base(null, null) { }
            public override void SignalEvent(Event evt) => SoundEvents.Add(evt);
        }

        static TestLocomotive CreateLocomotive()
        {
            var simulator = Uninitialized<Simulator>();
            var locomotive = Uninitialized<TestLocomotive>();
            locomotive.SoundEvents = new List<Event>();
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

        [Theory]
        [InlineData(105, Event.DoorOpen)]
        [InlineData(106, Event.DoorClose)]
        [InlineData(264, Event.WiperCycleStart)]
        [InlineData(265, Event.DoorLeftButton)]
        [InlineData(266, Event.DoorRightButton)]
        [InlineData(267, Event.DoorsCloseButton)]
        public void DoorButtonSoundTriggersAreSeparateFromDoorMovement(int trigger, Event expected)
        {
            Assert.Equal(expected, Events.From(Events.Source.MSTSCar, trigger));
            Assert.Equal(Event.None, Events.From(Events.Source.MSTSCrossing, trigger));
            Assert.Equal(Event.None, Events.From(Events.Source.MSTSInGame, trigger));
        }

        [Theory]
        [InlineData(false, false, false)]
        [InlineData(false, false, true)]
        [InlineData(false, true, false)]
        [InlineData(false, true, true)]
        [InlineData(true, false, false)]
        [InlineData(true, false, true)]
        [InlineData(true, true, false)]
        [InlineData(true, true, true)]
        public void RepeatedReleaseButtonsSoundImmediatelyAndKeepCabRelativeSide(bool right, bool rearCab, bool flipped)
        {
            var locomotive = CreateLocomotive();
            locomotive.UsingRearCab = rearCab;
            locomotive.Flipped = flipped;
            ParseOption(locomotive, "1");
            locomotive.Doors.LeftDoor.ReleaseMode = locomotive.Doors.RightDoor.ReleaseMode = true;
            locomotive.Doors.Initialize();
            var button = right ? Event.DoorRightButton : Event.DoorLeftButton;
            var selected = right ^ rearCab ? locomotive.Doors.RightDoor : locomotive.Doors.LeftDoor;

            Toggle(right);
            Toggle(right);
            Assert.True(selected.Released);
            Assert.Equal(DoorState.Closed, selected.State); // Still waiting for the random opening delay.
            Assert.Equal(new[] { button, button }, locomotive.SoundEvents);
            locomotive.Doors.Update(100);
            Assert.Equal(2, locomotive.SoundEvents.Count(evt => evt == button));
            Assert.Equal(1, locomotive.SoundEvents.Count(evt => evt == Event.DoorOpen));
            Assert.Equal(1, locomotive.SoundEvents.Count(evt => evt == Event.DoorClose));
            Uninitialized<CloseDoorsCommand>().Redo();
            Uninitialized<CloseDoorsCommand>().Redo();
            Assert.Equal(2, locomotive.SoundEvents.Count(evt => evt == Event.DoorsCloseButton));
            Assert.Equal(1, locomotive.SoundEvents.Count(evt => evt == Event.DoorClose));
        }

        [Fact]
        public void RejectedOpeningStillSoundsTheButton()
        {
            var locomotive = CreateLocomotive();
            typeof(MSTSLocomotive).GetProperty(nameof(MSTSLocomotive.DoorsOpeningBlockedWhenMoving)).SetValue(locomotive, true);
            locomotive.Train.SpeedMpS = 1;
            Toggle(true);
            Assert.Equal(new[] { Event.DoorRightButton }, locomotive.SoundEvents);
            Assert.Equal(DoorState.Closed, locomotive.Doors.RightDoor.State);
        }

        [Fact]
        public void ButtonEventsStayOnTheCommandReceiverAndAutomaticDoorsDoNotGenerateThem()
        {
            var locomotive = CreateLocomotive();
            var trailing = Uninitialized<TestLocomotive>();
            trailing.SoundEvents = new List<Event>();
            typeof(TrainCar).GetField(nameof(TrainCar.Simulator)).SetValue(trailing, locomotive.Simulator);
            trailing.Train = locomotive.Train;
            trailing.Doors = new Doors(trailing);
            locomotive.Train.Cars.Add(trailing);
            Toggle(false);
            Assert.Equal(new[] { Event.DoorLeftButton, Event.DoorOpen }, locomotive.SoundEvents);
            Assert.Equal(new[] { Event.DoorOpen }, trailing.SoundEvents);

            locomotive.SoundEvents.Clear();
            trailing.SoundEvents.Clear();
            locomotive.Train.SetDoors(DoorSide.Left, false);
            locomotive.Train.SetDoors(DoorSide.Right, true);
            foreach (var car in new[] { locomotive, trailing })
                Assert.Equal(new[] { Event.DoorClose, Event.DoorOpen }, car.SoundEvents);
        }

        public void Dispose()
        {
            ToggleDoorsLeftCommand.Receiver = PreviousLeftReceiver;
            ToggleDoorsRightCommand.Receiver = PreviousRightReceiver;
            CloseDoorsCommand.Receiver = PreviousCloseReceiver;
        }
    }
}
