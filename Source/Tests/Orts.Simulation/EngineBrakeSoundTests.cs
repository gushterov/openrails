// COPYRIGHT 2026 by the Open Rails project. GNU GPL version 3 or later.
using System.Collections.Generic;
using System.Runtime.Serialization;
using Orts.Common;
using Orts.Simulation.Physics;
using Orts.Simulation.RollingStocks;
using Orts.Simulation.RollingStocks.SubSystems.Brakes.MSTS;
using ORTS.Common;
using Xunit;

namespace Tests.Orts.Simulation
{
    public class EngineBrakeSoundTests
    {
        static T Uninitialized<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));

        class SoundLocomotive : MSTSLocomotive
        {
            public List<Event> Events;
            public SoundLocomotive() : base(null, null) { }
            public override void SignalEvent(Event evt) => Events.Add(evt);
        }

        class TestAirBrake : AirSinglePipe
        {
            public TestAirBrake() : base(null) { }
            public static void Propagate(TrainCar lead) => PropagateBrakeLinePressures(0.1f, lead, false);
        }

        static SoundLocomotive Locomotive(Train train)
        {
            var loco = Uninitialized<SoundLocomotive>();
            loco.Events = new List<Event>();
            loco.Train = train;
            loco.EngineBrakeState = AirSinglePipe.ValveState.Lap;
            loco.BrakeSystem = Uninitialized<AirSinglePipe>();
            loco.BrakeSystem.BrakePipeVolumeM3 = 0.1f;
            loco.MainResVolumeM3 = 1;
            loco.MainResPressurePSI = 100;
            loco.EngineBrakeApplyRatePSIpS = loco.EngineBrakeReleaseRatePSIpS = 20;
            // Isolate independent brakes from the train brake pipe gradient calculation.
            loco.BrakePipeChargingRatePSIorInHgpS = 1000;
            loco.AFMFilter = new IIRFilter(IIRFilter.FilterTypes.Butterworth, 1, IIRFilter.HzToRad(0.1f), 1);
            train.Cars.Add(loco);
            return loco;
        }

        static Train CreateTrain()
        {
            var train = Uninitialized<Train>();
            train.Cars = new List<TrainCar>();
            train.LocoGroups = new List<List<TrainCar>>();
            train.DPLeadUnits = new List<TrainCar>();
            return train;
        }

        [Fact]
        public void ConnectedLocomotivesReceiveApplyReleaseAndStopOncePerTransition()
        {
            var train = CreateTrain();
            var first = Locomotive(train);
            var lead = Locomotive(train);
            var last = Locomotive(train);
            train.DPLeadUnits.Add(lead);
            train.LocoGroups.Add(new List<TrainCar> { first, lead, last });
            train.BrakeLine3PressurePSI = 20;

            TestAirBrake.Propagate(lead);
            TestAirBrake.Propagate(lead);
            foreach (var loco in new[] { first, lead, last })
            {
                Assert.Equal(new[] { Event.EngineBrakePressureDecrease }, loco.Events);
                Assert.True(loco.BrakeSystem.BrakeLine3PressurePSI > 0);
                Assert.Equal(lead.BrakeSystem.BrakeLine3PressurePSI, loco.BrakeSystem.BrakeLine3PressurePSI);
            }

            // Hold the attained pressure, then release and hold again.
            train.BrakeLine3PressurePSI = lead.BrakeSystem.BrakeLine3PressurePSI;
            TestAirBrake.Propagate(lead);
            TestAirBrake.Propagate(lead);
            train.BrakeLine3PressurePSI = 0;
            TestAirBrake.Propagate(lead);
            TestAirBrake.Propagate(lead);
            train.BrakeLine3PressurePSI = lead.BrakeSystem.BrakeLine3PressurePSI;
            TestAirBrake.Propagate(lead);
            TestAirBrake.Propagate(lead);

            foreach (var loco in new[] { first, lead, last })
                Assert.Equal(new[]
                {
                    Event.EngineBrakePressureDecrease,
                    Event.EngineBrakePressureStoppedChanging,
                    Event.EngineBrakePressureIncrease,
                    Event.EngineBrakePressureStoppedChanging,
                }, loco.Events);
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public void RemoteGroupSoundsRequireIndependentBrakeSynchronization(bool leadSync, bool remoteSync)
        {
            var train = CreateTrain();
            var lead = Locomotive(train);
            var remote = Locomotive(train);
            var remoteTrail = Locomotive(train);
            typeof(MSTSLocomotive).GetProperty(nameof(MSTSLocomotive.DPSyncIndependent)).SetValue(lead, leadSync);
            typeof(MSTSLocomotive).GetProperty(nameof(MSTSLocomotive.DPSyncIndependent)).SetValue(remote, remoteSync);
            train.DPLeadUnits.AddRange(new[] { lead, remote });
            train.LocoGroups.Add(new List<TrainCar> { lead });
            train.LocoGroups.Add(new List<TrainCar> { remote, remoteTrail });
            train.BrakeLine3PressurePSI = 20;
            // Remote units follow the master locomotive's brake cylinder pressure.
            typeof(AirSinglePipe).GetField("CylPressurePSI", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .SetValue(lead.BrakeSystem, 20f);
            typeof(AirSinglePipe).GetProperty(nameof(AirSinglePipe.EngineRelayValveRatio)).SetValue(remote.BrakeSystem, 1f);

            TestAirBrake.Propagate(lead);
            TestAirBrake.Propagate(lead);

            Assert.Equal(new[] { Event.EngineBrakePressureDecrease }, lead.Events);
            foreach (var loco in new[] { remote, remoteTrail })
                if (leadSync && remoteSync)
                {
                    Assert.Equal(new[] { Event.EngineBrakePressureDecrease }, loco.Events);
                    Assert.True(loco.BrakeSystem.BrakeLine3PressurePSI > 0);
                }
                else
                {
                    Assert.Empty(loco.Events);
                    Assert.Equal(0, loco.BrakeSystem.BrakeLine3PressurePSI);
                }
        }

        [Fact]
        public void SmallPressureChangesDoNotStartSoundsOnAnyLocomotive()
        {
            var train = CreateTrain();
            var lead = Locomotive(train);
            var trail = Locomotive(train);
            train.DPLeadUnits.Add(lead);
            train.LocoGroups.Add(new List<TrainCar> { lead, trail });
            train.BrakeLine3PressurePSI = 0.01f;

            TestAirBrake.Propagate(lead);

            Assert.Empty(lead.Events);
            Assert.Empty(trail.Events);
            Assert.True(trail.BrakeSystem.BrakeLine3PressurePSI > 0);
        }
    }
}
