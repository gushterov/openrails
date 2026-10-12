// COPYRIGHT 2026 by the Open Rails project. GNU GPL version 3 or later.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using Orts.Common;
using Orts.Simulation;
using Orts.Simulation.Physics;
using Orts.Simulation.RollingStocks;
using Orts.Simulation.RollingStocks.SubSystems;
using Orts.Simulation.RollingStocks.SubSystems.Controllers;
using Orts.Simulation.RollingStocks.SubSystems.PowerSupplies;
using ORTS.Common;
using ORTS.Scripting.Api;
using ORTS.Settings;
using Xunit;

namespace Tests.Orts.Simulation
{
    public class TrainBrakeCommandLogTests : IDisposable
    {
        readonly MSTSLocomotive PreviousReceiver = TrainBrakeCommand.Receiver;
        readonly TraceListener[] PreviousListeners;
        static T Uninitialized<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));

        public TrainBrakeCommandLogTests()
        {
            // Parser tests leave a global listener which rejects even informational traces.
            PreviousListeners = new TraceListener[Trace.Listeners.Count];
            Trace.Listeners.CopyTo(PreviousListeners, 0);
            Trace.Listeners.Clear();
            Trace.Listeners.Add(new DefaultTraceListener());
        }

        class TestLocomotive : MSTSLocomotive
        {
            public TestLocomotive() : base(null, null) { }
            public override string GetTrainBrakeStatus() => "";
            public override void SignalEvent(Event evt) { }
        }

        class TestBrakeScript : BrakeController
        {
            public int Stops;
            public BrakeControllerEvent LastEvent;
            public float? LastTarget;
            public override ControllerState GetState() => ControllerState.Neutral;
            public override void Initialize() { }
            public override void InitializeMoving() { }
            public override float Update(float elapsedClockSeconds) => 0;
            public override void UpdatePressure(ref float pressureBar, float elapsedClockSeconds, ref float epPressureBar) { }
            public override void UpdateEngineBrakePressure(ref float pressureBar, float elapsedClockSeconds) { }
            public override bool IsValid() => true;
            public override float? GetStateFraction() => null;
            public override void HandleEvent(BrakeControllerEvent evt)
            {
                LastEvent = evt;
                if (evt == BrakeControllerEvent.StopIncrease || evt == BrakeControllerEvent.StopDecrease) Stops++;
            }
            public override void HandleEvent(BrakeControllerEvent evt, float? value)
            {
                LastEvent = evt;
                LastTarget = value;
            }
        }

        static (TestLocomotive locomotive, TestBrakeScript script, CommandLog log) CreateLocomotive()
        {
            var simulator = Uninitialized<Simulator>();
            var settings = Uninitialized<UserSettings>();
            settings.SuppressConfirmations = (int)ConfirmLevel.Information;
            typeof(Simulator).GetField(nameof(Simulator.Settings)).SetValue(simulator, settings);
            var catalog = typeof(Simulator).GetProperty("Catalog");
            if (catalog.GetValue(null) == null)
                catalog.SetValue(null, Activator.CreateInstance(catalog.PropertyType, "Orts.Simulation"));
            simulator.Confirmer = new Confirmer(simulator, 1);
            simulator.Log = new CommandLog(simulator);
            simulator.ClockTime = 100;
            var locomotive = Uninitialized<TestLocomotive>();
            typeof(TrainCar).GetField(nameof(TrainCar.Simulator)).SetValue(locomotive, simulator);
            locomotive.Train = Uninitialized<Train>();
            locomotive.Train.TrainType = Train.TRAINTYPE.PLAYER;
            locomotive.RemoteControlGroup = -1;
            var powerSupply = Uninitialized<ScriptedElectricPowerSupply>();
            powerSupply.MaxThrottlePercent = 100;
            powerSupply.PowerSupplyDynamicBrakePercent = -1;
            locomotive.PowerSupply = powerSupply;
            typeof(MSTSLocomotive).GetProperty(nameof(MSTSLocomotive.DynamicBrakeBlendingPercent)).SetValue(locomotive, -1f);
            locomotive.LocalDynamicBrakePercent = -1;
            locomotive.ThrottleController = new MSTSNotchController(new List<MSTSNotch> { new MSTSNotch(0, true, 0) });
            locomotive.DynamicBrakeController = new MSTSNotchController(new List<MSTSNotch> { new MSTSNotch(0, true, 0) });
            locomotive.TrainBrakeController = new ScriptedBrakeController(locomotive);
            var script = new TestBrakeScript();
            typeof(ScriptedBrakeController).GetField("Script", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(locomotive.TrainBrakeController, script);
            TrainBrakeCommand.Receiver = locomotive;
            return (locomotive, script, simulator.Log);
        }

        [Fact]
        public void IdleStopsStillReachTheControllerWithoutRecordingCommands()
        {
            var (locomotive, script, log) = CreateLocomotive();
            for (int i = 0; i < 10000; i++)
            {
                locomotive.StopTrainBrakeDecrease();
                locomotive.StopTrainBrakeIncrease();
            }
            Assert.Empty(log.CommandList);
            Assert.Equal(20000, script.Stops);
        }

        [Theory]
        [InlineData(ControllerPosition.Neutral, false)]
        [InlineData(ControllerPosition.DynamicBrakeHold, false)]
        [InlineData(ControllerPosition.DynamicBrakeIncrease, false)]
        [InlineData(ControllerPosition.DynamicBrakeIncrease, true)]
        [InlineData(ControllerPosition.DynamicBrakeIncreaseFast, true)]
        public void IdleBrakeStopsFromMpcDoNotFloodReplay(ControllerPosition position, bool automatic)
        {
            var (locomotive, script, log) = CreateLocomotive();
            if (automatic)
            {
                locomotive.CruiseControl = Uninitialized<CruiseControl>();
                locomotive.CruiseControl.SpeedRegMode = CruiseControl.SpeedRegulatorMode.Auto;
                locomotive.CruiseControl.CruiseControlLogic = CruiseControl.ControllerCruiseControlLogic.Full;
            }
            var mpc = new MultiPositionController(locomotive) { CanControlTrainBrake = true };
            mpc.PositionsList.Add(new MultiPositionController.Position(position.ToString(), "Default", ""));
            mpc.Initialize();
            for (int i = 0; i < 10000; i++) mpc.Update(1f / 120);
            Assert.Empty(log.CommandList);
            Assert.Equal(10000, script.Stops);
        }

        [Theory]
        [InlineData(true, 0)]
        [InlineData(false, 0)]
        [InlineData(true, 100)]
        [InlineData(false, 100)]
        public void CompletedAdjustmentRecordsOnceAndReplaysItsTarget(bool increase, double startTime)
        {
            var (locomotive, script, log) = CreateLocomotive();
            log.Simulator.ClockTime = startTime;
            if (increase) locomotive.StartTrainBrakeIncrease(null);
            else locomotive.StartTrainBrakeDecrease(null);
            locomotive.TrainBrakeController.CurrentValue = 0.5f;
            for (int i = 0; i < 10000; i++)
            {
                if (increase) locomotive.StopTrainBrakeIncrease();
                else locomotive.StopTrainBrakeDecrease();
            }
            var command = Assert.IsType<TrainBrakeCommand>(Assert.Single(log.CommandList));
            Assert.Equal(startTime, command.Time);
            Assert.Equal(10000, script.Stops);
            locomotive.TrainBrakeController.CurrentValue = increase ? 0 : 1;
            command.Redo();
            Assert.Equal(increase ? BrakeControllerEvent.StartIncrease : BrakeControllerEvent.StartDecrease, script.LastEvent);
            Assert.Equal(0.5f, script.LastTarget);
            // A subsequent genuine adjustment can still be recorded.
            if (increase) locomotive.StopTrainBrakeIncrease();
            else locomotive.StopTrainBrakeDecrease();
            Assert.Equal(2, log.CommandList.Count);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void StoppingTheOppositeDirectionDoesNotLoseTheStartedAdjustment(bool increase)
        {
            var (locomotive, _, log) = CreateLocomotive();
            if (increase)
            {
                locomotive.StartTrainBrakeIncrease(null);
                locomotive.StopTrainBrakeDecrease();
                Assert.Empty(log.CommandList);
                locomotive.StopTrainBrakeIncrease();
            }
            else
            {
                locomotive.StartTrainBrakeDecrease(0, true);
                locomotive.StopTrainBrakeIncrease();
                Assert.Empty(log.CommandList);
                locomotive.StopTrainBrakeDecrease();
            }
            Assert.Single(log.CommandList);
        }

        // Build old-format records without executing them, as BinaryFormatter does on load.
        static TrainBrakeCommand Brake(double time, bool increase = false, float? target = 0)
        {
            var command = Uninitialized<TrainBrakeCommand>();
            command.Time = time;
            typeof(BooleanCommand).GetField("ToState", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(command, increase);
            typeof(ContinuousCommand).GetField("Target", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(command, target);
            return command;
        }

        static CommandLog LoadLegacyLog(List<ICommand> commands)
        {
            var path = Path.GetTempFileName();
            try
            {
                using (var stream = File.Create(path)) new BinaryFormatter().Serialize(stream, commands);
                var log = new CommandLog(null);
                log.LoadLog(path);
                return log;
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void LoadingLegacyFloodCompactsCommandsAndTheirSerializedBackingArray()
        {
            var commands = new List<ICommand>(32768);
            for (int i = 0; i < 10000; i++) commands.Add(Brake(100));
            commands.Add(Brake(200, true, 0.75f));
            var log = LoadLegacyLog(commands);
            Assert.Equal(2, log.CommandList.Count);
            Assert.Equal(2, log.CommandList.Capacity);
            var path = Path.GetTempFileName();
            try
            {
                log.SaveLog(path);
                Assert.True(new FileInfo(path).Length < 4096);
                var reloaded = new CommandLog(null);
                reloaded.LoadLog(path);
                Assert.Equal(2, reloaded.CommandList.Count);
                Assert.Equal(200, reloaded.CommandList[1].Time);
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void CompactionPreservesDifferentAdjustmentsAndInterveningActions()
        {
            var bell = Uninitialized<BellCommand>();
            bell.Time = 101;
            var commands = new List<ICommand>
            {
                Brake(100), Brake(100), Brake(101), Brake(101, true), Brake(101, true, 0.5f),
                Brake(101, true, null), bell, Brake(101, true, null), Brake(101, true, null), bell,
            };
            var log = LoadLegacyLog(commands);
            Assert.Collection(log.CommandList,
                c => Assert.Equal(100, c.Time), c => Assert.Equal(101, c.Time),
                c => Assert.IsType<TrainBrakeCommand>(c), c => Assert.IsType<TrainBrakeCommand>(c),
                c => Assert.IsType<TrainBrakeCommand>(c), c => Assert.IsType<BellCommand>(c),
                c => Assert.IsType<TrainBrakeCommand>(c), c => Assert.IsType<BellCommand>(c));
        }

        [Fact]
        public void EmptyAndAlreadyCompactReplayLogsLoadUnchanged()
        {
            Assert.Empty(LoadLegacyLog(new List<ICommand>()).CommandList);
            var log = LoadLegacyLog(new List<ICommand> { Brake(1), Brake(2) });
            Assert.Equal(2, log.CommandList.Count);
        }

        public void Dispose()
        {
            TrainBrakeCommand.Receiver = PreviousReceiver;
            Trace.Listeners.Clear();
            Trace.Listeners.AddRange(PreviousListeners);
        }
    }
}
