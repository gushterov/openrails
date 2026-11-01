// COPYRIGHT 2026 by the Open Rails project. GNU GPL version 3 or later.
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using Orts.Common;
using Orts.Simulation;
using Orts.Simulation.Physics;
using Orts.Simulation.RollingStocks;
using Orts.Simulation.RollingStocks.SubSystems;
using Orts.Simulation.RollingStocks.SubSystems.Controllers;
using Orts.Simulation.RollingStocks.SubSystems.PowerSupplies;
using ORTS.Common;
using ORTS.Scripting.Api;
using Xunit;

namespace Tests.Orts.Simulation
{
    public class MPCEmergencyBrakeTests
    {
        static T Uninitialized<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));

        class TestLocomotive : MSTSLocomotive
        {
            public TestLocomotive() : base(null, null) { }
            public override void SignalEvent(Event evt) { }
        }

        static (MultiPositionController controller, MSTSLocomotive locomotive, MSTSBrakeController script) CreateController(
            bool automatic = false, bool speedOnly = false, bool priority = false, bool forceSelector = false,
            string emergencyFlag = "Stable")
        {
            var locomotive = Uninitialized<TestLocomotive>();
            var simulator = Uninitialized<Simulator>();
            typeof(TrainCar).GetField(nameof(TrainCar.Simulator)).SetValue(locomotive, simulator);
            locomotive.Train = Uninitialized<Train>();
            locomotive.Train.TrainType = Train.TRAINTYPE.PLAYER;
            locomotive.Train.MUDirection = Direction.Forward;
            locomotive.RemoteControlGroup = -1;
            locomotive.AbsSpeedMpS = 15;
            locomotive.MainResPressurePSI = 100;
            var powerSupply = Uninitialized<ScriptedElectricPowerSupply>();
            powerSupply.MaxThrottlePercent = 100;
            powerSupply.PowerSupplyDynamicBrakePercent = -1;
            locomotive.PowerSupply = powerSupply;
            typeof(MSTSLocomotive).GetProperty(nameof(MSTSLocomotive.DynamicBrakeBlendingPercent)).SetValue(locomotive, -1f);
            locomotive.LocalDynamicBrakePercent = -1;
            locomotive.ThrottleController = new MSTSNotchController(new List<MSTSNotch> { new MSTSNotch(0, true, 0) });
            locomotive.DynamicBrakeController = new MSTSNotchController(new List<MSTSNotch> { new MSTSNotch(0, true, 0) });
            if (automatic)
            {
                locomotive.CruiseControl = Uninitialized<CruiseControl>();
                locomotive.CruiseControl.SpeedRegulatorMaxForcePercentUnits = true;
                locomotive.CruiseControl.SpeedRegMode = CruiseControl.SpeedRegulatorMode.Auto;
                locomotive.CruiseControl.CruiseControlLogic = speedOnly
                    ? CruiseControl.ControllerCruiseControlLogic.SpeedOnly : CruiseControl.ControllerCruiseControlLogic.Full;
                locomotive.CruiseControl.DynamicBrakePriority = priority;
                locomotive.CruiseControl.UseThrottleAsForceSelector = forceSelector;
            }
            locomotive.TrainBrakeController = new ScriptedBrakeController(locomotive);
            locomotive.TrainBrakeController.Notches.Add(new MSTSNotch(0, false, (int)ControllerState.Release));
            var script = new MSTSBrakeController();
            typeof(BrakeController).GetMethod("AttachToHost", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(script, new object[] { locomotive.TrainBrakeController });
            typeof(ScriptedBrakeController).GetField("Script", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(locomotive.TrainBrakeController, script);
            script.Initialize();
            var controller = new MultiPositionController(locomotive) { controllerBinding = ControllerBinding.Throttle };
            controller.PositionsList.Add(new MultiPositionController.Position("Neutral", "Default", ""));
            controller.PositionsList.Add(new MultiPositionController.Position("EmergencyBrake", emergencyFlag, ""));
            controller.Initialize();
            locomotive.MultiPositionControllers = new List<MultiPositionController> { controller };
            return (controller, locomotive, script);
        }

        [Theory]
        [InlineData(false, false, false, false)]
        [InlineData(true, false, false, false)]
        [InlineData(true, true, false, false)]
        [InlineData(true, false, true, false)]
        [InlineData(true, false, true, true)]
        public void EmergencyFollowsHandleWhileMovingWithoutLatchingPushButton(bool automatic, bool speedOnly, bool priority, bool forceSelector)
        {
            var (controller, locomotive, script) = CreateController(automatic, speedOnly, priority, forceSelector);
            controller.DoMovement(MultiPositionController.Movement.Aft);
            controller.Update(0.1f);
            controller.Update(0.1f);

            Assert.True(locomotive.TrainBrakeController.EmergencyBraking);
            Assert.False(locomotive.TrainBrakeController.EmergencyBrakingPushButton);
            Assert.False(locomotive.EmergencyButtonPressed);
            Assert.Equal(ControllerState.Emergency, script.GetState());
            Assert.True(script.EmergencyBrakingPushButton()); // Existing custom scripts still receive the request.
            Assert.Equal(1f, locomotive.DynamicBrakeController.CurrentValue);
            float pressure = 5, epState = -2;
            script.UpdatePressure(ref pressure, 0.1f, ref epState);
            Assert.True(pressure < 5);
            Assert.Equal(1f, epState);

            // Release before either the train or the dynamic brake has reached zero.
            locomotive.LocalDynamicBrakePercent = 100;
            controller.DoMovement(MultiPositionController.Movement.Forward);
            controller.Update(0.1f);

            Assert.Equal(15f, locomotive.AbsSpeedMpS);
            Assert.Equal(100f, locomotive.DynamicBrakePercent);
            Assert.False(locomotive.TrainBrakeController.EmergencyBraking);
            Assert.False(script.EmergencyBrakingPushButton());
            Assert.Equal(ControllerState.Release, script.GetState());
            var releasedPressure = pressure;
            script.UpdatePressure(ref pressure, 0.1f, ref epState);
            Assert.True(pressure > releasedPressure);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void LeavingEmergencyDoesNotClearIndependentEmergency(bool pushButton)
        {
            var (controller, locomotive, script) = CreateController();
            var brake = locomotive.TrainBrakeController;
            controller.DoMovement(MultiPositionController.Movement.Aft);
            controller.Update(0.1f);
            if (pushButton)
            {
                locomotive.EmergencyButtonPressed = true;
                brake.EmergencyBrakingPushButton = true;
            }
            else
                brake.TCSEmergencyBraking = true;

            controller.DoMovement(MultiPositionController.Movement.Forward);
            controller.Update(0.1f);

            Assert.True(brake.EmergencyBraking);
            Assert.Equal(pushButton, brake.EmergencyBrakingPushButton);
            Assert.Equal(pushButton, locomotive.EmergencyButtonPressed);
            Assert.Equal(!pushButton, brake.TCSEmergencyBraking);
            Assert.Equal(pushButton ? ControllerState.EBPB : ControllerState.TCSEmergency, script.GetState());
        }

        [Fact]
        public void EmergencyRemainsAppliedAtStandstillUntilHandleIsMoved()
        {
            var (controller, locomotive, script) = CreateController();
            controller.DoMovement(MultiPositionController.Movement.Aft);
            controller.Update(0.1f);
            locomotive.AbsSpeedMpS = 0;
            controller.Update(0.1f);
            Assert.True(locomotive.TrainBrakeController.EmergencyBraking);

            controller.DoMovement(MultiPositionController.Movement.Forward);
            controller.Update(0.1f);
            Assert.False(locomotive.TrainBrakeController.EmergencyBraking);
        }

        [Fact]
        public void ReleasingOneHandleDoesNotClearAnotherHandlesEmergency()
        {
            var (controller, locomotive, script) = CreateController();
            var other = new MultiPositionController(locomotive);
            other.PositionsList.AddRange(controller.PositionsList);
            other.Initialize();
            locomotive.MultiPositionControllers.Add(other);
            controller.DoMovement(MultiPositionController.Movement.Aft);
            other.DoMovement(MultiPositionController.Movement.Aft);
            controller.Update(0.1f);
            other.Update(0.1f);

            controller.DoMovement(MultiPositionController.Movement.Forward);
            controller.Update(0.1f);
            Assert.True(locomotive.TrainBrakeController.EmergencyBraking);

            other.DoMovement(MultiPositionController.Movement.Forward);
            other.Update(0.1f);
            Assert.False(locomotive.TrainBrakeController.EmergencyBraking);
        }

        [Fact]
        public void SpringReturnCanCancelEmergencyWhileMoving()
        {
            var (controller, locomotive, script) = CreateController(emergencyFlag: "SpringLoadedForwards");
            controller.DoMovement(MultiPositionController.Movement.Aft);
            controller.Update(0.1f);
            Assert.True(locomotive.TrainBrakeController.EmergencyBraking);

            controller.DoMovement(MultiPositionController.Movement.Neutral);
            controller.Update(0.3f);
            Assert.Equal(ControllerPosition.Neutral, controller.SelectedPosition);
            Assert.False(locomotive.TrainBrakeController.EmergencyBraking);
        }

        [Fact]
        public void RestoredEmergencyCanBeReleasedWithoutPushButtonReset()
        {
            var (controller, locomotive, script) = CreateController();
            controller.DoMovement(MultiPositionController.Movement.Aft);
            controller.Update(0.1f);
            using (var stream = new MemoryStream())
            {
                controller.Save(new BinaryWriter(stream));
                stream.Position = 0;
                var restored = new MultiPositionController(locomotive);
                restored.PositionsList = controller.PositionsList;
                restored.Restore(new BinaryReader(stream));
                locomotive.MultiPositionControllers[0] = restored;
                Assert.True(locomotive.TrainBrakeController.EmergencyBraking);
                Assert.False(locomotive.TrainBrakeController.EmergencyBrakingPushButton);

                restored.DoMovement(MultiPositionController.Movement.Forward);
                restored.Update(0.1f);
                Assert.False(locomotive.TrainBrakeController.EmergencyBraking);
            }
        }
    }
}
