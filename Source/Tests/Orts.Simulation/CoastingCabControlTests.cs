// COPYRIGHT 2026 by the Open Rails project. GNU GPL version 3 or later.
using System;
using System.Runtime.Serialization;
using Orts.Formats.Msts;
using Orts.Simulation.Physics;
using Orts.Simulation.RollingStocks;
using Orts.Simulation.RollingStocks.SubSystems;
using Orts.Simulation.RollingStocks.SubSystems.PowerSupplies;
using ORTS.Common;
using Xunit;

namespace Tests.Orts.Simulation
{
    public class CoastingCabControlTests
    {
        static T Uninitialized<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));

        public static MSTSLocomotive CreateLocomotive(float throttle, float brake, float maxAccel, bool cruiseControl, bool automatic)
        {
            var locomotive = Uninitialized<MSTSLocomotive>();
            locomotive.Train = Uninitialized<Train>();
            locomotive.Train.TrainType = Train.TRAINTYPE.PLAYER;
            locomotive.Train.MUDirection = Direction.Forward;
            locomotive.RemoteControlGroup = -1;
            var powerSupply = Uninitialized<ScriptedElectricPowerSupply>();
            powerSupply.MaxThrottlePercent = 100;
            powerSupply.PowerSupplyDynamicBrakePercent = -1;
            locomotive.PowerSupply = powerSupply;
            typeof(MSTSLocomotive).GetProperty(nameof(MSTSLocomotive.DynamicBrakeBlendingPercent)).SetValue(locomotive, -1f);
            locomotive.LocalThrottlePercent = throttle;
            locomotive.LocalDynamicBrakePercent = brake;
            if (cruiseControl)
            {
                locomotive.CruiseControl = Uninitialized<CruiseControl>();
                locomotive.CruiseControl.SpeedRegulatorMaxForcePercentUnits = true;
                locomotive.CruiseControl.SelectedMaxAccelerationPercent = maxAccel;
                locomotive.CruiseControl.SpeedRegMode = automatic ? CruiseControl.SpeedRegulatorMode.Auto : CruiseControl.SpeedRegulatorMode.Manual;
            }
            return locomotive;
        }

        [Theory]
        [InlineData(0f, -1f, 0f, false, false, 1f)]
        [InlineData(0f, 0f, 0f, false, false, 1f)]
        [InlineData(0f, -1f, 0f, true, false, 1f)]
        [InlineData(0f, 0f, 0f, true, true, 1f)]
        [InlineData(0.01f, -1f, 0f, true, false, 0f)]
        [InlineData(0f, 0.01f, 0f, true, true, 0f)]
        [InlineData(0f, -1f, 0.01f, true, true, 0f)]
        [InlineData(0f, -1f, 50f, true, false, 0f)]
        [InlineData(25f, 30f, 50f, true, true, 0f)]
        public void IndicatorRequiresAllThreeDemandsToBeZero(float throttle, float brake, float maxAccel,
            bool cruiseControl, bool automatic, float expected)
        {
            var locomotive = CreateLocomotive(throttle, brake, maxAccel, cruiseControl, automatic);
            var control = new CabViewControl { ControlType = new CabViewControlType(CABViewControlTypes.ORTS_COASTING) };
            Assert.Equal(expected, locomotive.GetDataOf(control));
        }

        [Fact]
        public void PositiveDemandTurnsIndicatorOffAndClearingItTurnsItBackOn()
        {
            var locomotive = CreateLocomotive(0, -1, 0, true, true);
            var control = new CabViewControl { ControlType = new CabViewControlType(CABViewControlTypes.ORTS_COASTING) };
            Assert.Equal(1f, locomotive.GetDataOf(control));
            locomotive.CruiseControl.SelectedMaxAccelerationPercent = 50;
            Assert.Equal(0f, locomotive.GetDataOf(control));
            locomotive.CruiseControl.SelectedMaxAccelerationPercent = 0;
            Assert.Equal(1f, locomotive.GetDataOf(control));
        }
    }
}
