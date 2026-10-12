// COPYRIGHT 2026 by the Open Rails project. GNU GPL version 3 or later.
using Orts.Formats.Msts;
using Orts.Viewer3D;
using Xunit;

namespace Tests.RunActivity
{
    public class CabCameraDisplayTests
    {
        static CabCameraDisplayState Automatic(string returnPage = "") => new CabCameraDisplayState(
            new CameraDisplayConfiguration { ActivateOnDoors = true, HideAboveSpeedMpS = 2, ReturnPage = returnPage });

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
    }
}
