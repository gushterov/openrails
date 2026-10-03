// COPYRIGHT 2026 by the Open Rails project. GNU GPL version 3 or later.
using System;
using Orts.Viewer3D;
using Xunit;

namespace Tests.RunActivity
{
    public class CabWiperCycleTests
    {
        [Fact]
        public void StartsImmediatelyAndWaitsOnlyAfterTheReturnStroke()
        {
            var cycle = new CabWiperCycle(1, 2);
            float moved = 0;
            void Trace(float phase, float seconds) => moved += seconds;
            cycle.Update(0.5f, true, Trace);
            Assert.Equal(1, cycle.Position);
            Assert.False(cycle.IsWaiting);
            cycle.Update(0.5f, true, Trace);
            Assert.Equal(0, cycle.Position);
            Assert.True(cycle.IsWaiting);
            cycle.Update(1.75f, true, Trace);
            Assert.Equal(1, moved); // Waiting does not keep clearing water.
            cycle.Update(0.5f, true, Trace);
            Assert.Equal(0.5f, cycle.Position);
            Assert.Equal(1.25f, moved);
        }

        [Fact]
        public void ZeroDelayRetainsContinuousOperation()
        {
            var cycle = new CabWiperCycle(1, 0);
            cycle.Update(1.25f, true);
            Assert.Equal(0.5f, cycle.Position);
            Assert.False(cycle.IsWaiting);
        }

        [Fact]
        public void SwitchingOffFinishesStrokeAndReenablingStartsImmediately()
        {
            var cycle = new CabWiperCycle(1, 5);
            cycle.Update(0.25f, true);
            float moved = 0;
            cycle.Update(10, false, (phase, seconds) => moved += seconds);
            Assert.Equal(0.75f, moved);
            Assert.Equal(0, cycle.Position);
            Assert.False(cycle.IsWaiting);
            cycle.Update(0.25f, true);
            Assert.Equal(0.5f, cycle.Position);
        }

        [Fact]
        public void SwitchingOffWhileWaitingCancelsTheWait()
        {
            var cycle = new CabWiperCycle(1, 5);
            cycle.Update(2, true);
            cycle.Update(0, false);
            Assert.False(cycle.IsWaiting);
            cycle.Update(0.25f, true);
            Assert.Equal(0.5f, cycle.Position);
        }

        [Fact]
        public void PauseFreezesBothTheBladeAndTheDelay()
        {
            var cycle = new CabWiperCycle(1, 2);
            cycle.Update(0.25f, true);
            cycle.Update(0, true, (_, __) => Assert.True(false, "Paused blade moved"));
            Assert.Equal(0.5f, cycle.Position);
            cycle.Update(1, true);
            cycle.Update(0, true, (_, __) => Assert.True(false, "Parked blade moved"));
            Assert.True(cycle.IsWaiting);
            cycle.Update(1.75f, true);
            Assert.False(cycle.IsWaiting);
            Assert.Equal(0, cycle.Position);
        }

        [Fact]
        public void LongUpdatesCrossWaitsWithoutLosingSweeps()
        {
            var cycle = new CabWiperCycle(1, 2);
            var fine = new CabWiperCycle(1, 2);
            bool fullSweep = false;
            cycle.Update(30.25f, true, (phase, seconds) => fullSweep |= seconds == 1);
            for (int i = 0; i < 121; i++) fine.Update(0.25f, true);
            Assert.True(fullSweep);
            Assert.Equal(fine.Position, cycle.Position);
            Assert.Equal(fine.IsWaiting, cycle.IsWaiting);
        }

        [Fact]
        public void IntervalDoesNotRefreshTheWipeMask()
        {
            var sweep = new CabWiperSweep(3, 1, new[] { new[] { true, false, false }, new[] { false, false, true } });
            var cycle = new CabWiperCycle(1, 2);
            var map = new float[3];
            void Trace(float phase, float seconds) => sweep.Advance(phase, seconds, 1, p => p < 0.5f ? 0 : 1);
            cycle.Update(1, true, Trace);
            Assert.True(sweep.Apply(map, 1));
            cycle.Update(1, true, Trace);
            Assert.False(sweep.Apply(map, 2));
            Assert.All(map, time => Assert.Equal(1, time));
        }
    }
}
