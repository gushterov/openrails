// COPYRIGHT 2026 by the Open Rails project. GNU GPL version 3 or later.
using ORTS.Common;
using Xunit;

namespace Tests.Orts.Common
{
    public class IIRFilterTests
    {
        [Theory]
        [InlineData(0f)]
        [InlineData(-0.01f)]
        public void NonPositiveSamplingPeriodPreservesOutputAndHistory(float samplingPeriod)
        {
            var filter = new IIRFilter(IIRFilter.FilterTypes.Butterworth, 1, IIRFilter.HzToRad(0.7f), 0.01f);
            var reference = new IIRFilter(IIRFilter.FilterTypes.Butterworth, 1, IIRFilter.HzToRad(0.7f), 0.01f);
            float output = 0;
            for (int i = 0; i < 20; i++)
            {
                output = filter.Filter(25000, 0.01f);
                reference.Filter(25000, 0.01f);
            }
            Assert.True(output > 0);

            for (int i = 0; i < 100; i++)
                Assert.Equal(output, filter.Filter(0, samplingPeriod));

            // Resuming must continue from the same history as an uninterrupted filter.
            Assert.Equal(reference.Filter(0, 0.01f), filter.Filter(0, 0.01f));
        }

        [Fact]
        public void PausedFilterPreservesInitialAndResetZeroOutput()
        {
            var filter = new IIRFilter(IIRFilter.FilterTypes.Butterworth, 1, IIRFilter.HzToRad(0.7f), 0.01f);
            Assert.Equal(0, filter.Filter(25000, 0));
            Assert.True(filter.Filter(25000, 0.01f) > 0);
            filter.Reset();
            Assert.Equal(0, filter.Filter(25000, 0));
        }
    }
}
