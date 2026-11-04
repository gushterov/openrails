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
using Xunit;

namespace Tests.Orts.Simulation
{
    public class RandomDoorTests
    {
        sealed class QuietLocomotive : MSTSLocomotive
        {
            public QuietLocomotive() : base(null, null) { }
            public int OpenEvents;
            public override void SignalEvent(Event evt) { if (evt == Event.DoorOpen) OpenEvents++; }
        }

        static T Uninitialized<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));

        static QuietLocomotive[] CreateTrain(int count = 1)
        {
            var simulator = Uninitialized<Simulator>();
            var train = Uninitialized<Train>();
            typeof(Train).GetProperty(nameof(Train.Simulator)).SetValue(train, simulator);
            train.Cars = new List<TrainCar>();
            train.LeadLocomotiveIndex = 0;
            var cars = new QuietLocomotive[count];
            for (int i = 0; i < count; i++)
            {
                var car = cars[i] = Uninitialized<QuietLocomotive>();
                typeof(TrainCar).GetField(nameof(TrainCar.Simulator)).SetValue(car, simulator);
                car.UiD = i;
                car.CarID = "0 - " + i;
                car.Train = train;
                train.Cars.Add(car);
                car.Doors = new Doors(car);
                foreach (var door in new[] { car.Doors.LeftDoor, car.Doors.RightDoor })
                {
                    door.ReleaseMode = true;
                    door.OpeningDelayS = 1;
                    door.ClosingDelayS = 2;
                    door.RandomOpenDelayMinS = door.RandomOpenDelayMaxS = 2;
                    door.OpenDurationS = 3;
                }
                car.Doors.LeftDoor.ConfigureGroups(new[] { "D", "E" });
                car.Doors.RightDoor.ConfigureGroups(new[] { "A", "B" });
                car.Doors.Initialize();
            }
            return cars;
        }

        static QuietLocomotive SingleDoorCar()
        {
            var car = CreateTrain()[0];
            car.Doors.RightDoor.ConfigureGroups(new[] { "A" });
            return car;
        }

        static Doorway[] SelectedRight(IEnumerable<QuietLocomotive> cars) => cars
            .SelectMany(car => car.Doors.RightDoor.Doorways).Where(door => door.SelectedThisRelease).ToArray();

        static void Update(IEnumerable<QuietLocomotive> cars, float seconds)
        {
            foreach (var car in cars) car.Doors.Update(seconds);
        }

        [Fact]
        public void FourCarsChooseOneToEightDoorwaysAcrossTheWholeReleasedSide()
        {
            var cars = CreateTrain(4);
            var train = cars[0].Train;
            var counts = new HashSet<int>();
            for (int release = 0; release < 100; release++)
            {
                train.SetDoors(DoorSide.Right, true);
                int selected = SelectedRight(cars).Length;
                Assert.InRange(selected, 1, 8);
                counts.Add(selected);
                Assert.All(cars, car => Assert.False(car.Doors.LeftDoor.Released));
                Update(cars, 100);
                Assert.Equal(selected, cars.Sum(car => car.OpenEvents));
                Assert.False(train.DoorsClosedAndSecured);
                train.SetDoors(DoorSide.Both, false);
                Assert.True(train.DoorsClosedAndSecured);
                foreach (var car in cars) car.OpenEvents = 0;
            }
            Assert.Contains(1, counts); // Some coaches must be allowed to have no openings.
            Assert.Contains(8, counts); // Opening every doorway is also a valid random choice.
            Assert.True(counts.Count > 4);
        }

        [Fact]
        public void SelectedDoorwaysUseIndependentRandomOpeningDelays()
        {
            var cars = CreateTrain(4);
            foreach (var car in cars)
            {
                car.Doors.RightDoor.RandomOpenDelayMaxS = 15;
                car.Doors.Initialize();
            }
            var train = cars[0].Train;
            for (int attempt = 0; attempt < 100; attempt++)
            {
                train.SetDoors(DoorSide.Right, true);
                if (SelectedRight(cars).Length >= 4) break;
                train.SetDoors(DoorSide.Right, false);
            }
            var selected = SelectedRight(cars);
            Assert.True(selected.Length >= 4);
            var firstOpen = new Dictionary<Doorway, float>();
            for (int tick = 1; tick <= 400; tick++)
            {
                Update(cars, 0.05f);
                foreach (var door in selected)
                    if (door.State == DoorState.Opening && !firstOpen.ContainsKey(door))
                        firstOpen.Add(door, tick * 0.05f);
            }
            Assert.Equal(selected.Length, firstOpen.Count);
            Assert.True(firstOpen.Values.Distinct().Count() > 1);
            Assert.All(firstOpen.Values, time => Assert.InRange(time, 2f, 15.1f));
            Assert.Equal(selected.Length, cars.Sum(car => car.OpenEvents));
            Assert.All(cars.SelectMany(car => car.Doors.RightDoor.Doorways)
                .Where(door => !door.SelectedThisRelease), door => Assert.Equal(DoorState.Closed, door.State));
        }

        [Fact]
        public void DoorOpensOnceHoldsForDurationThenStaysClosedUntilAnotherRelease()
        {
            var car = SingleDoorCar();
            var door = car.Doors.RightDoor;
            car.Train.SetDoors(DoorSide.Right, true);
            Assert.True(door.Released);
            Assert.Equal(DoorState.Closed, door.State);
            door.Update(2);
            Assert.Equal(DoorState.Opening, door.State);
            door.Update(1);
            Assert.Equal(DoorState.Open, door.State);
            door.Update(2.9f);
            Assert.Equal(DoorState.Open, door.State);
            door.Update(0.2f);
            Assert.Equal(DoorState.Closing, door.State);
            door.Update(2);
            Assert.Equal(DoorState.Closed, door.State);
            var completed = Save(car.Doors);
            for (int i = 0; i < 10; i++)
            {
                car.Train.SetDoors(DoorSide.Right, true);
                door.Update(100);
            }
            Assert.Equal(completed, Save(car.Doors));
            Assert.Equal(1, car.OpenEvents);
            Assert.True(car.Train.DoorCommandOpen(DoorSide.Right));
            car.Train.SetDoors(DoorSide.Right, false);
            car.Train.SetDoors(DoorSide.Right, true);
            door.Update(100);
            Assert.Equal(2, car.OpenEvents);
            Assert.Equal(DoorState.Closed, door.State);
        }

        [Theory]
        [InlineData(0f)]
        [InlineData(2.5f)]
        [InlineData(4f)]
        [InlineData(6.5f)]
        [InlineData(8.5f)]
        public void ForceCloseCancelsPendingOpeningsAndNeverReopens(float elapsed)
        {
            var car = SingleDoorCar();
            car.Train.SetDoors(DoorSide.Both, true);
            car.Doors.Update(elapsed);
            car.Train.SetDoors(DoorSide.Both, false);
            int openings = car.OpenEvents;
            car.Doors.Update(100);
            Assert.False(car.Train.DoorsReleased(DoorSide.Both));
            Assert.True(car.Train.DoorsClosedAndSecured);
            Assert.Equal(openings, car.OpenEvents);
        }

        [Fact]
        public void FreshReleaseDuringForcedClosingAllowsOneNewOpening()
        {
            var car = SingleDoorCar();
            var door = car.Doors.RightDoor;
            door.SetDoor(true);
            door.Update(4);
            door.SetDoor(false);
            Assert.Equal(DoorState.Closing, door.State);
            door.SetDoor(true);
            door.Update(100);
            Assert.Equal(2, car.OpenEvents);
            Assert.Equal(DoorState.Closed, door.State);
            door.Update(100);
            Assert.Equal(2, car.OpenEvents);
        }

        [Fact]
        public void LockedCarsAreExcludedAndRemainingSideStillGetsAnOpening()
        {
            var cars = CreateTrain(4);
            foreach (var car in cars.Take(3)) car.Doors.RightDoor.SetDoorLock(true);
            cars[0].Train.SetDoors(DoorSide.Right, true);
            Assert.InRange(SelectedRight(cars).Length, 1, 2);
            Update(cars, 100);
            Assert.All(cars.Take(3), car =>
            {
                Assert.False(car.Doors.RightDoor.Released);
                Assert.Equal(0, car.OpenEvents);
            });
            Assert.InRange(cars[3].OpenEvents, 1, 2);
            cars[3].Doors.RightDoor.SetDoorLock(true);
            cars[0].Train.SetDoors(DoorSide.Right, true);
            Assert.False(cars[0].Train.DoorsReleased(DoorSide.Right));
        }

        [Fact]
        public void DelayedOpeningRechecksMovementRestriction()
        {
            var car = SingleDoorCar();
            typeof(MSTSLocomotive).GetProperty(nameof(MSTSLocomotive.DoorsOpeningBlockedWhenMoving)).SetValue(car, true);
            car.Train.SpeedMpS = 1;
            car.Train.SetDoors(DoorSide.Right, true);
            Assert.False(car.Doors.RightDoor.Released);
            car.Train.SpeedMpS = 0;
            car.Train.SetDoors(DoorSide.Right, true);
            car.Train.SpeedMpS = 1;
            car.Doors.Update(10);
            Assert.Equal(0, car.OpenEvents);
            car.Train.SpeedMpS = 0;
            car.Doors.Update(0.1f);
            Assert.Equal(1, car.OpenEvents);
            car.Train.SpeedMpS = 1;
            car.Train.SetDoors(DoorSide.Right, false);
            car.Doors.Update(100);
            Assert.True(car.Train.DoorsClosedAndSecured);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void FlippedCarsRespectTrainSideAndBothSidesEachSelectAtLeastOne(bool flipped)
        {
            var car = CreateTrain()[0];
            car.Flipped = flipped;
            car.Train.SetDoors(DoorSide.Right, true);
            Assert.Equal(!flipped, car.Doors.RightDoor.Released);
            Assert.Equal(flipped, car.Doors.LeftDoor.Released);
            car.Train.SetDoors(DoorSide.Both, false);
            car.Train.SetDoors(DoorSide.Both, true);
            Assert.Contains(car.Doors.LeftDoor.Doorways, door => door.SelectedThisRelease);
            Assert.Contains(car.Doors.RightDoor.Doorways, door => door.SelectedThisRelease);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void CabToggleHonorsReleaseAndSeparateCloseOption(bool separateClose)
        {
            var car = SingleDoorCar();
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(separateClose ? "( 1 )" : "( 0 )")))
            using (var stf = new STFReader(stream, "test.eng", Encoding.UTF8, false))
                car.Parse("wagon(ortsdoors(separateclosecommand", stf);
            var previous = ToggleDoorsRightCommand.Receiver;
            try
            {
                ToggleDoorsRightCommand.Receiver = car;
                Uninitialized<ToggleDoorsRightCommand>().Redo();
                Assert.True(car.Doors.RightDoor.Released);
                Uninitialized<ToggleDoorsRightCommand>().Redo();
                Assert.Equal(separateClose, car.Doors.RightDoor.Released);
            }
            finally { ToggleDoorsRightCommand.Receiver = previous; }
        }

        [Theory]
        [InlineData("DOOR_A1", "A")]
        [InlineData("DOOR_A2", "A")]
        [InlineData("DOOR_B1", "B")]
        [InlineData("DOOR_D2", "D")]
        [InlineData("DOOR_E1", "E")]
        [InlineData("PANTOGRAPH1", null)]
        public void DoubleDoorLeavesShareTheirDoorway(string matrix, string expected) =>
            Assert.Equal(expected, Doors.GetDoorGroup(matrix));

        static byte[] Save(Doors doors)
        {
            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true)) doors.Save(writer);
            return stream.ToArray();
        }

        [Theory]
        [InlineData(0f)]
        [InlineData(3f)]
        [InlineData(14f)]
        [InlineData(30f)]
        public void SaveResumePreservesSubsetTimersAndOnceOnlyLimit(float elapsed)
        {
            var original = CreateTrain(4);
            foreach (var car in original)
            {
                car.Doors.RightDoor.RandomOpenDelayMaxS = 15;
                car.Doors.Initialize();
            }
            original[0].Train.SetDoors(DoorSide.Right, true);
            Update(original, elapsed);
            var restored = CreateTrain(4);
            for (int i = 0; i < 4; i++)
            {
                restored[i].Doors.Copy(original[i].Doors);
                restored[i].Doors.Initialize();
                using var reader = new BinaryReader(new MemoryStream(Save(original[i].Doors)));
                restored[i].Doors.Restore(reader);
                Assert.Equal(Save(original[i].Doors), Save(restored[i].Doors));
            }
            for (int tick = 0; tick < 100; tick++)
            {
                Update(original, 0.37f);
                Update(restored, 0.37f);
                for (int i = 0; i < 4; i++) Assert.Equal(Save(original[i].Doors), Save(restored[i].Doors));
            }
            if (elapsed == 30) Assert.All(restored, car => Assert.Equal(0, car.OpenEvents));
            foreach (var cars in new[] { original, restored })
            {
                cars[0].Train.SetDoors(DoorSide.Right, false);
                cars[0].Train.SetDoors(DoorSide.Right, true);
            }
            for (int i = 0; i < 4; i++) Assert.Equal(Save(original[i].Doors), Save(restored[i].Doors));
        }

        [Fact]
        public void SelectionAndTimingAreRepeatableAcrossDifferentUpdateSteps()
        {
            var coarse = CreateTrain(4);
            var fine = CreateTrain(4);
            foreach (var cars in new[] { coarse, fine })
            {
                foreach (var car in cars)
                {
                    car.Doors.RightDoor.RandomOpenDelayMaxS = 15;
                    car.Doors.Initialize();
                }
                cars[0].Train.SetDoors(DoorSide.Right, true);
            }
            Update(coarse, 12);
            for (int i = 0; i < 48; i++) Update(fine, 0.25f);
            for (int tick = 0; tick < 60; tick++)
            {
                for (int i = 0; i < 4; i++)
                    Assert.Equal(coarse[i].Doors.RightDoor.Doorways.Select(door => (door.SelectedThisRelease, door.State)),
                        fine[i].Doors.RightDoor.Doorways.Select(door => (door.SelectedThisRelease, door.State)));
                Update(coarse, 0.25f);
                Update(fine, 0.25f);
            }
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(-2)]
        public void EarlierReleaseSaveFormatsRemainReadable(int marker)
        {
            var car = SingleDoorCar();
            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
            {
                writer.Write(marker);
                writer.Write(false);
                writer.Write(true);
                writer.Write(1u);
                writer.Write(1);
                writer.Write("A");
                writer.Write((int)DoorState.Open);
                writer.Write(2f);
                writer.Write(123u);
                if (marker == -2) writer.Write(true);
                writer.Write(987654);
            }
            stream.Position = 0;
            using var reader = new BinaryReader(stream);
            car.Doors.RightDoor.Restore(reader);
            Assert.Equal(987654, reader.ReadInt32());
            car.Doors.Update(100);
            Assert.Equal(DoorState.Closed, car.Doors.RightDoor.State);
            Assert.Equal(0, car.OpenEvents);
        }

        [Fact]
        public void LegacyDoorSaveLayoutAndManualOpeningRemainUnchanged()
        {
            var car = SingleDoorCar();
            car.Doors.LeftDoor.ReleaseMode = car.Doors.RightDoor.ReleaseMode = false;
            Assert.Equal(10, Save(car.Doors).Length);
            car.Train.SetDoors(DoorSide.Right, true);
            car.Doors.Update(0);
            car.Simulator.GameTime = 1;
            car.Doors.Update(1);
            Assert.Equal(DoorState.Open, car.Doors.RightDoor.State);
            car.Simulator.GameTime = 100;
            car.Doors.Update(99);
            Assert.Equal(DoorState.Open, car.Doors.RightDoor.State);
            var restored = SingleDoorCar();
            using var reader = new BinaryReader(new MemoryStream(Save(car.Doors)));
            restored.Doors.Restore(reader);
            Assert.Equal(DoorState.Open, restored.Doors.RightDoor.State);
            Assert.False(restored.Doors.RightDoor.Released);
        }

        [Fact]
        public void ConfigurationParsesThroughWagonAndCopiesWithoutRuntimeState()
        {
            var car = SingleDoorCar();
            foreach (var token in new[] { "ReleaseMode", "SeparateCloseCommand", "DoorGroups", "RandomOpenDelayMin", "RandomOpenDelayMax", "OpenDuration" })
            {
                string value = token == "ReleaseMode" || token == "SeparateCloseCommand" ? "1"
                    : token == "DoorGroups" ? "A B D E" : "4s";
                using var stream = new MemoryStream(Encoding.UTF8.GetBytes("( " + value + " )"));
                using var stf = new STFReader(stream, "doors.eng", Encoding.UTF8, false);
                car.Parse("wagon(ortsdoors(" + token.ToLowerInvariant(), stf);
            }
            car.Doors.RightDoor.SetDoor(true);
            var copy = new Doors(car);
            copy.Copy(car.Doors);
            copy.Initialize();
            Assert.True(copy.SeparateCloseCommand);
            Assert.True(copy.RightDoor.ReleaseMode);
            Assert.Equal(new[] { "A", "B" }, copy.RightDoor.Doorways.Select(door => door.Group));
            Assert.Equal(new[] { "D", "E" }, copy.LeftDoor.Doorways.Select(door => door.Group));
            Assert.Equal(4f, copy.RightDoor.OpenDurationS);
            Assert.Equal(4f, copy.RightDoor.RandomOpenDelayMinS);
            Assert.Equal(4f, copy.RightDoor.RandomOpenDelayMaxS);
            Assert.False(copy.RightDoor.Released);
        }

        [Fact]
        public void ZeroDurationsAreNormalizedAndDoNotLoop()
        {
            var car = SingleDoorCar();
            var door = car.Doors.RightDoor;
            door.OpeningDelayS = door.ClosingDelayS = door.OpenDurationS = 0;
            door.RandomOpenDelayMinS = door.RandomOpenDelayMaxS = 0;
            door.Initialize();
            door.SetDoor(true);
            door.Update(100);
            Assert.Equal(1, car.OpenEvents);
            Assert.Equal(DoorState.Closed, door.State);
        }
    }
}
