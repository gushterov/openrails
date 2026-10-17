// COPYRIGHT 2026 by the Open Rails project. GNU GPL version 3 or later.
using System.Collections.Generic;
using System.Reflection;
using Orts.Formats.Msts;
using Orts.Parsers.Msts;
using Orts.Simulation.RollingStocks;
using Orts.Simulation.RollingStocks.SubSystems;
using Orts.Simulation.RollingStocks.SubSystems.Controllers;
using Orts.Simulation.RollingStocks.SubSystems.PowerTransmissions;
using ORTS.Scripting.Api;
using Xunit;

namespace Tests.Orts.Simulation
{
    public class RequestedAmmeterTests
    {
        static CabViewControl Control() => new CabViewControl
        {
            ControlType = new CabViewControlType(CABViewControlTypes.ORTS_AMMETER_REQUESTED),
            MaxValue = 300,
        };

        static MSTSLocomotive CreateLocomotive()
        {
            var locomotive = CoastingCabControlTests.CreateLocomotive(20, -1, 50, true, true);
            locomotive.LocomotivePowerSupply.MainPowerSupplyState = PowerSupplyState.PowerOn;
            locomotive.LocomotiveAxles = new Axles(locomotive);
            locomotive.LocomotiveAxles.Add(new Axle(locomotive));
            locomotive.TractiveForceCurves = new InterpolatorDiesel2D(3)
            {
                Size = 3,
                X = new[] { 0f, 0.5f, 1f },
                Y = new[]
                {
                    new Interpolator(new[] { 0f, 20f }, new[] { 0f, 0f }),
                    new Interpolator(new[] { 0f, 20f }, new[] { 120000f, 80000f }),
                    new Interpolator(new[] { 0f, 20f }, new[] { 300000f, 200000f }),
                },
            };
            locomotive.AbsTractionSpeedMpS = 10;
            return locomotive;
        }

        static MSTSLocomotive CreateDriverControls(bool combinedHandle)
        {
            var locomotive = CreateLocomotive();
            locomotive.ThrottleController = new MSTSNotchController(new List<MSTSNotch> { new MSTSNotch(0, true, 0) });
            locomotive.DynamicBrakeController = new MSTSNotchController(new List<MSTSNotch> { new MSTSNotch(0, true, 0) });
            locomotive.DynamicBrakeForceCurves = locomotive.TractiveForceCurves;
            locomotive.CruiseControl.UseThrottleAsForceSelector = true;
            locomotive.CruiseControl.UseThrottleInCombinedControl = combinedHandle;
            locomotive.CombinedControlType = combinedHandle ? MSTSLocomotive.CombinedControl.ThrottleDynamic : MSTSLocomotive.CombinedControl.None;
            return locomotive;
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void AutomaticBrakingDoesNotReplaceTheDriversTractionSelection(bool combinedHandle)
        {
            var locomotive = CreateDriverControls(combinedHandle);
            var control = Control();
            foreach (var brake in new[] { 0f, 20f, 80f, 100f })
            {
                locomotive.DynamicBrake = true;
                locomotive.LocalThrottlePercent = 0;
                locomotive.LocalDynamicBrakePercent = brake;
                locomotive.DynamicBrakeForceN = brake * 2000;
                locomotive.CruiseControl.CCThrottleOrDynBrakePercent = -brake;
                Assert.Equal(100f, locomotive.GetDataOf(control));
            }
            locomotive.CruiseControl.SelectedMaxAccelerationPercent = 0;
            Assert.Equal(0f, locomotive.GetDataOf(control));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void DriverBrakeSelectionIsIndependentOfAutomaticAndBlendedBraking(bool combinedHandle)
        {
            var locomotive = CreateDriverControls(combinedHandle);
            var control = Control();
            control.DynamicBrakeScale = 2;
            locomotive.CruiseControl.SelectedMaxAccelerationPercent = 0;
            locomotive.DynamicBrakeController.SetPercent(50);
            foreach (var automaticBrake in new[] { -1f, 0f, 20f, 90f })
            {
                locomotive.DynamicBrake = automaticBrake > 0;
                locomotive.LocalDynamicBrakePercent = automaticBrake;
                locomotive.LocomotivePowerSupply.PowerSupplyDynamicBrakePercent = 100;
                locomotive.CruiseControl.CCThrottleOrDynBrakePercent = -automaticBrake;
                Assert.Equal(-200f, locomotive.GetDataOf(control));
            }
            locomotive.DynamicBrakeController.SetPercent(100);
            Assert.Equal(-500f, locomotive.GetDataOf(control));
            locomotive.DynamicBrakeController.SetPercent(0);
            Assert.Equal(0f, locomotive.GetDataOf(control));
            locomotive.CruiseControl.SelectedMaxAccelerationPercent = 50;
            Assert.Equal(100f, locomotive.GetDataOf(control));
        }

        [Fact]
        public void ManualModeKeepsUsingAppliedBrakeDemand()
        {
            var locomotive = CreateDriverControls(false);
            locomotive.CruiseControl.SpeedRegMode = CruiseControl.SpeedRegulatorMode.Manual;
            locomotive.DynamicBrake = true;
            locomotive.DynamicBrakeController.SetPercent(10);
            locomotive.LocalDynamicBrakePercent = 50;
            Assert.Equal(-100f, locomotive.GetDataOf(Control()));
        }

        [Fact]
        public void AutomaticModeShowsSelectedLimitWhileAppliedTractionChanges()
        {
            var locomotive = CreateLocomotive();
            var control = Control();
            foreach (var throttle in new[] { 0f, 10f, 50f, 90f })
            {
                locomotive.LocalThrottlePercent = throttle;
                locomotive.TractionForceN = throttle * 2000;
                locomotive.TractiveForceN = locomotive.TractionForceN;
                locomotive.CruiseControl.CCThrottleOrDynBrakePercent = throttle;
                Assert.Equal(100f, locomotive.GetDataOf(control));
            }
        }

        [Fact]
        public void AutomaticLimitFollowsSelectorSpeedAndPowerReduction()
        {
            var locomotive = CreateLocomotive();
            var control = Control();
            Assert.Equal(100f, locomotive.GetDataOf(control));
            locomotive.CruiseControl.SelectedMaxAccelerationPercent = 100;
            Assert.Equal(250f, locomotive.GetDataOf(control));
            locomotive.AbsTractionSpeedMpS = 20;
            Assert.Equal(200f, locomotive.GetDataOf(control));
            locomotive.PowerReduction = 0.25f;
            Assert.Equal(150f, locomotive.GetDataOf(control));
            locomotive.CruiseControl.SelectedMaxAccelerationPercent = 0;
            Assert.Equal(0f, locomotive.GetDataOf(control));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ManualModeKeepsUsingThrottle(bool hasCruiseControl)
        {
            var locomotive = CreateLocomotive();
            if (hasCruiseControl)
                locomotive.CruiseControl.SpeedRegMode = CruiseControl.SpeedRegulatorMode.Manual;
            else
                locomotive.CruiseControl = null;
            var control = Control();
            Assert.Equal(40f, locomotive.GetDataOf(control));
            locomotive.LocalThrottlePercent = 50;
            Assert.Equal(100f, locomotive.GetDataOf(control));
            locomotive.LocalThrottlePercent = 0;
            Assert.Equal(0f, locomotive.GetDataOf(control));
        }

        [Fact]
        public void AutomaticLimitStillRespectsTractionAvailability()
        {
            var locomotive = CreateLocomotive();
            var control = Control();
            locomotive.LocomotivePowerSupply.ThrottleReductionPercent = 75;
            Assert.Equal(50f, locomotive.GetDataOf(control));
            locomotive.LocomotivePowerSupply.ThrottleReductionPercent = 0;
            locomotive.Train.MUDirection = ORTS.Common.Direction.N;
            Assert.Equal(0f, locomotive.GetDataOf(control));
            locomotive.Train.MUDirection = ORTS.Common.Direction.Forward;
            locomotive.LocomotivePowerSupply.MainPowerSupplyState = PowerSupplyState.PowerOff;
            Assert.Equal(0f, locomotive.GetDataOf(control));
        }

        [Theory]
        [InlineData(0f, 0f)]
        [InlineData(25f, 40f)]
        [InlineData(50f, 80f)]
        [InlineData(75f, 160f)]
        [InlineData(100f, 250f)]
        public void AutomaticLimitUsesConfiguredForceStepThrottleTable(float selectedPercent, float expected)
        {
            var locomotive = CreateLocomotive();
            var cruiseControl = locomotive.CruiseControl;
            cruiseControl.SpeedRegulatorMaxForcePercentUnits = false;
            cruiseControl.MaxForceSelectorIsDiscrete = true;
            typeof(CruiseControl).GetField("SpeedRegulatorMaxForceSteps", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(cruiseControl, 4f);
            cruiseControl.ForceStepsThrottleTable = new List<int> { 20, 40, 70, 100 };
            cruiseControl.SelectedMaxAccelerationPercent = selectedPercent;
            Assert.Equal(expected, locomotive.GetDataOf(Control()));
        }
    }
}
