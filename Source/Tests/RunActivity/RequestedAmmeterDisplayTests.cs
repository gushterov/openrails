// COPYRIGHT 2026 by the Open Rails project. GNU GPL version 3 or later.
using System.Reflection;
using System.Runtime.Serialization;
using Orts.Formats.Msts;
using Orts.Simulation;
using Orts.Simulation.RollingStocks;
using Orts.Simulation.RollingStocks.SubSystems;
using Orts.Simulation.RollingStocks.SubSystems.PowerSupplies;
using Orts.Viewer3D.RollingStock;
using ORTS.Scripting.Api;
using Xunit;

namespace Tests.RunActivity
{
    public class RequestedAmmeterDisplayTests
    {
        sealed class TestLocomotive : MSTSLocomotive
        {
            public float RequestedForce;
            public TestLocomotive() : base(null, null) { }
            public override float GetDataOf(CabViewControl control) => RequestedForce;
        }

        static T Uninitialized<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));

        static (TestLocomotive locomotive, Simulator simulator, CabViewControlRenderer renderer) CreateDisplay(bool dial = false)
        {
            var locomotive = Uninitialized<TestLocomotive>();
            locomotive.RequestedForce = 100;
            locomotive.CruiseControl = Uninitialized<CruiseControl>();
            locomotive.CruiseControl.SpeedRegMode = CruiseControl.SpeedRegulatorMode.Auto;
            locomotive.PowerSupply = Uninitialized<ScriptedElectricPowerSupply>();
            locomotive.LocomotivePowerSupply.LowVoltagePowerSupplyState = PowerSupplyState.PowerOn;
            var simulator = Uninitialized<Simulator>();
            simulator.ClockTime = 100;
            typeof(TrainCar).GetField(nameof(TrainCar.Simulator)).SetValue(locomotive, simulator);
            var control = new CVCGauge
            {
                ControlType = new CabViewControlType(CABViewControlTypes.ORTS_AMMETER_REQUESTED),
                MinValue = -200,
                MaxValue = 200,
            };
            CabViewControlRenderer renderer = dial ? (CabViewControlRenderer)Uninitialized<CabViewDialRenderer>()
                : Uninitialized<CabViewGaugeRenderer>();
            typeof(CabViewControlRenderer).GetField("Locomotive", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(renderer, locomotive);
            typeof(CabViewControlRenderer).GetField("Control").SetValue(renderer, control);
            if (!dial)
                typeof(CabViewGaugeRenderer).GetField("Gauge", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(renderer, control);
            return (locomotive, simulator, renderer);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void TractionMovesSmoothlyThroughZeroIntoBraking(bool dial)
        {
            var (locomotive, simulator, renderer) = CreateDisplay(dial);
            Assert.Equal(0.75f, renderer.GetRangeFraction());
            locomotive.RequestedForce = 0;
            simulator.ClockTime += 0.1;
            var reducingTraction = renderer.GetRangeFraction();
            Assert.InRange(reducingTraction, 0.5f, 0.75f);
            Assert.True(reducingTraction > 0.5f);

            locomotive.RequestedForce = -100;
            simulator.ClockTime += 0.05;
            var enteringBraking = renderer.GetRangeFraction();
            Assert.InRange(enteringBraking, 0.5f, reducingTraction);
            if (renderer is CabViewGaugeRenderer gauge)
            {
                gauge.GetColor(out bool positive);
                Assert.True(positive);
            }

            simulator.ClockTime += 0.1;
            var braking = renderer.GetRangeFraction();
            Assert.InRange(braking, 0.25f, 0.5f);
            if (renderer is CabViewGaugeRenderer brakingGauge)
            {
                brakingGauge.GetColor(out bool positive);
                Assert.False(positive);
            }
            simulator.ClockTime += 2;
            Assert.Equal(0.25f, renderer.GetRangeFraction(), 4);
            Assert.Equal(-100f, locomotive.RequestedForce);
        }

        [Fact]
        public void RepeatedReadsAndPausedSimulationDoNotAdvanceTheDisplay()
        {
            var (locomotive, simulator, renderer) = CreateDisplay();
            renderer.GetRangeFraction();
            locomotive.RequestedForce = -100;
            Assert.Equal(0.75f, renderer.GetRangeFraction());
            simulator.ClockTime += 0.1;
            var fraction = renderer.GetRangeFraction();
            for (int read = 0; read < 10; read++)
            {
                ((CabViewGaugeRenderer)renderer).GetColor(out _);
                Assert.Equal(fraction, renderer.GetRangeFraction());
                Assert.Equal(fraction - 0.5f, renderer.GetRangeFraction(true), 6);
            }
            simulator.ClockTime = 0;
            Assert.Equal(0.25f, renderer.GetRangeFraction());
        }

        [Fact]
        public void ResponseIsIndependentOfFrameRate()
        {
            var (slowLocomotive, slowSimulator, slowRenderer) = CreateDisplay();
            var (fastLocomotive, fastSimulator, fastRenderer) = CreateDisplay();
            slowRenderer.GetRangeFraction();
            fastRenderer.GetRangeFraction();
            slowLocomotive.RequestedForce = fastLocomotive.RequestedForce = -100;
            slowSimulator.ClockTime += 0.2;
            var expected = slowRenderer.GetRangeFraction();
            for (int frame = 0; frame < 20; frame++)
            {
                fastSimulator.ClockTime += 0.01;
                fastRenderer.GetRangeFraction();
            }
            Assert.Equal(expected, fastRenderer.GetRangeFraction(), 6);
        }

        [Fact]
        public void ManualModeAndOtherMetersRemainImmediate()
        {
            var (locomotive, _, renderer) = CreateDisplay();
            renderer.GetRangeFraction();
            locomotive.CruiseControl.SpeedRegMode = CruiseControl.SpeedRegulatorMode.Manual;
            locomotive.RequestedForce = -100;
            Assert.Equal(0.25f, renderer.GetRangeFraction());
            locomotive.CruiseControl.SpeedRegMode = CruiseControl.SpeedRegulatorMode.Auto;
            locomotive.RequestedForce = 100;
            Assert.Equal(0.75f, renderer.GetRangeFraction());
            renderer.Control.ControlType = new CabViewControlType(CABViewControlTypes.AMMETER);
            locomotive.RequestedForce = -100;
            Assert.Equal(0.25f, renderer.GetRangeFraction());
            renderer.Control.ControlType = new CabViewControlType(CABViewControlTypes.ORTS_AMMETER_REQUESTED);
            locomotive.CruiseControl = null;
            locomotive.RequestedForce = 100;
            Assert.Equal(0.75f, renderer.GetRangeFraction());
        }

        [Fact]
        public void PowerOffValueIsImmediateAndPowerOnStartsAtCurrentDemand()
        {
            var (locomotive, simulator, renderer) = CreateDisplay();
            typeof(CabViewControl).GetProperty(nameof(CabViewControl.DisabledIfLowVoltagePowerSupplyOff))
                .SetValue(renderer.Control, true);
            typeof(CabViewControl).GetProperty(nameof(CabViewControl.ValueIfDisabled))
                .SetValue(renderer.Control, 0f);
            renderer.GetRangeFraction();
            locomotive.LocomotivePowerSupply.LowVoltagePowerSupplyState = PowerSupplyState.PowerOff;
            locomotive.RequestedForce = -100;
            simulator.ClockTime += 0.1;
            Assert.Equal(0.5f, renderer.GetRangeFraction());
            locomotive.LocomotivePowerSupply.LowVoltagePowerSupplyState = PowerSupplyState.PowerOn;
            Assert.Equal(0.25f, renderer.GetRangeFraction());
        }
    }
}
