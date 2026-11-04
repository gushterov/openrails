// COPYRIGHT 2026 by the Open Rails project. GNU GPL version 3 or later.
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using Orts.Formats.Msts;
using Orts.Simulation;
using Orts.Simulation.Physics;
using Orts.Simulation.RollingStocks;
using Orts.Simulation.RollingStocks.SubSystems;
using Orts.Viewer3D;
using Orts.Viewer3D.RollingStock;
using Xunit;
using Event = Orts.Common.Event;

namespace Tests.RunActivity
{
    public class CabCameraDisplayTests
    {
        static CabCameraDisplayState Automatic(string returnPage = "") => new CabCameraDisplayState(
            new CameraDisplayConfiguration { ActivateOnDoors = true, HideAboveSpeedMpS = 2, ReturnPage = returnPage });

        sealed class QuietLocomotive : MSTSLocomotive
        {
            public QuietLocomotive() : base(null, null) { }
            public override void SignalEvent(Event evt) { }
        }

        static T Uninitialized<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));

        static (MSTSLocomotive Locomotive, CabRenderer Renderer) CreateDisplay(bool releaseMode = true, bool rearCab = false)
        {
            var simulator = Uninitialized<Simulator>();
            var locomotive = Uninitialized<QuietLocomotive>();
            typeof(TrainCar).GetField(nameof(TrainCar.Simulator)).SetValue(locomotive, simulator);
            locomotive.UsingRearCab = rearCab;
            locomotive.Train = Uninitialized<Train>();
            typeof(Train).GetProperty(nameof(Train.Simulator)).SetValue(locomotive.Train, simulator);
            locomotive.Train.Cars = new List<TrainCar> { locomotive };
            locomotive.Doors = new Doors(locomotive);
            var door = locomotive.Doors.RightDoor;
            door.ReleaseMode = releaseMode;
            door.RandomOpenDelayMinS = door.RandomOpenDelayMaxS = 5;
            door.OpeningDelayS = door.ClosingDelayS = 1;
            door.OpenDurationS = 2;
            locomotive.Doors.Initialize();

            // Exercise the real renderer update without constructing a graphics device.
            var renderer = Uninitialized<CabRenderer>();
            renderer.ActiveScreen = new[] { "status" };
            typeof(CabRenderer).GetField("_Locomotive", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(renderer, locomotive);
            typeof(CabRenderer).GetField("CameraDisplayStates", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(renderer, new Dictionary<int, List<CabCameraDisplayState>>
                {
                    [0] = new List<CabCameraDisplayState> { Automatic() },
                    [1] = new List<CabCameraDisplayState> { Automatic() },
                });
            return (locomotive, renderer);
        }

        [Theory]
        [InlineData(true, false)]
        [InlineData(true, true)]
        [InlineData(false, false)]
        [InlineData(false, true)]
        public void RendererActivatesOnReleaseBeforeRandomDelayAndRetainsLegacyOpening(bool releaseMode, bool rearCab)
        {
            var (locomotive, renderer) = CreateDisplay(releaseMode, rearCab);
            renderer.UpdateCameraDisplays();
            Assert.Equal("status", renderer.ActiveScreen[0]);
            locomotive.Train.SetDoors(DoorSide.Right, true);
            Assert.Equal(releaseMode ? DoorState.Closed : DoorState.Opening, locomotive.Doors.RightDoor.State);
            renderer.UpdateCameraDisplays();
            Assert.Equal("cameras", renderer.ActiveScreen[0]);
        }

        [Fact]
        public void RendererDoesNotRetriggerOnDelayedMotionAfterManualSelection()
        {
            var (locomotive, renderer) = CreateDisplay();
            locomotive.Train.SetDoors(DoorSide.Right, true);
            renderer.UpdateCameraDisplays();
            Assert.Equal("cameras", renderer.ActiveScreen[0]);
            renderer.SelectScreen("traction", 0);
            locomotive.Train.SetDoors(DoorSide.Right, true); // Same release; no new activation.
            renderer.UpdateCameraDisplays();
            Assert.Equal("traction", renderer.ActiveScreen[0]);
            locomotive.Doors.Update(5);
            Assert.Equal(DoorState.Opening, locomotive.Doors.RightDoor.State);
            renderer.UpdateCameraDisplays();
            Assert.Equal("traction", renderer.ActiveScreen[0]);
            locomotive.Doors.Update(4);
            Assert.Equal(DoorState.Closed, locomotive.Doors.RightDoor.State);
            Assert.True(locomotive.Doors.RightDoor.Released);
            renderer.UpdateCameraDisplays();
            Assert.Equal("traction", renderer.ActiveScreen[0]);
            locomotive.Train.SetDoors(DoorSide.Right, false);
            renderer.UpdateCameraDisplays();
            locomotive.Train.SetDoors(DoorSide.Right, true);
            renderer.UpdateCameraDisplays();
            Assert.Equal(DoorState.Closed, locomotive.Doors.RightDoor.State);
            Assert.Equal("cameras", renderer.ActiveScreen[0]);
        }

        [Fact]
        public void RendererSpeedCutoffWaitsForNewReleaseBeforeReactivating()
        {
            var (locomotive, renderer) = CreateDisplay();
            locomotive.Train.SetDoors(DoorSide.Right, true);
            renderer.UpdateCameraDisplays();
            Assert.Equal("cameras", renderer.ActiveScreen[0]);
            locomotive.SpeedMpS = 3;
            renderer.UpdateCameraDisplays();
            Assert.Equal("status", renderer.ActiveScreen[0]);
            locomotive.SpeedMpS = 0;
            locomotive.Doors.Update(100);
            renderer.UpdateCameraDisplays();
            Assert.Equal("status", renderer.ActiveScreen[0]);
            locomotive.Train.SetDoors(DoorSide.Right, false);
            renderer.UpdateCameraDisplays();
            locomotive.Train.SetDoors(DoorSide.Right, true);
            renderer.UpdateCameraDisplays();
            Assert.Equal("cameras", renderer.ActiveScreen[0]);
        }

        [Fact]
        public void DoorOpeningKeepsPageThroughClosingThenRestoresPreviousPageAtSpeed()
        {
            var state = Automatic();
            Assert.Equal("status", state.Update("status", false, 0));
            Assert.Equal("cameras", state.Update("status", true, 0));
            Assert.Equal("cameras", state.Update("cameras", false, 0));
            Assert.Equal("cameras", state.Update("cameras", false, 1.99f));
            Assert.Equal("status", state.Update("cameras", false, 2));
            Assert.Equal("status", state.Update("status", false, 0));
            Assert.Equal("cameras", state.Update("status", true, 0));
        }

        [Fact]
        public void PersistentOpenDoorsDoNotRetriggerAfterSpeedCutoff()
        {
            var state = Automatic();
            Assert.Equal("cameras", state.Update("default", true, 0));
            Assert.Equal("default", state.Update("cameras", true, 3));
            Assert.Equal("default", state.Update("default", true, 0));
            state.Update("default", false, 0);
            Assert.Equal("cameras", state.Update("default", true, 0));
        }

        [Theory]
        [InlineData(2)]
        [InlineData(-2)]
        public void OpeningAboveCutoffDoesNotActivateWhenSlowingDown(float speed)
        {
            var state = Automatic();
            Assert.Equal("status", state.Update("status", true, speed));
            Assert.Equal("status", state.Update("status", true, 0));
        }

        [Fact]
        public void ManualPageSelectionCancelsAutomaticReturn()
        {
            var state = Automatic();
            state.Update("status", true, 0);
            Assert.Equal("traction", state.Update("traction", true, 0));
            Assert.Equal("traction", state.Update("traction", false, 3));
        }

        [Fact]
        public void ManualSiemensPageIsUnaffectedByDoorsAndSpeed()
        {
            var state = new CabCameraDisplayState(new CameraDisplayConfiguration());
            Assert.Equal("left", state.Update("left", true, 0));
            Assert.Equal("right", state.Update("right", false, 30));
        }

        [Fact]
        public void ConfiguredReturnPageWorksInReverse()
        {
            var state = Automatic("traction");
            state.Update("status", true, 0);
            Assert.Equal("traction", state.Update("cameras", false, -2));
        }

        [Fact]
        public void ManualOrRestoredCameraPageCanRemainAboveCutoff()
        {
            var state = Automatic();
            state.Update("status", false, 0);
            Assert.Equal("cameras", state.Update("cameras", false, 0));
            Assert.Equal("cameras", state.Update("cameras", false, 2));
            Assert.Equal("cameras", Automatic().Update("cameras", false, 2));
        }

        [Theory]
        [InlineData(3)]
        [InlineData(-3)]
        public void DriverCanReopenCameraPageAfterAutomaticSpeedCutoff(float speed)
        {
            var state = Automatic();
            Assert.Equal("cameras", state.Update("status", true, 0));
            Assert.Equal("status", state.Update("cameras", true, speed));
            state.NotifyManualSelection();
            Assert.Equal("cameras", state.Update("cameras", true, speed));
            Assert.Equal("cameras", state.Update("cameras", false, speed));
        }

        [Fact]
        public void ReselectingAutomaticCameraPageOverridesItsPendingCutoff()
        {
            var state = Automatic();
            Assert.Equal("cameras", state.Update("status", true, 0));
            state.NotifyManualSelection();
            Assert.Equal("cameras", state.Update("cameras", false, 3));
            Assert.Equal("status", state.Update("status", false, 0));
            Assert.Equal("cameras", state.Update("status", true, 0));
            Assert.Equal("status", state.Update("cameras", false, 3));
        }

        [Fact]
        public void DoorOpeningDoesNotTurnManualCameraViewIntoAutomaticView()
        {
            var state = Automatic();
            state.NotifyManualSelection();
            Assert.Equal("cameras", state.Update("cameras", true, 0));
            Assert.Equal("cameras", state.Update("cameras", false, 3));
        }

        [Fact]
        public void DisplaysKeepIndependentPreviousPages()
        {
            var left = Automatic();
            var right = Automatic();
            left.Update("status", true, 0);
            right.Update("traction", true, 0);
            Assert.Equal("status", left.Update("cameras", false, 2));
            Assert.Equal("traction", right.Update("cameras", false, 2));
        }

        static CabCameraDisplayState LowSpeed(bool activateOnDoors = false) => new CabCameraDisplayState(
            new CameraDisplayConfiguration { ActivateBelowSpeedMpS = 15 / 3.6f,
                HideAboveSpeedMpS = 45 / 3.6f, ActivateOnDoors = activateOnDoors });

        [Theory]
        [InlineData(1)]
        [InlineData(-1)]
        public void SlowingBelowThresholdActivatesAndCutoffRestoresPage(float direction)
        {
            var state = LowSpeed();
            Assert.Equal("status", state.Update("status", false, direction * 20 / 3.6f));
            Assert.Equal("status", state.Update("status", false, direction * 15 / 3.6f));
            Assert.Equal("cameras", state.Update("status", false, direction * 14 / 3.6f));
            Assert.Equal("cameras", state.Update("cameras", false, direction * 30 / 3.6f));
            Assert.Equal("status", state.Update("cameras", false, direction * 45 / 3.6f));
            Assert.Equal("cameras", state.Update("status", false, direction * 14 / 3.6f));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(10)]
        [InlineData(-10)]
        [InlineData(14)]
        [InlineData(-14)]
        public void StartingBelowThresholdAndAcceleratingDoesNotActivate(float startingSpeedKph)
        {
            var state = LowSpeed();
            Assert.Equal("status", state.Update("status", false, startingSpeedKph / 3.6f));
            Assert.Equal("status", state.Update("status", false, 0));
            Assert.Equal("status", state.Update("status", false, 10 / 3.6f));
            Assert.Equal("status", state.Update("status", false, 14 / 3.6f));
            Assert.Equal("status", state.Update("status", false, 15 / 3.6f));
            Assert.Equal("cameras", state.Update("status", false, 14 / 3.6f));
        }

        [Fact]
        public void DoorOpeningAtRestStillActivatesWithSpeedTriggerConfigured()
        {
            var state = LowSpeed(true);
            Assert.Equal("status", state.Update("status", false, 0));
            Assert.Equal("cameras", state.Update("status", true, 0));
        }

        [Fact]
        public void ManualExitAtLowSpeedStaysUntilNextThresholdCrossing()
        {
            var state = LowSpeed();
            Assert.Equal("status", state.Update("status", false, 15 / 3.6f));
            Assert.Equal("cameras", state.Update("status", false, 0));
            state.NotifyManualSelection();
            Assert.Equal("status", state.Update("status", false, 0));
            Assert.Equal("status", state.Update("status", false, 14 / 3.6f));
            Assert.Equal("status", state.Update("status", false, 15 / 3.6f));
            Assert.Equal("cameras", state.Update("status", false, 14 / 3.6f));
        }

        [Fact]
        public void ManualCameraRemainsAboveCutoffAfterSpeedTrigger()
        {
            var state = LowSpeed();
            Assert.Equal("status", state.Update("status", false, 15 / 3.6f));
            Assert.Equal("cameras", state.Update("status", false, 0));
            state.NotifyManualSelection();
            Assert.Equal("cameras", state.Update("cameras", false, 20));
            Assert.Equal("cameras", state.Update("cameras", false, 0));
            Assert.Equal("cameras", state.Update("cameras", false, 20));
        }

        [Fact]
        public void DoorsCanActivateAboveLowSpeedThresholdButBelowCutoff()
        {
            var state = LowSpeed(true);
            Assert.Equal("status", state.Update("status", false, 20 / 3.6f));
            Assert.Equal("cameras", state.Update("status", true, 20 / 3.6f));
            Assert.Equal("status", state.Update("cameras", true, 45 / 3.6f));
            Assert.Equal("cameras", state.Update("status", true, 14 / 3.6f));
        }

        [Fact]
        public void SpeedTriggerDoesNotActivateAboveAutomaticCutoff()
        {
            var state = new CabCameraDisplayState(new CameraDisplayConfiguration {
                ActivateBelowSpeedMpS = 5, HideAboveSpeedMpS = 2 });
            Assert.Equal("status", state.Update("status", false, 5));
            Assert.Equal("status", state.Update("status", false, 3));
        }
    }
}
