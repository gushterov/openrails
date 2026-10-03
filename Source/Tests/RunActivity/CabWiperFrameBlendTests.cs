// COPYRIGHT 2026 by the Open Rails project. GNU GPL version 3 or later.
using System;
using Orts.Viewer3D;
using Xunit;

namespace Tests.RunActivity
{
    public class CabWiperFrameBlendTests
    {
        [Theory]
        [InlineData(0f, 0, 0, 0f)]
        [InlineData(0.125f, 0, 1, 0.5f)]
        [InlineData(0.25f, 1, 1, 0f)]
        [InlineData(0.625f, 1, 2, 0.5f)]
        [InlineData(1f, 2, 2, 0f)]
        public void FollowsAuthoredFramePositions(float position, int first, int second, float amount)
        {
            var blend = CabWiperFrameBlend.Select(position, new double[] { 0, 0.25, 1 }, 3, false);
            Assert.Equal(first, blend.First);
            Assert.Equal(second, blend.Second);
            Assert.Equal(amount, blend.Amount);
        }

        [Fact]
        public void ReversedSheetBlendsTheCorrespondingFrames()
        {
            var blend = CabWiperFrameBlend.Select(0.125f, new double[] { 0, 0.25, 1 }, 3, true);
            Assert.Equal(2, blend.First);
            Assert.Equal(1, blend.Second);
            Assert.Equal(0.5f, blend.Amount);
        }

        [Fact]
        public void HoldsTheFinalFrameIfTheLastSwitchValueIsBelowOne()
        {
            var blend = CabWiperFrameBlend.Select(1, new double[] { 0, 0.5, 0.9 }, 3, false);
            Assert.Equal(2, blend.First);
            Assert.Equal(2, blend.Second);
            Assert.Equal(0, blend.Amount);
        }

        [Fact]
        public void DuplicateValuesDoNotDivideByZero()
        {
            var blend = CabWiperFrameBlend.Select(0.5f, new double[] { 0, 0, 1 }, 3, false);
            Assert.Equal(1, blend.First);
            Assert.Equal(2, blend.Second);
            Assert.Equal(0.5f, blend.Amount);
        }

        [Fact]
        public void SingleFrameStaysOpaqueWithoutInterpolation()
        {
            var blend = CabWiperFrameBlend.Select(0.5f, Array.Empty<double>(), 1, false);
            Assert.Equal(0, blend.First);
            Assert.Equal(0, blend.Second);
            Assert.Equal(0, blend.Amount);
        }

        [Fact]
        public void BothStrokesUseTheSameBlendAndParkWithoutGhosting()
        {
            var cycle = new CabWiperCycle(2, 1);
            var values = new double[] { 0, 0.5, 1 };
            cycle.Update(0.25f, true);
            var outward = CabWiperFrameBlend.Select(cycle.Position, values, 3, false);
            cycle.Update(1.5f, true);
            var returning = CabWiperFrameBlend.Select(cycle.Position, values, 3, false);
            Assert.Equal(outward, returning);
            cycle.Update(0.5f, true);
            var parked = CabWiperFrameBlend.Select(cycle.Position, values, 3, false);
            Assert.Equal(0, parked.First);
            Assert.Equal(0, parked.Second);
        }
    }
}
