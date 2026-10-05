// COPYRIGHT 2026 by the Open Rails project. GNU GPL version 3 or later.
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Orts.Formats.Msts;
using Orts.Simulation;
using Orts.Simulation.Physics;
using Orts.Simulation.RollingStocks;
using Orts.Viewer3D;
using Orts.Viewer3D.Popups;
using ORTS.Common;
using ORTS.Common.Input;
using ORTS.Settings;
using Xunit;

namespace Tests.RunActivity
{
    public class CabDirectionTests
    {
        static T Uninitialized<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));

        static void SetProperty(object target, string name, object value) =>
            target.GetType().GetProperty(name).SetValue(target, value);

        static void SetSoundField(TrackSoundSource source, string name, object value) =>
            typeof(TrackSoundSource).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(source, value);

        static T SoundField<T>(TrackSoundSource source, string name) =>
            (T)typeof(TrackSoundSource).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(source);

        static (Train train, Simulator simulator, MSTSLocomotive loco) CreateTrain(bool flipped, bool rearCab)
        {
            var simulator = Uninitialized<Simulator>();
            var train = Uninitialized<Train>();
            SetProperty(train, nameof(Train.Simulator), simulator);
            var loco = Uninitialized<MSTSLocomotive>();
            loco.Train = train;
            loco.Flipped = flipped;
            loco.UsingRearCab = rearCab;
            simulator.PlayerLocomotive = loco;
            train.Cars = new List<TrainCar> { loco, Uninitialized<MSTSWagon>(), Uninitialized<MSTSLocomotive>() };
            foreach (var car in train.Cars) car.Train = train;
            return (train, simulator, loco);
        }

        [Theory]
        [InlineData(false, false, false)]
        [InlineData(false, true, true)]
        [InlineData(true, false, true)]
        [InlineData(true, true, false)]
        public void CabAndVehicleOrientationChooseTheForwardEnd(bool flipped, bool rearCab, bool expectedRear)
        {
            var (train, simulator, loco) = CreateTrain(flipped, rearCab);
            Assert.Equal(expectedRear, train.IsPlayerCabReversed);
            Assert.Equal(expectedRear, train.LeadingEndIsRear);

            // An opposite-facing EMU driving car must behave like a rear cab.
            var otherCab = (MSTSLocomotive)train.Cars[2];
            otherCab.Flipped = !expectedRear;
            simulator.PlayerLocomotive = otherCab;
            Assert.Equal(!expectedRear, train.IsPlayerCabReversed);
            Assert.Same(loco, train.Cars[0]); // Presentation must not reorder the formation.
        }

        [Theory]
        [InlineData(false, 5f, false)]
        [InlineData(false, -5f, true)]
        [InlineData(true, 5f, false)]
        [InlineData(true, -5f, true)]
        [InlineData(true, 0f, true)]
        [InlineData(false, 0f, false)]
        public void LocationTriggersFollowTravelAndUseTheCabAtRest(bool rearCab, float speed, bool expectedRear)
        {
            var (train, _, _) = CreateTrain(false, rearCab);
            train.SpeedMpS = speed;
            Assert.Equal(expectedRear, train.LeadingEndIsRear);
        }

        [Fact]
        public void AnotherTrainDoesNotInheritThePlayersCabOrientation()
        {
            var (train, simulator, _) = CreateTrain(false, true);
            simulator.PlayerLocomotive = Uninitialized<MSTSLocomotive>();
            Assert.False(train.IsPlayerCabReversed);
            train.MUDirection = Direction.Forward;
            Assert.False(train.LeadingEndIsRear);
        }

        static TrackSoundSource CreateTrackSound(Train train, Simulator simulator, int increment)
        {
            var viewer = Uninitialized<Viewer>();
            SetProperty(viewer, nameof(Viewer.Simulator), simulator);
            var world = Uninitialized<World>();
            typeof(World).GetField("Sounds").SetValue(world, new WorldSounds(viewer));
            SetProperty(viewer, nameof(Viewer.World), world);
            simulator.TDB = Uninitialized<TrackDatabaseFile>();
            simulator.TDB.TrackDB = Uninitialized<TrackDB>();
            train.FrontTDBTraveller = Uninitialized<Traveller>();
            train.RearTDBTraveller = Uninitialized<Traveller>();
            train.PresentPosition = new[] { new Train.TCPosition(), new Train.TCPosition() };
            var source = Uninitialized<TrackSoundSource>();
            source.Viewer = viewer;
            source.Car = (MSTSWagon)train.Cars[increment > 0 ? 0 : train.Cars.Count - 1];
            SetSoundField(source, "initCar", source.Car);
            SetSoundField(source, "initCarIncr", increment);
            SetSoundField(source, "initTrackSection", train.PresentPosition[0].TCSectionIndex);
            return source;
        }

        [Theory]
        [InlineData(1, 1)]
        [InlineData(1, -1)]
        [InlineData(3, 1)]
        [InlineData(3, -1)]
        public void TrackSoundsRefreshWhenTheLeadingEndCrossesTheNextBoundary(int carCount, int increment)
        {
            var (train, simulator, _) = CreateTrain(false, increment < 0);
            train.Cars.RemoveRange(carCount, train.Cars.Count - carCount);
            train.Length = carCount * 20;
            train.SpeedMpS = increment * 5;
            train.DistanceTravelledM = 100;
            var source = CreateTrackSound(train, simulator, increment);
            float initialDistance = increment > 0 ? 100 : 100 - train.Length;
            SetSoundField(source, "initDist", initialDistance);
            SetSoundField(source, "nextDist", 10f);
            SetSoundField(source, "prevDist", 1000f);

            train.DistanceTravelledM += increment * 9;
            source.UpdateTType(false);
            Assert.Equal(initialDistance, SoundField<float>(source, "initDist"));
            train.DistanceTravelledM += increment * 2;
            source.UpdateTType(false);
            Assert.Equal(initialDistance + increment * 11, SoundField<float>(source, "initDist"));
        }

        [Fact]
        public void SingleLocomotiveReselectsTrackSoundsWhenTravelDirectionChanges()
        {
            var (train, simulator, _) = CreateTrain(false, false);
            train.Cars.RemoveRange(1, 2);
            train.Length = 20;
            train.DistanceTravelledM = 100;
            var source = CreateTrackSound(train, simulator, 1);
            SetSoundField(source, "initDist", 100f);
            SetSoundField(source, "nextDist", 500f);
            SetSoundField(source, "prevDist", 500f);
            train.SpeedMpS = -5;
            source.UpdateTType(false);
            Assert.Equal(-1, SoundField<int>(source, "initCarIncr"));
            Assert.Equal(80, SoundField<float>(source, "initDist"));
        }

        [Theory]
        [InlineData(Train.TRAIN_CONTROL.AUTO_NODE)]
        [InlineData(Train.TRAIN_CONTROL.AUTO_SIGNAL)]
        [InlineData(Train.TRAIN_CONTROL.MANUAL)]
        [InlineData(Train.TRAIN_CONTROL.EXPLORER)]
        public void MonitorShowsSignalsAheadOfRearCabAtTheTop(Train.TRAIN_CONTROL mode)
        {
            var info = new Train.TrainInfo
            {
                ControlMode = mode,
                cabOrientation = 1,
                direction = 1,
                speedMpS = -12,
                projectedSpeedMpS = -15,
                currentElevationPercent = 2,
            };
            var ahead = new Train.TrainObjectItem(Train.END_AUTHORITY.END_OF_AUTHORITY, 150);
            var behind = new Train.TrainObjectItem(Train.END_AUTHORITY.NO_PATH_RESERVED, 50);
            info.ObjectInfoBackward.Add(ahead);
            info.ObjectInfoForward.Add(behind);

            info.UseCabOrientation();
            Assert.Same(ahead, Assert.Single(info.ObjectInfoForward));
            Assert.Same(behind, Assert.Single(info.ObjectInfoBackward));
            Assert.Equal(150, ahead.DistanceToTrainM);
            Assert.Equal(0, info.direction);
            Assert.Equal(0, info.cabOrientation);
            Assert.Equal(12, info.speedMpS);
            Assert.Equal(15, info.projectedSpeedMpS);
            Assert.Equal(2, info.currentElevationPercent);
            info.UseCabOrientation();
            Assert.Same(ahead, Assert.Single(info.ObjectInfoForward));
        }

        [Theory]
        [InlineData(0, 1)]
        [InlineData(-1, -1)]
        public void MonitorPreservesReverseAndNeutralRelativeToCab(int direction, int expected)
        {
            var info = new Train.TrainInfo { cabOrientation = 1, direction = direction };
            info.UseCabOrientation();
            Assert.Equal(expected, info.direction);
        }

        [Fact]
        public void OutOfControlStatusStaysAvailableToMonitor()
        {
            var info = new Train.TrainInfo { ControlMode = Train.TRAIN_CONTROL.OUT_OF_CONTROL, cabOrientation = 1 };
            var reason = new Train.TrainObjectItem(default(Train.OUTOFCONTROL));
            info.ObjectInfoForward.Add(reason);
            info.UseCabOrientation();
            Assert.Same(reason, Assert.Single(info.ObjectInfoForward));
        }

        class TestTrackingCamera : TrackingCamera
        {
            public TestTrackingCamera(Viewer viewer, AttachedTo end) : base(viewer, end) { }
            public static TestTrackingCamera Create(Viewer viewer, bool front)
            {
                // Camera orientation does not require graphics, audio, or user settings.
                var camera = Uninitialized<TestTrackingCamera>();
                typeof(global::Orts.Viewer3D.Camera).GetField("Viewer", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(camera, viewer);
                typeof(TrackingCamera).GetField("OutsideFront", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(camera, front);
                return camera;
            }
            public bool AtConsistFront => Front;
            public float Orbit => PositionYRadians;
            public void Attach(TrainCar car) => attachedCar = car;
            protected override void SetCameraCar(TrainCar car) => attachedCar = car;
            public void ActivateWithoutRendering() => OnActivate(false);
        }

        [Theory]
        [InlineData(false, true, 0)]
        [InlineData(false, false, 2)]
        [InlineData(true, true, 2)]
        [InlineData(true, false, 0)]
        public void PressingTwoOrThreeAttachesToTheCorrectEnd(bool rearCab, bool outsideFront, int expectedCar)
        {
            var (train, _, loco) = CreateTrain(false, false);
            train.FrontTDBTraveller = Uninitialized<Traveller>();
            train.RearTDBTraveller = Uninitialized<Traveller>();
            for (int i = 0; i < train.Cars.Count; i++) train.Cars[i].CarID = i.ToString();
            var viewer = Uninitialized<Viewer>();
            SetProperty(viewer, nameof(Viewer.SelectedTrain), train);
            SetProperty(viewer, nameof(Viewer.TrainCarOperationsViewerWindow), Uninitialized<TrainCarOperationsViewerWindow>());
            var camera = TestTrackingCamera.Create(viewer, outsideFront);
            loco.UsingRearCab = rearCab;

            var input = Uninitialized<InputSettings>();
            var commands = new UserCommandInput[Enum.GetNames(typeof(UserCommand)).Length];
            commands[(int)UserCommand.CameraOutsideFront] = new UserCommandKeyInput(Keys.D2);
            commands[(int)UserCommand.CameraOutsideRear] = new UserCommandKeyInput(Keys.D3);
            typeof(InputSettings).GetField("Commands").SetValue(input, commands);
            var settingsField = typeof(UserInput).GetField("InputSettings", BindingFlags.Static | BindingFlags.NonPublic);
            var keyboardField = typeof(UserInput).GetField("KeyboardState", BindingFlags.Static | BindingFlags.NonPublic);
            var previousSettings = settingsField.GetValue(null);
            var previousKeyboard = keyboardField.GetValue(null);
            try
            {
                settingsField.SetValue(null, input);
                keyboardField.SetValue(null, new KeyboardState(outsideFront ? Keys.D2 : Keys.D3));
                camera.ActivateWithoutRendering();
                Assert.Same(train.Cars[expectedCar], camera.AttachedCar);
            }
            finally
            {
                settingsField.SetValue(null, previousSettings);
                keyboardField.SetValue(null, previousKeyboard);
            }
        }

        [Fact]
        public void OutsideCamerasFollowCabChangesAndPhysicalReversals()
        {
            var (train, simulator, loco) = CreateTrain(false, false);
            var viewer = Uninitialized<Viewer>();
            SetProperty(viewer, nameof(Viewer.SelectedTrain), train);
            var front = TestTrackingCamera.Create(viewer, true);
            var rear = TestTrackingCamera.Create(viewer, false);
            float originalOrbit = front.Orbit;
            front.Attach(train.Cars[0]);
            rear.Attach(train.Cars[2]);
            loco.UsingRearCab = true;
            front.UpdateCabOrientation();
            rear.UpdateCabOrientation();
            Assert.False(front.AtConsistFront);
            Assert.True(rear.AtConsistFront);
            Assert.Null(front.AttachedCar);
            Assert.Null(rear.AttachedCar);
            Assert.Equal(2, viewer.CameraOutsideRearPosition);
            Assert.Equal(0, viewer.CameraOutsideFrontPosition);
            Assert.InRange(front.Orbit - originalOrbit, MathHelper.Pi - 0.001f, MathHelper.Pi + 0.001f);

            // The signalling system can reverse the formation after a path reversal.
            // The same cab then faces the stored front; camera 2 must follow it.
            train.Cars.Reverse();
            foreach (var car in train.Cars) car.Flipped = !car.Flipped;
            front.UpdateCabOrientation();
            rear.UpdateCabOrientation();
            Assert.True(front.AtConsistFront);
            Assert.False(rear.AtConsistFront);
            Assert.Same(loco, simulator.PlayerLocomotive);
        }
    }
}
