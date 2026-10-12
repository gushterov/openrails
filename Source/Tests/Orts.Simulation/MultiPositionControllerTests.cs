// COPYRIGHT 2026 by the Open Rails project. GNU GPL version 3 or later.
using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Reflection;
using Orts.Simulation;
using Orts.Simulation.Physics;
using Orts.Simulation.RollingStocks;
using Orts.Simulation.RollingStocks.SubSystems;
using Orts.Simulation.RollingStocks.SubSystems.Controllers;
using Orts.Simulation.RollingStocks.SubSystems.PowerSupplies;
using Orts.Simulation.RollingStocks.SubSystems.Brakes.MSTS;
using ORTS.Common;
using Xunit;

namespace Tests.Orts.Simulation
{
    public class MultiPositionControllerTests
    {
        static T Uninitialized<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));

        static (MultiPositionController controller, MSTSLocomotive locomotive) CreateController(
            ControllerPosition position, float brakePercent, bool automatic = false, float? throttleDecreaseTimeSeconds = null)
        {
            // Exercise the MPC and real notch controller without loading a route or rolling stock.
            var locomotive = Uninitialized<MSTSLocomotive>();
            locomotive.Train = Uninitialized<Train>();
            locomotive.Train.TrainType = Train.TRAINTYPE.PLAYER;
            locomotive.Train.MUDirection = Direction.Forward;
            locomotive.RemoteControlGroup = -1;
            var powerSupply = Uninitialized<ScriptedElectricPowerSupply>();
            powerSupply.MaxThrottlePercent = 100;
            powerSupply.PowerSupplyDynamicBrakePercent = -1;
            locomotive.PowerSupply = powerSupply;
            typeof(MSTSLocomotive).GetProperty(nameof(MSTSLocomotive.DynamicBrakeBlendingPercent))
                .SetValue(locomotive, -1f);
            locomotive.LocalDynamicBrakePercent = brakePercent;
            locomotive.ThrottleController = new MSTSNotchController(new List<MSTSNotch>
            {
                new MSTSNotch(0, true, 0),
            });
            locomotive.DynamicBrakeController = new MSTSNotchController(new List<MSTSNotch>
            {
                new MSTSNotch(0, true, 0),
            });
            locomotive.DynamicBrakeController.SetPercent(Math.Max(brakePercent, 0));
            if (automatic || throttleDecreaseTimeSeconds.HasValue)
            {
                locomotive.CruiseControl = Uninitialized<CruiseControl>();
                locomotive.CruiseControl.SpeedRegulatorMaxForcePercentUnits = true;
                locomotive.CruiseControl.SpeedRegMode = automatic ? CruiseControl.SpeedRegulatorMode.Auto : CruiseControl.SpeedRegulatorMode.Manual;
                locomotive.CruiseControl.CruiseControlLogic = CruiseControl.ControllerCruiseControlLogic.Full;
                locomotive.CruiseControl.ThrottleFullRangeDecreaseTimeSeconds = throttleDecreaseTimeSeconds ?? 6;
            }
            var controller = new MultiPositionController(locomotive);
            controller.PositionsList.Add(new MultiPositionController.Position(position.ToString(), "Default", ""));
            controller.Initialize();
            return (controller, locomotive);
        }

        [Theory]
        [InlineData(ControllerPosition.DynamicBrakeIncrease, 1f / 60)]
        [InlineData(ControllerPosition.DynamicBrakeIncrease, 1f / 120)]
        [InlineData(ControllerPosition.DynamicBrakeIncreaseFast, 1f / 120)]
        [InlineData(ControllerPosition.DynamicBrakeIncreaseOrThrottleDecrease, 1f / 120)]
        [InlineData(ControllerPosition.DynamicBrakeIncreaseOrThrottleDecreaseFast, 1f / 120)]
        public void ManualBrakeStartsFromOffAndContinuesIncreasing(ControllerPosition position, float elapsedSeconds)
        {
            var (controller, locomotive) = CreateController(position, -1);
            Assert.Equal(-1f, locomotive.DynamicBrakePercent);
            for (int frame = 0; frame < 20; frame++)
            {
                var previousPercent = locomotive.DynamicBrakePercent;
                controller.Update(elapsedSeconds);
                // Publish controller demand on the next simulation update, as UpdateControllers does.
                var savedValue = locomotive.DynamicBrakeController.SavedValue;
                locomotive.LocalDynamicBrakePercent = savedValue > 0 ? savedValue * 100 : -1;
                Assert.True(locomotive.DynamicBrakePercent > Math.Max(previousPercent, 0));
            }
        }

        [Theory]
        [InlineData(ControllerPosition.DynamicBrakeIncrease)]
        [InlineData(ControllerPosition.DynamicBrakeIncreaseFast)]
        public void AutomaticBrakeStartsFromOff(ControllerPosition position)
        {
            var (controller, locomotive) = CreateController(position, -1, true);
            controller.Update(1f / 120);
            Assert.True(locomotive.DynamicBrakeController.CurrentValue > 0);
        }

        [Theory]
        [InlineData(0f)]
        [InlineData(50f)]
        [InlineData(99.9f)]
        [InlineData(100f)]
        public void ManualBrakeIncreasesExistingDemandAndRespectsMaximum(float initialPercent)
        {
            var (controller, locomotive) = CreateController(ControllerPosition.DynamicBrakeIncrease, initialPercent);
            controller.Update(1f / 60);
            Assert.InRange(locomotive.DynamicBrakeController.CurrentValue, initialPercent / 100, 1f);
            if (initialPercent < 100)
                Assert.True(locomotive.DynamicBrakeController.CurrentValue > initialPercent / 100);
        }

        [Theory]
        [InlineData(30, 6f)]
        [InlineData(60, 6f)]
        [InlineData(120, 6f)]
        [InlineData(60, 4f)]
        [InlineData(60, 0f)]
        public void NeutralReducesThrottleOverTimeAndHoldsDynamicBrake(int updatesPerSecond, float decreaseTimeSeconds)
        {
            var (controller, locomotive) = CreateController(ControllerPosition.Neutral, 50, false, decreaseTimeSeconds);
            locomotive.ThrottleController.SetPercent(100);
            locomotive.LocalThrottlePercent = 100;
            for (int frame = 0; frame < updatesPerSecond; frame++)
            {
                controller.Update(1f / updatesPerSecond);
                locomotive.LocalThrottlePercent = locomotive.ThrottleController.SavedValue * 100;
            }
            // Invalid durations use the default six seconds rather than dividing by zero.
            var duration = decreaseTimeSeconds > 0 ? decreaseTimeSeconds : 6;
            Assert.InRange(locomotive.ThrottlePercent, 100 - 100 / duration - 0.01f, 100 - 100 / duration + 0.01f);
            Assert.Equal(50f, locomotive.DynamicBrakePercent);
            Assert.Equal(0.5f, locomotive.DynamicBrakeController.CurrentValue);
        }

        [Theory]
        [InlineData(ControllerPosition.Neutral)]
        [InlineData(ControllerPosition.DynamicBrakeHold)]
        public void NeutralWithoutCruiseControlReachesZeroAfterSixSeconds(ControllerPosition position)
        {
            var (controller, locomotive) = CreateController(position, 50);
            locomotive.ThrottleController.SetPercent(100);
            locomotive.LocalThrottlePercent = 100;
            for (int frame = 0; frame < 721; frame++)
            {
                controller.Update(1f / 120);
                locomotive.LocalThrottlePercent = locomotive.ThrottleController.SavedValue * 100;
                Assert.InRange(locomotive.ThrottlePercent, 0, 100);
                if (frame == 119)
                    Assert.InRange(locomotive.ThrottlePercent, 83.32f, 83.35f);
            }
            Assert.Equal(0f, locomotive.ThrottlePercent);
            Assert.Equal(50f, locomotive.DynamicBrakePercent);
        }

        [Theory]
        [InlineData(30)]
        [InlineData(60)]
        [InlineData(120)]
        public void AutomaticSouhlasRaisesForceLimitFromZero(int updatesPerSecond)
        {
            var (controller, locomotive) = CreateController(ControllerPosition.ThrottleIncrease, -1, true);
            locomotive.CruiseControl.UseThrottleAsForceSelector = true;
            Assert.Equal(0f, locomotive.CruiseControl.SelectedMaxAccelerationPercent);
            for (int frame = 0; frame < updatesPerSecond; frame++)
                controller.Update(1f / updatesPerSecond);
            Assert.InRange(locomotive.CruiseControl.SelectedMaxAccelerationPercent, 14.99f, 15.01f);
            Assert.Equal(CruiseControl.SpeedSelectorMode.Start, locomotive.CruiseControl.SpeedSelMode);
            Assert.Equal(0f, locomotive.ThrottleController.CurrentValue);
        }

        [Theory]
        [InlineData(ControllerPosition.Drive, CruiseControl.SpeedSelectorMode.On, 40f)]
        [InlineData(ControllerPosition.Neutral, CruiseControl.SpeedSelectorMode.Neutral, 40f)]
        [InlineData(ControllerPosition.Neutral, CruiseControl.SpeedSelectorMode.Neutral, 0f)]
        public void AutomaticDriveAndCoastSelectModeWithoutRaisingForce(
            ControllerPosition position, CruiseControl.SpeedSelectorMode expectedMode, float forcePercent)
        {
            var (controller, locomotive) = CreateController(position, -1, true);
            locomotive.CruiseControl.UseThrottleAsForceSelector = true;
            locomotive.CruiseControl.SelectedMaxAccelerationPercent = forcePercent;
            controller.Update(1f / 60);
            float expectedForce = position == ControllerPosition.Neutral ? Math.Max(0, forcePercent - 100f / 6 / 60) : forcePercent;
            Assert.InRange(locomotive.CruiseControl.SelectedMaxAccelerationPercent, expectedForce - 0.001f, expectedForce + 0.001f);
            Assert.Equal(expectedMode, locomotive.CruiseControl.SpeedSelMode);
            Assert.Equal(0f, locomotive.ThrottleController.CurrentValue);
        }

        [Theory]
        [InlineData(99.9f)]
        [InlineData(100f)]
        public void AutomaticSouhlasRespectsMaximumForce(float forcePercent)
        {
            var (controller, locomotive) = CreateController(ControllerPosition.ThrottleIncrease, -1, true);
            locomotive.CruiseControl.UseThrottleAsForceSelector = true;
            locomotive.CruiseControl.SelectedMaxAccelerationPercent = forcePercent;
            controller.Update(1f / 30);
            Assert.Equal(100f, locomotive.CruiseControl.SelectedMaxAccelerationPercent);
        }

        [Theory]
        [InlineData(30)]
        [InlineData(120)]
        public void AutomaticNeutralLowersForceAndHoldsManualBrakeDespiteBrakePriority(int updatesPerSecond)
        {
            var (controller, locomotive) = CreateController(ControllerPosition.Neutral, 100, true, 6);
            locomotive.CruiseControl.UseThrottleAsForceSelector = true;
            locomotive.CruiseControl.SelectedMaxAccelerationPercent = 100;
            locomotive.CruiseControl.DynamicBrakePriority = true;
            for (int frame = 0; frame < updatesPerSecond; frame++) controller.Update(1f / updatesPerSecond);
            Assert.InRange(locomotive.CruiseControl.SelectedMaxAccelerationPercent, 83.32f, 83.35f);
            Assert.Equal(1f, locomotive.DynamicBrakeController.CurrentValue);
            Assert.Equal(CruiseControl.SpeedSelectorMode.Neutral, locomotive.CruiseControl.SpeedSelMode);
        }

        [Theory]
        [InlineData(ControllerPosition.Drive)]
        [InlineData(ControllerPosition.ThrottleIncrease)]
        public void AutomaticDrivingReleasesBrakeBeforePermittingTraction(ControllerPosition position)
        {
            var (controller, locomotive) = CreateController(position, 50, true);
            locomotive.CruiseControl.UseThrottleAsForceSelector = true;
            locomotive.CruiseControl.SelectedMaxAccelerationPercent = 40;
            locomotive.CruiseControl.DynamicBrakePriority = true;
            for (int frame = 0; frame < 60; frame++) controller.Update(1f / 60);
            Assert.InRange(locomotive.DynamicBrakeController.CurrentValue, 0.2499f, 0.2501f);
            Assert.Equal(40f, locomotive.CruiseControl.SelectedMaxAccelerationPercent);
            Assert.Equal(CruiseControl.SpeedSelectorMode.Neutral, locomotive.CruiseControl.SpeedSelMode);
            for (int frame = 0; frame < 61; frame++) controller.Update(1f / 60);
            Assert.Equal(0f, locomotive.DynamicBrakeController.CurrentValue);
            Assert.False(locomotive.CruiseControl.DynamicBrakePriority);
            Assert.Equal(position == ControllerPosition.Drive ? CruiseControl.SpeedSelectorMode.On : CruiseControl.SpeedSelectorMode.Start,
                locomotive.CruiseControl.SpeedSelMode);
        }

        [Theory]
        [InlineData(30)]
        [InlineData(120)]
        public void AutomaticBrakeClearsTractionLimitAndIncreasesOverTime(int updatesPerSecond)
        {
            var (controller, locomotive) = CreateController(ControllerPosition.DynamicBrakeIncrease, -1, true);
            locomotive.CruiseControl.UseThrottleAsForceSelector = true;
            locomotive.CruiseControl.SelectedMaxAccelerationPercent = 100;
            for (int frame = 0; frame < updatesPerSecond; frame++)
            {
                controller.Update(1f / updatesPerSecond);
                locomotive.LocalDynamicBrakePercent = locomotive.DynamicBrakeController.SavedValue * 100;
            }
            Assert.Equal(0f, locomotive.CruiseControl.SelectedMaxAccelerationPercent);
            Assert.InRange(locomotive.DynamicBrakeController.CurrentValue, 0.4999f, 0.5001f);
            Assert.True(locomotive.CruiseControl.DynamicBrakePriority);
        }

        [Theory]
        [InlineData(0f, false)]
        [InlineData(100f, true)]
        public void RegulatorCanStartFromStandstillOnlyWithNonzeroStartupPowerCap(float startupPowerCap, bool shouldApplyPower)
        {
            var (controller, locomotive) = CreateController(ControllerPosition.ThrottleIncrease, -1, true);
            locomotive.Train.Cars = new List<TrainCar> { locomotive };
            locomotive.MaxForceN = 70000;
            locomotive.MaxCurrentA = 1400;
            locomotive.BrakeSystem = Uninitialized<AirSinglePipe>();
            locomotive.BrakeSystem.BrakeLine1PressurePSI = 90;
            locomotive.TrainBrakeController = new ScriptedBrakeController(locomotive) { MaxPressurePSI = 90 };
            locomotive.CruiseControl = new CruiseControl(locomotive)
            {
                SpeedRegMode = CruiseControl.SpeedRegulatorMode.Auto,
                SpeedRegulatorMaxForcePercentUnits = true,
                UseThrottleAsForceSelector = true,
                SelectedSpeedMpS = 10,
                PowerReductionValue = startupPowerCap,
            };
            locomotive.CruiseControl.SpeedRegulatorOptions.Add("startfromzero");
            typeof(CruiseControl).GetField("reducingForce", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(locomotive.CruiseControl, true);
            controller.Update(1);
            locomotive.CruiseControl.CalculateRequiredForce(0.5f, 0);
            var output = locomotive.CruiseControl.CCThrottleOrDynBrakePercent;
            if (shouldApplyPower) Assert.InRange(output, 0.01f, 15f);
            else Assert.Equal(0f, output);
        }

        [Theory]
        [InlineData(ControllerPosition.ThrottleIncrease, true)]
        [InlineData(ControllerPosition.Drive, true)]
        [InlineData(ControllerPosition.Neutral, false)]
        public void SeparateSpeedSelectorNeutralPreservesTractionCommand(ControllerPosition position, bool shouldApplyPower)
        {
            var (controller, locomotive) = CreateController(position, -1, true);
            locomotive.Train.Cars = new List<TrainCar> { locomotive };
            locomotive.MaxForceN = 70000;
            locomotive.MaxCurrentA = 1400;
            locomotive.BrakeSystem = Uninitialized<AirSinglePipe>();
            locomotive.BrakeSystem.BrakeLine1PressurePSI = 90;
            locomotive.TrainBrakeController = new ScriptedBrakeController(locomotive) { MaxPressurePSI = 90 };
            locomotive.CruiseControl = new CruiseControl(locomotive)
            {
                SpeedRegMode = CruiseControl.SpeedRegulatorMode.Auto,
                SpeedRegulatorMaxForcePercentUnits = true,
                UseThrottleAsForceSelector = true,
                SelectedMaxAccelerationPercent = 40,
                SelectedSpeedMpS = 10,
                PowerReductionValue = 100,
            };
            locomotive.CruiseControl.SpeedRegulatorOptions.Add("startfromzero");
            var speedSelector = new MultiPositionController(locomotive) { controllerBinding = ControllerBinding.SelectedSpeed };
            speedSelector.PositionsList.Add(new MultiPositionController.Position("Neutral", "Default", ""));
            speedSelector.Initialize();

            // Follow the locomotive's MPC update order: driving handle, then speed selector.
            for (int frame = 0; frame < 120; frame++)
            {
                controller.Update(1f / 60);
                var expectedMode = locomotive.CruiseControl.SpeedSelMode;
                var expectedForceLimit = locomotive.CruiseControl.SelectedMaxAccelerationPercent;
                speedSelector.Update(1f / 60);
                Assert.Equal(expectedMode, locomotive.CruiseControl.SpeedSelMode);
                Assert.Equal(expectedForceLimit, locomotive.CruiseControl.SelectedMaxAccelerationPercent);
                Assert.Equal(10f, locomotive.CruiseControl.SelectedSpeedMpS);
                locomotive.CruiseControl.CalculateRequiredForce(1f / 60, 0);
            }
            if (shouldApplyPower) Assert.True(locomotive.CruiseControl.CCThrottleOrDynBrakePercent > 0);
            else Assert.Equal(0f, locomotive.CruiseControl.CCThrottleOrDynBrakePercent);
        }

        static (MultiPositionController controller, MSTSLocomotive locomotive) CreateRunningRegulator()
        {
            var (controller, locomotive) = CreateController(ControllerPosition.Neutral, -1, true);
            locomotive.Train.Cars = new List<TrainCar> { locomotive };
            locomotive.Train.LeadLocomotiveIndex = 0;
            locomotive.MultiPositionControllers = new List<MultiPositionController> { controller };
            locomotive.MaxForceN = 70000;
            locomotive.MaxCurrentA = 1400;
            locomotive.BrakeSystem = Uninitialized<AirSinglePipe>();
            locomotive.BrakeSystem.BrakeLine1PressurePSI = 90;
            locomotive.TrainBrakeController = new ScriptedBrakeController(locomotive) { MaxPressurePSI = 90 };
            locomotive.TrainBrakeController.Notches.Add(new MSTSNotch(0, false, (int)ORTS.Scripting.Api.ControllerState.Release));
            typeof(ScriptedBrakeController).GetField("Script", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(locomotive.TrainBrakeController, new MSTSBrakeController());
            locomotive.CruiseControl = new CruiseControl(locomotive)
            {
                SpeedRegMode = CruiseControl.SpeedRegulatorMode.Auto,
                SpeedRegulatorMaxForcePercentUnits = true,
                UseThrottleAsForceSelector = true,
                SelectedMaxAccelerationPercent = 80,
                SelectedSpeedMpS = 10,
                ThrottleFullRangeDecreaseTimeSeconds = 6,
                CCThrottleOrDynBrakePercent = 60,
                PowerReductionValue = 100,
            };
            locomotive.LocalThrottlePercent = 60;
            return (controller, locomotive);
        }

        static void SelectPosition(MultiPositionController controller, ControllerPosition position)
        {
            controller.PositionsList[0] = new MultiPositionController.Position(position.ToString(), "Default", "");
            controller.Initialize();
            controller.controllerPosition = position;
        }

        static void UpdateRegulatorAndMPC(MultiPositionController controller, MSTSLocomotive locomotive, float elapsedSeconds)
        {
            // Publish the preceding regulator output, then update CC and MPC in locomotive order.
            locomotive.LocalThrottlePercent = locomotive.CruiseControl.ThrottlePercent ?? locomotive.LocalThrottlePercent;
            float manualBrake = locomotive.DynamicBrakeController.SavedValue > 0
                ? locomotive.DynamicBrakeController.SavedValue * 100 : -1;
            locomotive.LocalDynamicBrakePercent = Math.Max(manualBrake, locomotive.CruiseControl.DynamicBrakePercent ?? -1);
            locomotive.CruiseControl.Update(elapsedSeconds);
            controller.Update(elapsedSeconds);
        }

        [Theory]
        [InlineData(ControllerPosition.DynamicBrakeIncrease, 30)]
        [InlineData(ControllerPosition.DynamicBrakeIncrease, 120)]
        [InlineData(ControllerPosition.DynamicBrakeIncreaseFast, 30)]
        [InlineData(ControllerPosition.DynamicBrakeIncreaseFast, 120)]
        public void AutomaticBEHoldsResidualTractionUntilItIsReducedInNeutral(ControllerPosition brakePosition, int updatesPerSecond)
        {
            var (controller, locomotive) = CreateRunningRegulator();
            float elapsedSeconds = 1f / updatesPerSecond;
            for (int frame = 0; frame < updatesPerSecond; frame++)
                UpdateRegulatorAndMPC(controller, locomotive, elapsedSeconds);
            Assert.InRange(locomotive.CruiseControl.CCThrottleOrDynBrakePercent, 43.32f, 43.35f);
            float remainingThrottle = locomotive.CruiseControl.CCThrottleOrDynBrakePercent;
            float remainingForceLimit = locomotive.CruiseControl.SelectedMaxAccelerationPercent;

            SelectPosition(controller, brakePosition);
            for (int frame = 0; frame < updatesPerSecond * 2; frame++)
            {
                UpdateRegulatorAndMPC(controller, locomotive, elapsedSeconds);
                Assert.Equal(remainingThrottle, locomotive.CruiseControl.CCThrottleOrDynBrakePercent);
                Assert.Equal(remainingForceLimit, locomotive.CruiseControl.SelectedMaxAccelerationPercent);
                Assert.Equal(0f, locomotive.DynamicBrakeController.CurrentValue);
                Assert.False(locomotive.CruiseControl.DynamicBrakePriority);
            }

            SelectPosition(controller, ControllerPosition.Neutral);
            for (int frame = 0; frame < updatesPerSecond * 3; frame++)
                UpdateRegulatorAndMPC(controller, locomotive, elapsedSeconds);
            Assert.Equal(0f, locomotive.CruiseControl.CCThrottleOrDynBrakePercent);
            SelectPosition(controller, brakePosition);
            for (int frame = 0; frame < 3; frame++)
                UpdateRegulatorAndMPC(controller, locomotive, elapsedSeconds);
            Assert.True(locomotive.DynamicBrakeController.CurrentValue > 0);
            Assert.Equal(0f, locomotive.CruiseControl.SelectedMaxAccelerationPercent);
            Assert.True(locomotive.CruiseControl.DynamicBrakePriority);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void AutomaticBEStillAllowsBrakePipeAndTCSToCutTraction(bool tcsEmergency)
        {
            var (controller, locomotive) = CreateRunningRegulator();
            SelectPosition(controller, ControllerPosition.DynamicBrakeIncrease);
            if (tcsEmergency)
                typeof(ScriptedBrakeController).GetField("tcsEmergencyBraking", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(locomotive.TrainBrakeController, true);
            else locomotive.BrakeSystem.BrakeLine1PressurePSI = 80;
            UpdateRegulatorAndMPC(controller, locomotive, 1f / 60);
            UpdateRegulatorAndMPC(controller, locomotive, 1f / 60);
            Assert.Equal(0f, locomotive.CruiseControl.CCThrottleOrDynBrakePercent);
        }

        [Fact]
        public void AutomaticBEHoldDoesNotApplyToOtherCruiseControllers()
        {
            var (controller, locomotive) = CreateRunningRegulator();
            locomotive.CruiseControl.UseThrottleAsForceSelector = false;
            SelectPosition(controller, ControllerPosition.DynamicBrakeIncrease);
            UpdateRegulatorAndMPC(controller, locomotive, 1f / 60);
            UpdateRegulatorAndMPC(controller, locomotive, 1f / 60);
            Assert.InRange(locomotive.CruiseControl.CCThrottleOrDynBrakePercent, 59.4f, 59.5f);
        }
    }
}
