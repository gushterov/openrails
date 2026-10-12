// COPYRIGHT 2026 by the Open Rails project. GNU GPL version 3 or later.
using System.Reflection;
using System.Runtime.Serialization;
using Orts.Formats.Msts;
using Orts.Simulation.RollingStocks;
using Orts.Simulation.RollingStocks.SubSystems;
using Orts.Viewer3D;
using Orts.Viewer3D.RollingStock;
using ORTS.Scripting.Api;
using Xunit;

namespace Tests.Orts.Simulation
{
    public class CruiseControlDisplayTests
    {
        static (MSTSLocomotive locomotive, OdoMeter odometer, CabViewGaugeRenderer gauge) CreateGauge(
            CruiseControl.SpeedRegulatorMode mode, int trainLength)
        {
            var locomotive = (MSTSLocomotive)FormatterServices.GetUninitializedObject(typeof(MSTSLocomotive));
            var cruiseControl = (CruiseControl)FormatterServices.GetUninitializedObject(typeof(CruiseControl));
            locomotive.CruiseControl = cruiseControl;
            cruiseControl.SpeedRegMode = mode;
            cruiseControl.TrainLengthMeters = trainLength;
            var odometer = new OdoMeter(locomotive);
            odometer.Setup(trainLength);
            typeof(CruiseControl).GetField("RestrictedRegionOdometer", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(cruiseControl, odometer);
            var control = new CVCGauge
            {
                ControlType = new CabViewControlType(CABViewControlTypes.ORTS_REMAINING_TRAIN_LENGTH_PERCENT),
                MinValue = 0,
                MaxValue = 100,
            };
            var gauge = (CabViewGaugeRenderer)FormatterServices.GetUninitializedObject(typeof(CabViewGaugeRenderer));
            typeof(CabViewControlRenderer).GetField("Locomotive", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(gauge, locomotive);
            typeof(CabViewControlRenderer).GetField("Control").SetValue(gauge, control);
            return (locomotive, odometer, gauge);
        }

        [Theory]
        [InlineData(CruiseControl.SpeedRegulatorMode.Auto)]
        [InlineData(CruiseControl.SpeedRegulatorMode.Manual)]
        public void RestrictedZoneFillsGaugeThenEmptiesAsTrainPasses(CruiseControl.SpeedRegulatorMode mode)
        {
            var (locomotive, odometer, gauge) = CreateGauge(mode, 200);
            locomotive.DistanceM = 1000;
            Assert.Equal(0f, gauge.GetRangeFraction());
            odometer.Start();
            foreach (var distance in new[] { 0f, 50f, 100f, 150f, 200f, 201f })
            {
                locomotive.DistanceM = 1000 + distance;
                var expected = distance > 200 ? 0 : 100 - distance / 2;
                Assert.Equal(expected, locomotive.GetDataOf(gauge.Control));
                Assert.Equal(expected / 100, gauge.GetRangeFraction());
            }
            odometer.Stop();
            Assert.Equal(0f, gauge.GetRangeFraction());
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void InvalidTrainLengthLeavesGaugeEmpty(int trainLength)
        {
            var (_, odometer, gauge) = CreateGauge(CruiseControl.SpeedRegulatorMode.Auto, trainLength);
            odometer.Start();
            Assert.Equal(0f, gauge.GetRangeFraction());
        }

        [Fact]
        public void RemainingPercentageCannotExceedFullScale()
        {
            var (locomotive, odometer, gauge) = CreateGauge(CruiseControl.SpeedRegulatorMode.Auto, 200);
            locomotive.DistanceM = 1000;
            odometer.Start();
            locomotive.DistanceM = 990;
            Assert.Equal(100f, locomotive.GetDataOf(gauge.Control));
            Assert.Equal(1f, gauge.GetRangeFraction());
            odometer.Stop();
            Assert.Equal(0f, gauge.GetRangeFraction());
        }
    }
}
