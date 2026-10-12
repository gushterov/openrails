// COPYRIGHT 2026 by the Open Rails project. GNU GPL version 3 or later.
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using Microsoft.Xna.Framework;
using Orts.Simulation;
using Orts.Simulation.Physics;
using Orts.Simulation.RollingStocks;
using Orts.Simulation.RollingStocks.SubSystems;
using Orts.Simulation.RollingStocks.SubSystems.Controllers;
using Xunit;

namespace Tests.Orts.Simulation
{
    public class CruiseControlTrainDataTests
    {
        static T Uninitialized<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));

        static (Simulator simulator, Train train, MSTSLocomotive locomotive, CruiseControl cruiseControl) CreateTrain(float length)
        {
            var simulator = Uninitialized<Simulator>();
            var train = Uninitialized<Train>();
            typeof(Train).GetProperty(nameof(Train.Simulator)).SetValue(train, simulator);
            train.TrainType = Train.TRAINTYPE.PLAYER;
            train.Length = length;
            train.LeadLocomotiveIndex = 0;
            var locomotive = Uninitialized<MSTSLocomotive>();
            typeof(TrainCar).GetField(nameof(TrainCar.Simulator)).SetValue(locomotive, simulator);
            locomotive.Train = train;
            locomotive.WheelAxles = new List<WheelAxle>
            {
                new WheelAxle(Vector3.Zero, 0, 0),
                new WheelAxle(Vector3.Zero, 0, 0),
                new WheelAxle(Vector3.Zero, 0, 0, true),
            };
            locomotive.TrainBrakeController = new ScriptedBrakeController(locomotive);
            typeof(ScriptedBrakeController).GetField("Script", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(locomotive.TrainBrakeController, new MSTSBrakeController());
            var trailer = Uninitialized<MSTSWagon>();
            trailer.Train = train;
            trailer.WheelAxles = new List<WheelAxle>
            {
                new WheelAxle(Vector3.Zero, 0, 0),
                new WheelAxle(Vector3.Zero, 0, 0),
                new WheelAxle(Vector3.Zero, 0, 0),
                new WheelAxle(Vector3.Zero, 0, 0),
            };
            train.Cars = new List<TrainCar> { locomotive, trailer };
            simulator.PlayerLocomotive = locomotive;
            var cruiseControl = new CruiseControl(locomotive)
            {
                DoComputeNumberOfAxles = true,
                SpeedRegulatorMaxForcePercentUnits = true,
            };
            locomotive.CruiseControl = cruiseControl;
            return (simulator, train, locomotive, cruiseControl);
        }

        [Theory]
        [InlineData(150f, 150)]
        [InlineData(150.1f, 151)]
        [InlineData(147.9f, 148)]
        public void AutomaticInitializationCountsRealAxlesAndUsesActualLength(float length, int expectedMeters)
        {
            var (_, _, _, cruiseControl) = CreateTrain(length);
            cruiseControl.Initialize();
            Assert.Equal(6, cruiseControl.SelectedNumberOfAxles);
            Assert.Equal(expectedMeters, cruiseControl.TrainLengthMeters);
        }

        [Fact]
        public void FirstPlayerUpdateInitializesDataAfterPlayerSelection()
        {
            var (simulator, _, locomotive, cruiseControl) = CreateTrain(150.1f);
            simulator.PlayerLocomotive = null;
            cruiseControl.Initialize();
            Assert.Equal(0, cruiseControl.SelectedNumberOfAxles);
            Assert.Equal(0, cruiseControl.TrainLengthMeters);
            simulator.PlayerLocomotive = locomotive;
            cruiseControl.Update(0.1f);
            Assert.Equal(6, cruiseControl.SelectedNumberOfAxles);
            Assert.Equal(151, cruiseControl.TrainLengthMeters);
        }

        [Fact]
        public void InitializationWaitsUntilThePlayerCarIsAddedToTheConsist()
        {
            var (_, train, locomotive, cruiseControl) = CreateTrain(150f);
            train.Cars.Remove(locomotive);
            cruiseControl.Initialize();
            Assert.Equal(0, cruiseControl.SelectedNumberOfAxles);
            Assert.Equal(0, cruiseControl.TrainLengthMeters);
            train.Cars.Insert(0, locomotive);
            cruiseControl.Update(0.1f);
            Assert.Equal(6, cruiseControl.SelectedNumberOfAxles);
            Assert.Equal(150, cruiseControl.TrainLengthMeters);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void UpdatesPreserveManualValuesWhenDisabledOrAlreadyInitialized(bool automatic)
        {
            var (_, _, _, cruiseControl) = CreateTrain(150f);
            cruiseControl.DoComputeNumberOfAxles = automatic;
            cruiseControl.Initialize();
            cruiseControl.SelectedNumberOfAxles = 88;
            cruiseControl.TrainLengthMeters = 321;
            cruiseControl.Update(0.1f);
            Assert.Equal(88, cruiseControl.SelectedNumberOfAxles);
            Assert.Equal(321, cruiseControl.TrainLengthMeters);
        }

        [Fact]
        public void RestoredGamesRetainSavedTrainData()
        {
            var (_, _, _, original) = CreateTrain(150f);
            original.Initialize();
            original.SelectedNumberOfAxles = 88;
            original.TrainLengthMeters = 321;
            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true)) original.Save(writer);
            stream.Position = 0;
            var (_, _, _, restored) = CreateTrain(200f);
            using (var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, true)) restored.Restore(reader);
            restored.Update(0.1f);
            Assert.Equal(88, restored.SelectedNumberOfAxles);
            Assert.Equal(321, restored.TrainLengthMeters);
        }
    }
}
