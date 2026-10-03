// COPYRIGHT 2026 by the Open Rails project. GNU GPL version 3 or later.
using Orts.Viewer3D;
using Xunit;

namespace Tests.RunActivity
{
    public class CabRainTests
    {
        [Fact]
        public void WetGlassBuildsUpAndDriesGradually()
        {
            var pane = new CabRainState();
            pane.Update(10, 1, false);
            Assert.InRange(pane.Wetness, 0.96f, 0.98f);
            pane.Update(10, 0, false);
            Assert.InRange(pane.Wetness, 0.5f, 0.6f);
            pane.Update(120, 0, false);
            Assert.InRange(pane.Wetness, 0, 0.002f);
        }

        [Fact]
        public void WipersClearFrontButOtherPanesStayWet()
        {
            var front = new CabRainState();
            var side = new CabRainState();
            front.Update(10, 1, false);
            side.Update(10, 1, false);
            front.Update(5, 1, true);
            side.Update(5, 1, false);
            Assert.InRange(front.Wetness, 0.15f, 0.17f);
            Assert.InRange(side.Wetness, 0.99f, 1);
            front.Update(10, 1, false);
            Assert.True(front.Wetness > 0.97f);
        }

        [Fact]
        public void AccumulationIsIndependentOfFrameRateAndFreezesWhenPaused()
        {
            var coarse = new CabRainState();
            var fine = new CabRainState();
            coarse.Update(8, 0.7f, false);
            for (int i = 0; i < 1600; i++) fine.Update(0.005f, 0.7f, false);
            Assert.InRange(System.Math.Abs(coarse.Wetness - fine.Wetness), 0, 0.0001f);
            float before = coarse.Wetness;
            coarse.Update(0, 0, true);
            Assert.Equal(before, coarse.Wetness);
            Assert.Equal(8, coarse.Time);
        }
    }
}
