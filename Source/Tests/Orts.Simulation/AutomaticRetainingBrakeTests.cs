// COPYRIGHT 2026 by the Open Rails project. GNU GPL version 3 or later.
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using Orts.Common;
using Orts.Parsers.Msts;
using Orts.Simulation.Physics;
using Orts.Simulation.RollingStocks;
using Orts.Simulation.RollingStocks.SubSystems;
using Orts.Simulation.RollingStocks.SubSystems.Brakes;
using Orts.Simulation.RollingStocks.SubSystems.Brakes.MSTS;
using Orts.Simulation.RollingStocks.SubSystems.Controllers;
using ORTS.Common;
using ORTS.Scripting.Api;
using Xunit;

namespace Tests.Orts.Simulation
{
    public class AutomaticRetainingBrakeTests
    {
        static AutomaticRetainingBrake CreateBrake() => new AutomaticRetainingBrake { Enabled = true };

        static void Parse(MSTSLocomotive locomotive, string parameter, string value)
        {
            using var stream = new MemoryStream(Encoding.ASCII.GetBytes("( " + value + " )"));
            using var reader = new STFReader(stream, "test.eng", Encoding.ASCII, false);
            locomotive.Parse("engine(" + parameter.ToLowerInvariant(), reader);
        }

        [Fact]
        public void DefaultsAndEngineParameters()
        {
            var locomotive = ParkingBrakeCabControlTests.CreateLocomotive();
            locomotive.AutomaticRetainingBrake = new AutomaticRetainingBrake();
            var brake = locomotive.AutomaticRetainingBrake;
            Assert.False(brake.Enabled);
            Assert.Equal(MpS.FromKpH(2), brake.ApplySpeedMpS);
            Assert.Equal(MpS.FromKpH(3), brake.DepartureSpeedMpS);
            Assert.Equal(8f, brake.DepartureTimeoutS);
            Assert.Equal(Bar.ToPSI(2), brake.PressurePSI);

            Parse(locomotive, "ORTSAutomaticRetainingBrake", "1");
            Parse(locomotive, "ORTSAutomaticRetainingBrakeApplySpeed", "1.5km/h");
            Parse(locomotive, "ORTSAutomaticRetainingBrakeDepartureSpeed", "4km/h");
            Parse(locomotive, "ORTSAutomaticRetainingBrakeDepartureTimeout", "10s");
            Parse(locomotive, "ORTSAutomaticRetainingBrakePressure", "250kpa");
            Assert.True(brake.Enabled);
            Assert.Equal(MpS.FromKpH(1.5f), brake.ApplySpeedMpS, 5);
            Assert.Equal(MpS.FromKpH(4), brake.DepartureSpeedMpS, 5);
            Assert.Equal(10f, brake.DepartureTimeoutS);
            Assert.Equal(Bar.ToPSI(2.5f), brake.PressurePSI, 4);
            Parse(locomotive, "ORTSAutomaticRetainingBrakePressure", "2");
            Assert.Equal(Bar.ToPSI(2), brake.PressurePSI, 4);
            Parse(locomotive, "ORTSAutomaticRetainingBrakePressure", "-1bar");
            Parse(locomotive, "ORTSAutomaticRetainingBrakeDepartureTimeout", "-1s");
            Assert.Equal(0, brake.PressurePSI);
            Assert.Equal(0, brake.DepartureTimeoutS);
        }

        [Theory]
        [InlineData(0f, true)]
        [InlineData(1.99f, true)]
        [InlineData(-1.99f, true)]
        [InlineData(2f, false)]
        [InlineData(-2f, false)]
        [InlineData(3f, false)]
        [InlineData(-30f, false)]
        public void ApplicationIsStrictlyBelowSpeedInEitherDirection(float speedKpH, bool expected)
        {
            var brake = CreateBrake();
            brake.Update(1, MpS.FromKpH(speedKpH), ControllerPosition.Neutral);
            Assert.Equal(expected, brake.Applied);
        }

        [Theory]
        [InlineData(ControllerPosition.ThrottleIncrease)]
        [InlineData(ControllerPosition.ThrottleIncreaseFast)]
        public void EnableReleasesAndDoesNotStartTimer(ControllerPosition enable)
        {
            var brake = CreateBrake();
            brake.Update(1, 0, ControllerPosition.Neutral);
            Assert.True(brake.Applied);
            brake.Update(1, 0, enable);
            brake.Update(20, 0, enable);
            Assert.False(brake.Applied);
            brake.Update(7.99f, 0, ControllerPosition.Drive);
            Assert.False(brake.Applied);
            brake.Update(0.02f, 0, ControllerPosition.Drive);
            Assert.True(brake.Applied);
            brake.Update(100, 0, ControllerPosition.Drive);
            Assert.True(brake.Applied);
            brake.Update(0.1f, 0, enable);
            Assert.False(brake.Applied);
        }

        [Fact]
        public void RepeatedEnableAndDriveDoNotExtendAnActiveAttempt()
        {
            var brake = CreateBrake();
            brake.Update(0, 0, ControllerPosition.ThrottleIncrease);
            brake.Update(4, 0, ControllerPosition.Drive);
            brake.Update(1, 0, ControllerPosition.ThrottleIncrease);
            brake.Update(3, 0, ControllerPosition.Drive);
            Assert.True(brake.Applied);
        }

        [Theory]
        [InlineData(3f)]
        [InlineData(-3f)]
        public void ReachingDepartureSpeedCancelsTimerAndNextStopApplies(float speedKpH)
        {
            var brake = CreateBrake();
            brake.Update(0, 0, ControllerPosition.ThrottleIncrease);
            brake.Update(7, MpS.FromKpH(speedKpH), ControllerPosition.Drive);
            brake.Update(20, MpS.FromKpH(2.5f), ControllerPosition.Drive);
            Assert.False(brake.Applied);
            brake.Update(0.1f, MpS.FromKpH(1.9f), ControllerPosition.Drive);
            Assert.True(brake.Applied);
        }

        [Theory]
        [InlineData(ControllerPosition.Neutral)]
        [InlineData(ControllerPosition.Drive)]
        public void CancellationAboveApplySpeedWaitsUntilBelowSpeed(ControllerPosition position)
        {
            var brake = CreateBrake();
            brake.Update(0, 0, ControllerPosition.ThrottleIncrease);
            brake.Update(8, MpS.FromKpH(2.5f), position);
            Assert.False(brake.Applied);
            brake.Update(1, MpS.FromKpH(2), position);
            Assert.False(brake.Applied);
            brake.Update(0.1f, MpS.FromKpH(1.9f), position);
            Assert.True(brake.Applied);
        }

        [Fact]
        public void NeutralCancelsDepartureImmediatelyAtRest()
        {
            var brake = CreateBrake();
            brake.Update(0, 0, ControllerPosition.ThrottleIncrease);
            brake.Update(1, 0, ControllerPosition.Drive);
            brake.Update(0.1f, 0, ControllerPosition.Neutral);
            Assert.True(brake.Applied);
        }

        static MSTSLocomotive CreateLocomotive(ControllerState brakeState = ControllerState.Lap)
        {
            var locomotive = ParkingBrakeCabControlTests.CreateLocomotive();
            locomotive.AutomaticRetainingBrake = CreateBrake();
            locomotive.Doors = new Doors(locomotive);
            locomotive.EngineBrakeApplyRatePSIpS = 10;
            locomotive.EngineBrakeReleaseRatePSIpS = 20;
            locomotive.MultiPositionControllers = new List<MultiPositionController>
            {
                new MultiPositionController(locomotive) { controllerPosition = ControllerPosition.Neutral },
            };
            SelectPosition(locomotive, ControllerPosition.Neutral);
            var controller = new ScriptedBrakeController(locomotive)
            {
                MaximumValue = 1,
                Notches = new List<MSTSNotch> { new MSTSNotch(0, false, (int)brakeState) },
            };
            var script = new MSTSBrakeController();
            typeof(BrakeController).GetMethod("AttachToHost", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(script, new object[] { controller });
            typeof(ScriptedBrakeController).GetField("Script", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(controller, script);
            script.Initialize();
            locomotive.EngineBrakeController = controller;
            return locomotive;
        }

        static void SelectPosition(MSTSLocomotive locomotive, ControllerPosition position)
        {
            var mpc = locomotive.MultiPositionControllers[0];
            mpc.PositionsList.Clear();
            mpc.PositionsList.Add(new MultiPositionController.Position(position.ToString(), "Default", ""));
            mpc.Initialize();
        }

        [Fact]
        public void AutomaticPressureReleasesFromLapAndDoesNotMoveManualHandle()
        {
            var locomotive = CreateLocomotive();
            float pressure = 0;
            locomotive.UpdateEngineBrakePressure(ref pressure, 1);
            Assert.Equal(10, pressure, 4);
            locomotive.UpdateEngineBrakePressure(ref pressure, 10);
            Assert.Equal(Bar.ToPSI(2), pressure, 4);
            // The selected lever position is available before the MPC's next update.
            SelectPosition(locomotive, ControllerPosition.ThrottleIncrease);
            Assert.Equal(ControllerPosition.Neutral, locomotive.MultiPositionControllers[0].controllerPosition);
            locomotive.UpdateEngineBrakePressure(ref pressure, 1);
            Assert.Equal(Bar.ToPSI(2) - 20, pressure, 4);
            locomotive.UpdateEngineBrakePressure(ref pressure, 1);
            Assert.Equal(0, pressure, 4);
            Assert.Equal(0, locomotive.EngineBrakeController.CurrentValue);
        }

        [Theory]
        [InlineData(DoorState.Opening, DoorSide.Left)]
        [InlineData(DoorState.Opening, DoorSide.Right)]
        [InlineData(DoorState.Open, DoorSide.Left)]
        [InlineData(DoorState.Open, DoorSide.Right)]
        [InlineData(DoorState.Closing, DoorSide.Left)]
        [InlineData(DoorState.Closing, DoorSide.Right)]
        public void AnyUnclosedDoorBlocksReleaseUntilClosedAndEnableSelectedAgain(DoorState state, DoorSide side)
        {
            var locomotive = CreateLocomotive();
            var trailing = (QuietLocomotive)FormatterServices.GetUninitializedObject(typeof(QuietLocomotive));
            trailing.Doors = new Doors(trailing);
            locomotive.Train.Cars.Add(trailing);
            var door = side == DoorSide.Left ? trailing.Doors.LeftDoor : trailing.Doors.RightDoor;
            typeof(Door).GetProperty(nameof(Door.State)).SetValue(door, state);

            float pressure = 0;
            locomotive.UpdateEngineBrakePressure(ref pressure, 10);
            SelectPosition(locomotive, ControllerPosition.ThrottleIncrease);
            locomotive.UpdateEngineBrakePressure(ref pressure, 10);
            Assert.True(locomotive.AutomaticRetainingBrake.Applied);
            Assert.Equal(Bar.ToPSI(2), pressure, 4);

            typeof(Door).GetProperty(nameof(Door.State)).SetValue(door, DoorState.Closed);
            locomotive.UpdateEngineBrakePressure(ref pressure, 10);
            Assert.True(locomotive.AutomaticRetainingBrake.Applied);
            Assert.Equal(Bar.ToPSI(2), pressure, 4);
            SelectPosition(locomotive, ControllerPosition.Drive);
            locomotive.UpdateEngineBrakePressure(ref pressure, 1);
            Assert.True(locomotive.AutomaticRetainingBrake.Applied);
            SelectPosition(locomotive, ControllerPosition.ThrottleIncrease);
            locomotive.UpdateEngineBrakePressure(ref pressure, 10);
            Assert.False(locomotive.AutomaticRetainingBrake.Applied);
            Assert.Equal(0, pressure, 4);
        }

        [Theory]
        [InlineData(0f, true)]
        [InlineData(1.99f, true)]
        [InlineData(2f, false)]
        [InlineData(2.5f, false)]
        [InlineData(-3f, false)]
        public void OpeningDoorsCancelsDepartureButRespectsApplySpeed(float speedKpH, bool expected)
        {
            var brake = CreateBrake();
            brake.Update(0, 0, ControllerPosition.ThrottleIncrease);
            brake.Update(1, 0, ControllerPosition.Drive);
            Assert.False(brake.Applied);
            brake.Update(1, MpS.FromKpH(speedKpH), ControllerPosition.Drive, false);
            Assert.Equal(expected, brake.Applied);
            brake.Update(1, MpS.FromKpH(1), ControllerPosition.Drive, false);
            Assert.True(brake.Applied);
        }

        [Theory]
        [InlineData(10f)]
        [InlineData(40f)]
        public void AutomaticReleaseRetainsOriginalManualPressure(float manualPressurePSI)
        {
            var locomotive = CreateLocomotive();
            float pressure = manualPressurePSI;
            locomotive.UpdateEngineBrakePressure(ref pressure, 10);
            Assert.Equal(System.Math.Max(manualPressurePSI, Bar.ToPSI(2)), pressure, 4);
            SelectPosition(locomotive, ControllerPosition.ThrottleIncrease);
            locomotive.UpdateEngineBrakePressure(ref pressure, 10);
            Assert.Equal(manualPressurePSI, pressure, 4);
        }

        [Fact]
        public void ManualReleaseCannotDefeatAutomaticApplicationAndMaximumIsRespected()
        {
            var locomotive = CreateLocomotive(ControllerState.Release);
            locomotive.AutomaticRetainingBrake.PressurePSI = 200;
            float pressure = 40;
            locomotive.UpdateEngineBrakePressure(ref pressure, 100);
            Assert.Equal(locomotive.EngineBrakeController.MaxPressurePSI
                - locomotive.EngineBrakeController.FullServReductionPSI, pressure, 4);
            locomotive.UpdateEngineBrakePressure(ref pressure, 100);
            Assert.True(pressure > 0);
        }

        [Fact]
        public void DisabledOptionAndMissingThrottleControllerPreserveNormalBraking()
        {
            var locomotive = CreateLocomotive();
            locomotive.AutomaticRetainingBrake.Enabled = false;
            float pressure = 12;
            locomotive.UpdateEngineBrakePressure(ref pressure, 10);
            Assert.Equal(12, pressure, 4);
            locomotive.AutomaticRetainingBrake.Enabled = true;
            locomotive.MultiPositionControllers[0].controllerBinding = ControllerBinding.SelectedSpeed;
            locomotive.UpdateEngineBrakePressure(ref pressure, 10);
            Assert.Equal(12, pressure, 4);
            locomotive.MultiPositionControllers[0].controllerBinding = ControllerBinding.Throttle;
            locomotive.Train.TrainType = Train.TRAINTYPE.AI;
            locomotive.UpdateEngineBrakePressure(ref pressure, 10);
            Assert.Equal(12, pressure, 4);
        }

        [Fact]
        public void SaveRestoreKeepsElapsedDepartureTimeAndSeparatePressures()
        {
            var locomotive = CreateLocomotive();
            var brake = locomotive.AutomaticRetainingBrake;
            float pressure = 10;
            locomotive.UpdateEngineBrakePressure(ref pressure, 10);
            SelectPosition(locomotive, ControllerPosition.ThrottleIncrease);
            locomotive.UpdateEngineBrakePressure(ref pressure, 0.1f);
            SelectPosition(locomotive, ControllerPosition.Drive);
            locomotive.UpdateEngineBrakePressure(ref pressure, 0.4f);

            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
                brake.Save(writer);
            var restored = new AutomaticRetainingBrake(brake);
            stream.Position = 0;
            using (var reader = new BinaryReader(stream, Encoding.UTF8, true))
                restored.Restore(reader);
            float restoredPressure = pressure;
            restored.Update(0.1f, 0, ControllerPosition.Drive);
            restored.UpdateEngineBrakePressure(ref restoredPressure, locomotive.EngineBrakeController, 0.1f, 10, 20);
            locomotive.UpdateEngineBrakePressure(ref pressure, 0.1f);
            Assert.Equal(pressure, restoredPressure, 4);
            restored.Update(7.49f, 0, ControllerPosition.Drive);
            Assert.False(restored.Applied);
            restored.Update(0.02f, 0, ControllerPosition.Drive);
            Assert.True(restored.Applied);
        }

        [Fact]
        public void CabChangeDoesNotTurnAutomaticPressureIntoManualPressure()
        {
            var oldCab = CreateLocomotive();
            float pressure = 0;
            oldCab.UpdateEngineBrakePressure(ref pressure, 10);
            var newCab = CreateLocomotive();
            newCab.AutomaticRetainingBrake.CopyPressureStateFrom(oldCab.AutomaticRetainingBrake);
            newCab.UpdateEngineBrakePressure(ref pressure, 1);
            SelectPosition(newCab, ControllerPosition.ThrottleIncrease);
            newCab.UpdateEngineBrakePressure(ref pressure, 10);
            Assert.Equal(0, pressure, 4);
        }

        class QuietLocomotive : MSTSLocomotive
        {
            public QuietLocomotive() : base(null, null) { }
            public override void SignalEvent(Event evt) { }
        }

        class TestAirBrake : AirSinglePipe
        {
            public TestAirBrake() : base(null) { }
            public static void Propagate(TrainCar lead) => PropagateBrakeLinePressures(0.1f, lead, false);
        }

        [Fact]
        public void AutomaticDemandPropagatesToFourConnectedEngines()
        {
            var lead = CreateLocomotive();
            var train = lead.Train;
            train.LeadLocomotiveIndex = 0;
            train.LocoGroups = new List<List<TrainCar>> { new List<TrainCar> { lead } };
            train.DPLeadUnits = new List<TrainCar> { lead };
            lead.BrakeSystem.BrakePipeVolumeM3 = 0.1f;
            lead.MainResVolumeM3 = 1;
            lead.MainResPressurePSI = 100;
            lead.BrakePipeChargingRatePSIorInHgpS = 1000;
            lead.AFMFilter = new IIRFilter(IIRFilter.FilterTypes.Butterworth, 1, IIRFilter.HzToRad(0.1f), 1);
            for (int i = 1; i < 4; i++)
            {
                var trailing = (QuietLocomotive)FormatterServices.GetUninitializedObject(typeof(QuietLocomotive));
                trailing.Train = train;
                trailing.Doors = new Doors(trailing);
                trailing.BrakeSystem = (AirSinglePipe)FormatterServices.GetUninitializedObject(typeof(AirSinglePipe));
                trailing.BrakeSystem.BrakePipeVolumeM3 = 0.1f;
                train.Cars.Add(trailing);
                train.LocoGroups[0].Add(trailing);
            }
            lead.UpdateEngineBrakePressure(ref train.BrakeLine3PressurePSI, 10);
            // Suppress sound callbacks on the uninitialized test lead; pressure still propagates.
            lead.EngineBrakeState = AirSinglePipe.ValveState.Apply;
            TestAirBrake.Propagate(lead);
            Assert.Equal(4, train.Cars.Count);
            foreach (var engine in train.Cars)
            {
                Assert.True(engine.BrakeSystem.BrakeLine3PressurePSI > 0);
                Assert.Equal(lead.BrakeSystem.BrakeLine3PressurePSI, engine.BrakeSystem.BrakeLine3PressurePSI);
            }
        }
    }
}
