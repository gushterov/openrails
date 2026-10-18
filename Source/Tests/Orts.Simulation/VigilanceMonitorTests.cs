// COPYRIGHT 2026 by the Open Rails project. GNU GPL version 3 or later.
using System.IO;
using System.Reflection;
using System.Text;
using Orts.Parsers.Msts;
using Orts.Simulation.RollingStocks.SubSystems;
using ORTS.Common;
using ORTS.Scripting.Api;
using Xunit;

namespace Tests.Orts.Simulation
{
    public class VigilanceMonitorTests
    {
        class Rig
        {
            public float Time;
            public float Speed = 10;
            public Direction Direction = Direction.Forward;
            public bool Horn, Bell, MPC, Alarm, Emergency;
            public readonly MSTSTrainControlSystem Script;
            public readonly ScriptedTrainControlSystem.MonitoringDevice Monitor;
            static readonly MethodInfo UpdateVigilance = typeof(MSTSTrainControlSystem)
                .GetMethod("UpdateVigilance", BindingFlags.NonPublic | BindingFlags.Instance);

            public Rig()
            {
                Monitor = new ScriptedTrainControlSystem.MonitoringDevice
                {
                    AlarmTimeS = 10,
                    MonitorTimeS = 15,
                    PenaltyTimeS = 5,
                };
                Script = new MSTSTrainControlSystem
                {
                    VigilanceMonitor = Monitor,
                    GameTime = () => Time,
                    SpeedMpS = () => Speed,
                    CurrentDirection = () => Direction,
                    IsAlerterEnabled = () => true,
                    HornPressed = () => Horn,
                    BellPressed = () => Bell,
                    MPCThrottleUp = () => MPC,
                    AlerterSound = () => Alarm,
                    SetVigilanceAlarm = value => Alarm = value,
                    SetVigilanceAlarmDisplay = value => { },
                    SetVigilanceEmergencyDisplay = value => Emergency = value,
                };
                Script.Initialize();
            }

            public void Update(float time)
            {
                Time = time;
                UpdateVigilance.Invoke(Script, null);
            }

            public void Start()
            {
                Update(0); // Disabled -> standby.
                Update(0); // Start the countdown.
            }

            public void Input(string control, bool pressed)
            {
                switch (control)
                {
                    case "Horn": Horn = pressed; break;
                    case "Bell": Bell = pressed; break;
                    case "MPC": MPC = pressed; break;
                }
            }

            public void Enable(string control)
            {
                Monitor.ResetOnHorn = control == "Horn";
                Monitor.ResetOnBell = control == "Bell";
                Monitor.ResetOnMPCThrottleUp = control == "MPC";
            }
        }

        [Theory]
        [InlineData("0kph", 0f)]
        [InlineData("36kph", 10f)]
        [InlineData("10mph", 4.4704f)]
        [InlineData("-1kph", 0f)]
        public void ParsesAndCopiesConfiguration(string speed, float expected)
        {
            string text = "( ORTSMonitoringDeviceActivationSpeed ( " + speed + @" )
                ORTSMonitoringDeviceResetOnHorn ( 1 )
                ORTSMonitoringDeviceResetOnBell ( 1 )
                ORTSMonitoringDeviceResetOnMPCThrottleUp ( 1 ) )";
            using var stream = new MemoryStream(Encoding.Unicode.GetBytes(text));
            using var reader = new STFReader(stream, "", Encoding.Unicode, false);
            var parsed = new ScriptedTrainControlSystem.MonitoringDevice(reader);
            var copy = new ScriptedTrainControlSystem.MonitoringDevice(parsed);
            foreach (var monitor in new[] { parsed, copy })
            {
                Assert.Equal(expected, monitor.ActivationSpeedMpS, 4);
                Assert.True(monitor.ResetOnHorn);
                Assert.True(monitor.ResetOnBell);
                Assert.True(monitor.ResetOnMPCThrottleUp);
            }
        }

        [Fact]
        public void DefaultActivationSpeedIsTwentyKph()
        {
            var rig = new Rig();
            rig.Speed = 19.99f / 3.6f;
            Assert.False(rig.Script.VigilanceSystemEnabled);
            rig.Speed = 20f / 3.6f;
            Assert.True(rig.Script.VigilanceSystemEnabled);
        }

        [Fact]
        public void ZeroThresholdActivatesOnMovementAndRetainsOtherDisableConditions()
        {
            var rig = new Rig();
            rig.Monitor.ActivationSpeedMpS = 0;
            rig.Speed = 0;
            Assert.False(rig.Script.VigilanceSystemEnabled);
            rig.Speed = 0.001f;
            Assert.True(rig.Script.VigilanceSystemEnabled);
            rig.Monitor.ResetOnDirectionNeutral = true;
            rig.Direction = Direction.N;
            Assert.False(rig.Script.VigilanceSystemEnabled);
            rig.Direction = Direction.Reverse;
            Assert.True(rig.Script.VigilanceSystemEnabled);
            rig.Script.IsAlerterEnabled = () => false;
            Assert.False(rig.Script.VigilanceSystemEnabled);
        }

        [Fact]
        public void DisablingZeroSpeedResetIgnoresActivationThreshold()
        {
            var rig = new Rig();
            rig.Monitor.ResetOnZeroSpeed = false;
            rig.Speed = 0;
            Assert.True(rig.Script.VigilanceSystemEnabled);
        }

        [Fact]
        public void StandingStillDoesNotConsumeFirstCountdown()
        {
            var rig = new Rig { Speed = 0 };
            rig.Update(100);
            rig.Speed = 10;
            rig.Update(100);
            rig.Update(100);
            rig.Update(109);
            Assert.False(rig.Alarm);
            rig.Update(110);
            Assert.True(rig.Alarm);
        }

        [Theory]
        [InlineData("Horn")]
        [InlineData("Bell")]
        [InlineData("MPC")]
        public void ExtraControlsAreDisabledByDefault(string control)
        {
            var rig = new Rig();
            rig.Start();
            rig.Input(control, true);
            rig.Update(9);
            rig.Update(10);
            Assert.True(rig.Alarm);
        }

        [Theory]
        [InlineData("Horn")]
        [InlineData("Bell")]
        [InlineData("MPC")]
        public void EachControlResetsOnceOnEntryAndCanBeUsedAgain(string control)
        {
            var rig = new Rig();
            rig.Enable(control);
            rig.Start();
            rig.Input(control, true);
            rig.Update(9);
            rig.Update(10);
            Assert.False(rig.Alarm);
            rig.Update(19); // Holding the input does not suppress the next alarm.
            Assert.True(rig.Alarm);
            rig.Input(control, false);
            rig.Update(19);
            Assert.True(rig.Alarm); // Release alone does not acknowledge.
            rig.Input(control, true);
            rig.Update(20);
            Assert.False(rig.Alarm);
            rig.Update(30);
            Assert.True(rig.Alarm);
        }

        [Fact]
        public void ResetButtonStillAcknowledgesWithExtraControlsDisabled()
        {
            var rig = new Rig();
            rig.Start();
            rig.Update(10);
            Assert.True(rig.Alarm);
            rig.Script.HandleEvent(TCSEvent.AlerterPressed, "");
            rig.Update(11);
            Assert.False(rig.Alarm);
            Assert.True(rig.Script.ResetButtonPressed);
        }

        [Theory]
        [InlineData("Horn")]
        [InlineData("Bell")]
        [InlineData("MPC")]
        public void ExtraControlsDoNotReleasePenaltyWhileMoving(string control)
        {
            var rig = new Rig();
            rig.Enable(control);
            rig.Start();
            rig.Update(10);
            rig.Update(10); // Start warning timer.
            rig.Update(15);
            Assert.True(rig.Emergency);
            rig.Update(15); // Start penalty timer.
            rig.Input(control, true);
            rig.Update(21);
            Assert.True(rig.Emergency);
            rig.Speed = 0;
            rig.Update(22);
            Assert.False(rig.Emergency);
        }
    }
}
