// COPYRIGHT 2026 by the Open Rails project. GNU GPL version 3 or later.
using System;
using Orts.Common;
using Orts.Viewer3D;
using Xunit;

namespace Tests.RunActivity
{
    public class CabWiperCycleTests
    {
        [Theory]
        [InlineData(6, Event.WiperOn)]
        [InlineData(7, Event.WiperOff)]
        [InlineData(264, Event.WiperCycleStart)]
        public void WiperSoundTriggersMapToSeparateEvents(int trigger, Event expected)
        {
            Assert.Equal(expected, Events.From(Events.Source.MSTSCar, trigger));
            Assert.Equal((Event)0, Events.From(Events.Source.MSTSCrossing, 264));
        }

        [Fact]
        public void SoundStartsOncePerCycleAndNotAtReversalOrDuringDelay()
        {
            var cycle = new CabWiperCycle(1, 2);
            int starts = 0;
            void Update(float seconds) => cycle.Update(seconds, true, cycleStarted: () => starts++);
            Update(0.25f);
            Assert.Equal(1, starts);
            Update(0.25f); // Direction reversal.
            Update(0.5f); // Return to park.
            Update(1.75f); // Parked delay.
            Assert.Equal(1, starts);
            Update(0.25f); // Exactly the start of the next cycle.
            Assert.Equal(2, starts);
            Assert.Equal(0, cycle.Position);
            Update(0);
            Update(0.25f);
            Assert.Equal(2, starts);
        }

        [Fact]
        public void ContinuousWipingKeepsSoundOnTheAnimationClock()
        {
            var cycle = new CabWiperCycle(1, 0);
            int starts = 0;
            for (int i = 0; i < 400; i++)
                cycle.Update(0.25f, true, cycleStarted: () => starts++);
            Assert.Equal(101, starts); // Initial start plus the boundary at each whole second.
            Assert.Equal(0, cycle.Position);
        }

        [Fact]
        public void PauseAndInvalidUpdatesDoNotStartSound()
        {
            var cycle = new CabWiperCycle(1, 2);
            int starts = 0;
            foreach (float seconds in new[] { 0f, -1f, float.NaN, float.PositiveInfinity })
                cycle.Update(seconds, true, cycleStarted: () => starts++);
            Assert.Equal(0, starts);
            cycle.Update(0.25f, true, cycleStarted: () => starts++);
            Assert.Equal(1, starts);
            cycle.Update(0, true, cycleStarted: () => starts++);
            Assert.Equal(1, starts);
            var disabledCycle = new CabWiperCycle(0, 0);
            disabledCycle.Update(1, true, cycleStarted: () => starts++);
            Assert.Equal(1, starts);
        }

        [Fact]
        public void SwitchingOffDoesNotStartAnotherSoundWhileParking()
        {
            var cycle = new CabWiperCycle(1, 2);
            int starts = 0;
            cycle.Update(0.25f, true, cycleStarted: () => starts++);
            cycle.Update(0.25f, false, cycleStarted: () => starts++);
            cycle.Update(10, false, cycleStarted: () => starts++);
            Assert.Equal(1, starts);
            Assert.Equal(0, cycle.Position);
            cycle.Update(0.25f, true, cycleStarted: () => starts++);
            Assert.Equal(2, starts);
            cycle.Update(1, true, cycleStarted: () => starts++);
            cycle.Update(0, false, cycleStarted: () => starts++);
            cycle.Update(0.25f, true, cycleStarted: () => starts++);
            Assert.Equal(3, starts);
        }

        [Fact]
        public void LongFrameCoalescesMissedSoundsAndSkipsFinishedWipes()
        {
            var cycle = new CabWiperCycle(1, 2);
            int starts = 0;
            cycle.Update(30.25f, true, cycleStarted: () => starts++);
            Assert.Equal(1, starts);
            Assert.Equal(0.5f, cycle.Position);
            cycle.Update(31, true, cycleStarted: () => starts++);
            Assert.Equal(1, starts); // Ends in the parked delay.
            Assert.True(cycle.IsWaiting);
            cycle.Update(1.75f, true, cycleStarted: () => starts++);
            Assert.Equal(2, starts);
        }

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
