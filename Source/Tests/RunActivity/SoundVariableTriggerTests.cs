// COPYRIGHT 2026 by the Open Rails project. GNU GPL version 3 or later.
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using Orts.Formats.Msts;
using Orts.Parsers.Msts;
using Orts.Simulation.RollingStocks;
using Orts.Simulation.RollingStocks.SubSystems;
using Orts.Simulation.RollingStocks.SubSystems.Controllers;
using Orts.Viewer3D;
using Xunit;

namespace Tests.RunActivity
{
    public class SoundVariableTriggerTests
    {
        static T Uninitialized<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));

        static Variable_Trigger Parse(string name, string threshold = "50")
        {
            using var stream = new MemoryStream(Encoding.ASCII.GetBytes($"( {name} {threshold} )"));
            using var reader = new STFReader(stream, "test.sms", Encoding.ASCII, false);
            return new Variable_Trigger(reader);
        }

        static MSTSLocomotive Locomotive()
        {
            var locomotive = Uninitialized<MSTSLocomotive>();
            locomotive.CarID = Guid.NewGuid().ToString();
            locomotive.ThrottleController = new MSTSNotchController(new List<MSTSNotch> { new MSTSNotch(0, true, 0) });
            locomotive.DynamicBrakeController = new MSTSNotchController(new List<MSTSNotch> { new MSTSNotch(0, true, 0) });
            return locomotive;
        }

        [Theory]
        [InlineData("ThrottleController_Inc_Past", SoundVariable.ControlType.ThrottleController, Variable_Trigger.TriggerType.Inc_Past)]
        [InlineData("ThrottleController_Dec_Past", SoundVariable.ControlType.ThrottleController, Variable_Trigger.TriggerType.Dec_Past)]
        [InlineData("DynamicBrakeController_Inc_Past", SoundVariable.ControlType.DynamicBrakeController, Variable_Trigger.TriggerType.Inc_Past)]
        [InlineData("DynamicBrakeController_Dec_Past", SoundVariable.ControlType.DynamicBrakeController, Variable_Trigger.TriggerType.Dec_Past)]
        public void CustomHandleTriggersStillParse(string name, SoundVariable.ControlType control, Variable_Trigger.TriggerType direction)
        {
            var trigger = Parse(name);
            Assert.Equal(control, trigger.TriggerVariable.Control);
            Assert.Equal(direction, trigger.Type);
            Assert.Equal(50f, trigger.Threshold);
        }

        [Fact]
        public void UpstreamEngineTriggersAndUnitsStillParse()
        {
            var trigger = Parse("Engine2Power_Inc_Past", "50kW");
            Assert.Equal(SoundVariable.ControlType.EnginePower, trigger.TriggerVariable.Control);
            Assert.Equal(1, trigger.TriggerVariable.SourceID);
            Assert.Equal(50000f, trigger.Threshold);
        }

        [Fact]
        public void SoundUsesSelectedCruiseControlForceInsteadOfEffectiveThrottle()
        {
            var locomotive = Locomotive();
            locomotive.ThrottleController.SetPercent(10);
            locomotive.CruiseControl = Uninitialized<CruiseControl>();
            locomotive.CruiseControl.SpeedRegulatorMaxForcePercentUnits = true;
            locomotive.CruiseControl.SpeedRegMode = CruiseControl.SpeedRegulatorMode.Auto;
            locomotive.CruiseControl.UseThrottleAsForceSelector = true;
            locomotive.CruiseControl.SelectedMaxAccelerationPercent = 75;
            var stream = Uninitialized<SoundStream>();
            Assert.Equal(75f, stream.ReadValue(new SoundVariable("ThrottleController"), locomotive));
        }

        [Fact]
        public void CombinedHandleSoundIgnoresAutomaticBrakeWhileSelectingTraction()
        {
            var locomotive = Locomotive();
            locomotive.CruiseControl = Uninitialized<CruiseControl>();
            locomotive.CruiseControl.SpeedRegulatorMaxForcePercentUnits = true;
            locomotive.CruiseControl.SpeedRegMode = CruiseControl.SpeedRegulatorMode.Auto;
            locomotive.CruiseControl.UseThrottleAsForceSelector = true;
            locomotive.CruiseControl.UseThrottleInCombinedControl = true;
            locomotive.CruiseControl.SelectedMaxAccelerationPercent = 60;
            locomotive.CombinedControlType = MSTSLocomotive.CombinedControl.ThrottleDynamic;
            locomotive.CombinedControlSplitPosition = 0.5f;
            locomotive.DynamicBrakeController.SetPercent(80);
            var stream = Uninitialized<SoundStream>();
            Assert.Equal(0f, stream.ReadValue(new SoundVariable("DynamicBrakeController"), locomotive));
        }

        sealed class CountingCommand : ORTSSoundCommand
        {
            public int Count;
            public CountingCommand() : base(null) { }
            public override void Run() => Count++;
        }

        [Fact]
        public void ThrottleDescentPlaysOnceAcrossThresholdsAndRearmsAfterAscent()
        {
            var locomotive = Locomotive();
            var stream = Uninitialized<SoundStream>();
            stream.SoundSource = Uninitialized<SoundSource>();
            stream.SoundSource.Car = locomotive;
            var first = new ORTSVariableTrigger(stream, Parse("ThrottleController_Dec_Past", "70"));
            var second = new ORTSVariableTrigger(stream, Parse("ThrottleController_Dec_Past", "30"));
            var firstCommand = new CountingCommand();
            var secondCommand = new CountingCommand();
            first.SoundCommand = firstCommand;
            second.SoundCommand = secondCommand;
            foreach (float percent in new[] { 100f, 60f, 20f })
            {
                locomotive.ThrottleController.SetPercent(percent);
                first.TryTrigger();
                second.TryTrigger();
            }
            Assert.Equal(1, firstCommand.Count);
            Assert.Equal(0, secondCommand.Count);

            locomotive.ThrottleController.SetPercent(100);
            first.TryTrigger();
            second.TryTrigger();
            locomotive.ThrottleController.SetPercent(60);
            first.TryTrigger();
            second.TryTrigger();
            Assert.Equal(2, firstCommand.Count);
            Assert.Equal(0, secondCommand.Count);
        }
    }
}
