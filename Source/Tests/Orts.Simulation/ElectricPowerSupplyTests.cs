// COPYRIGHT 2026 by the Open Rails project. GNU GPL version 3 or later.
using System;
using System.Reflection;
using System.Runtime.Serialization;
using Orts.Formats.Msts;
using Orts.Simulation;
using Orts.Simulation.RollingStocks;
using Orts.Simulation.RollingStocks.SubSystems.PowerSupplies;
using ORTS.Scripting.Api;
using Xunit;

namespace Tests.Orts.Simulation
{
    public class ElectricPowerSupplyTests
    {
        static T Uninitialized<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));

        sealed class PowerSupplyTestRig
        {
            public readonly ScriptedElectricPowerSupply Supply;
            public readonly Pantograph Pantograph;
            readonly DefaultElectricPowerSupply Script;
            float Time;

            public PowerSupplyTestRig(float mainDelay, float auxiliaryDelay)
            {
                var locomotive = Uninitialized<MSTSElectricLocomotive>();
                var simulator = Uninitialized<Simulator>();
                simulator.TRK = Uninitialized<RouteFile>();
                simulator.TRK.Tr_RouteFile = Uninitialized<Tr_RouteFile>();
                simulator.TRK.Tr_RouteFile.MaxLineVoltage = 25000;
                typeof(TrainCar).GetField(nameof(TrainCar.Simulator)).SetValue(locomotive, simulator);
                locomotive.Pantographs = new Pantographs(locomotive);
                Pantograph = new Pantograph(locomotive);
                locomotive.Pantographs.Add(Pantograph);
                Supply = new ScriptedElectricPowerSupply(locomotive);
                locomotive.PowerSupply = Supply;
                typeof(ScriptedLocomotivePowerSupply).GetProperty(nameof(Supply.PowerOnDelayS)).SetValue(Supply, mainDelay);
                typeof(ScriptedLocomotivePowerSupply).GetProperty(nameof(Supply.AuxPowerOnDelayS)).SetValue(Supply, auxiliaryDelay);
                Script = new DefaultElectricPowerSupply { GameTime = () => Time, SignalEvent = evt => { } };
                typeof(PowerSupply).GetMethod("AttachToHost", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(Script, new object[] { Supply });
                Script.Initialize();
            }

            public void SetPantographState(PantographState state) =>
                typeof(Pantograph).GetProperty(nameof(Pantograph.State)).SetValue(Pantograph, state);

            public void Update(float time)
            {
                Time = time;
                Script.Update(0.01f);
            }
        }

        [Theory]
        [InlineData(2f, 0f)]
        [InlineData(0f, 2f)]
        [InlineData(2f, 4f)]
        public void DynamicBrakeWaitsForBothPowerOnDelays(float mainDelay, float auxiliaryDelay)
        {
            var rig = new PowerSupplyTestRig(mainDelay, auxiliaryDelay);
            rig.SetPantographState(PantographState.Raising);
            rig.Update(0);
            Assert.False(rig.Supply.DynamicBrakeAvailable);

            // Pantograph raising finishes after 3 seconds; the breaker then closes.
            rig.SetPantographState(PantographState.Up);
            rig.Supply.CircuitBreaker.State = CircuitBreakerState.Closed;
            rig.Update(3);
            Assert.False(rig.Supply.DynamicBrakeAvailable);
            var readyTime = 3 + Math.Max(mainDelay, auxiliaryDelay);
            rig.Update(readyTime - 0.01f);
            Assert.False(rig.Supply.DynamicBrakeAvailable);
            rig.Update(readyTime);
            Assert.True(rig.Supply.MainPowerSupplyOn);
            Assert.True(rig.Supply.AuxiliaryPowerSupplyOn);
            Assert.True(rig.Supply.DynamicBrakeAvailable);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void InterruptedStartupRestartsDynamicBrakeWait(bool lowerPantograph)
        {
            var rig = new PowerSupplyTestRig(2, 0);
            rig.SetPantographState(PantographState.Up);
            rig.Supply.CircuitBreaker.State = CircuitBreakerState.Closed;
            rig.Update(0);
            rig.Update(1);
            Assert.False(rig.Supply.DynamicBrakeAvailable);

            if (lowerPantograph)
                rig.SetPantographState(PantographState.Lowering);
            else
                rig.Supply.CircuitBreaker.State = CircuitBreakerState.Open;
            rig.Update(1);
            Assert.False(rig.Supply.DynamicBrakeAvailable);

            rig.SetPantographState(PantographState.Up);
            rig.Supply.CircuitBreaker.State = CircuitBreakerState.Closed;
            rig.Update(3);
            rig.Update(4.99f);
            Assert.False(rig.Supply.DynamicBrakeAvailable);
            rig.Update(5);
            Assert.True(rig.Supply.DynamicBrakeAvailable);

            rig.Supply.CircuitBreaker.State = CircuitBreakerState.Open;
            rig.Update(6);
            Assert.False(rig.Supply.DynamicBrakeAvailable);
        }

        [Fact]
        public void ZeroPowerOnDelaysAllowDynamicBrakeWhenBreakerCloses()
        {
            var rig = new PowerSupplyTestRig(0, 0);
            rig.SetPantographState(PantographState.Up);
            rig.Update(0);
            Assert.False(rig.Supply.DynamicBrakeAvailable);
            rig.Supply.CircuitBreaker.State = CircuitBreakerState.Closed;
            rig.Update(0);
            Assert.True(rig.Supply.DynamicBrakeAvailable);
        }
    }
}
