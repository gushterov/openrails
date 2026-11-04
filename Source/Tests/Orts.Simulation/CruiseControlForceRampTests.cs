// COPYRIGHT 2026 by the Open Rails project. GNU GPL version 3 or later.
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using Orts.Parsers.Msts;
using Orts.Simulation;
using Orts.Simulation.RollingStocks;
using Orts.Simulation.RollingStocks.SubSystems;
using Orts.Simulation.RollingStocks.SubSystems.Brakes.MSTS;
using Orts.Simulation.RollingStocks.SubSystems.Controllers;
using Orts.Simulation.RollingStocks.SubSystems.PowerSupplies;
using ORTS.Common;
using ORTS.Scripting.Api;
using Xunit;

namespace Tests.Orts.Simulation
{
    public class CruiseControlForceRampTests
    {
        static void SetRate(MSTSLocomotive locomotive, string name, float rate) =>
            typeof(MSTSLocomotive).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(locomotive, rate);

        static MSTSLocomotive CreateLocomotive(float speed = 10, bool electric = false)
        {
            var locomotive = CoastingCabControlTests.CreateLocomotive(0, -1, 100, false, true);
            if (electric)
            {
                locomotive = CreateElectricLocomotive(locomotive);
            }
            locomotive.Train.Cars = new List<TrainCar> { locomotive };
            locomotive.MaxForceN = locomotive.MaxDynamicBrakeForceN = 300000;
            locomotive.MaxCurrentA = 1000;
            locomotive.AbsTractionSpeedMpS = locomotive.PrevAbsTractionSpeedMpS = speed;
            locomotive.AbsWheelSpeedMpS = locomotive.WheelSpeedMpS = locomotive.SpeedMpS = speed;
            locomotive.LocomotivePowerSupply.MainPowerSupplyState = PowerSupplyState.PowerOn;
            ((ScriptedLocomotivePowerSupply)locomotive.LocomotivePowerSupply).AvailableTractionPowerW = float.MaxValue;
            locomotive.LocomotivePowerSupply.DynamicBrakeAvailable = true;
            locomotive.DynamicBrakeAvailable = true;
            // Deliberately nonlinear demand-to-force mapping: 50% is only 1/3 of full force.
            locomotive.TractiveForceCurves = new InterpolatorDiesel2D(3)
            {
                Size = 3,
                X = new[] { 0f, 0.5f, 1f },
                Y = new[]
                {
                    new Interpolator(new[] { 0f, 40f }, new[] { 0f, 0f }),
                    new Interpolator(new[] { 0f, 40f }, new[] { 100000f, 100000f }),
                    new Interpolator(new[] { 0f, 40f }, new[] { 300000f, 300000f }),
                },
            };
            locomotive.DynamicBrakeForceCurves = locomotive.TractiveForceCurves;
            locomotive.CruiseControl = new CruiseControl(locomotive)
            {
                SpeedRegMode = CruiseControl.SpeedRegulatorMode.Auto,
                SpeedSelMode = CruiseControl.SpeedSelectorMode.Start,
                SpeedRegulatorMaxForcePercentUnits = true,
                SelectedMaxAccelerationPercent = 100,
                SelectedSpeedMpS = 20,
                PowerReductionValue = 100,
                ThrottleFullRangeIncreaseTimeSeconds = 1000,
                ThrottleFullRangeDecreaseTimeSeconds = 1000,
                DynamicBrakeFullRangeIncreaseTimeSeconds = 1000,
                DynamicBrakeFullRangeDecreaseTimeSeconds = 1000,
            };
            return locomotive;
        }

        static MSTSElectricLocomotive CreateElectricLocomotive(MSTSLocomotive template)
        {
            var locomotive = (MSTSElectricLocomotive)FormatterServices.GetUninitializedObject(typeof(MSTSElectricLocomotive));
            locomotive.Train = template.Train;
            locomotive.PowerSupply = template.PowerSupply;
            locomotive.RemoteControlGroup = -1;
            locomotive.LocalDynamicBrakePercent = -1;
            locomotive.Variable1 = new float[1];
            typeof(MSTSLocomotive).GetProperty(nameof(MSTSLocomotive.DynamicBrakeBlendingPercent)).SetValue(locomotive, -1f);
            return locomotive;
        }

        static void UpdateElectricSound(MSTSLocomotive locomotive) =>
            typeof(MSTSElectricLocomotive).GetMethod("UpdateSoundVariables", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(locomotive, new object[] { 0.02f });

        static float ChangeDemand(MSTSLocomotive locomotive, float demand, float target, float seconds)
        {
            var args = new object[] { demand, seconds, target };
            typeof(CruiseControl).GetMethod(target > demand ? "IncreaseForce" : "DecreaseForce",
                BindingFlags.Instance | BindingFlags.NonPublic).Invoke(locomotive.CruiseControl, args);
            return (float)args[0];
        }

        static float UpdateForce(MSTSLocomotive locomotive, bool braking, float demand, float seconds)
        {
            locomotive.ThrottlePercent = braking ? 0 : demand;
            locomotive.DynamicBrakePercent = braking ? demand : -1;
            locomotive.DynamicBrake = braking;
            locomotive.DynamicBrakeCommandStartTime = 0;
            typeof(MSTSLocomotive).GetMethod(braking ? "UpdateDynamicBrakeForce" : "UpdateTractionForce",
                BindingFlags.Instance | BindingFlags.NonPublic).Invoke(locomotive, new object[] { seconds });
            return braking ? locomotive.DynamicBrakeForceN : locomotive.TractionForceN;
        }

        static void ConfigureBrakePipe(MSTSLocomotive locomotive)
        {
            locomotive.BrakeSystem = (AirSinglePipe)FormatterServices.GetUninitializedObject(typeof(AirSinglePipe));
            locomotive.BrakeSystem.BrakeLine1PressurePSI = 90;
            locomotive.TrainBrakeController = new ScriptedBrakeController(locomotive) { MaxPressurePSI = 90 };
            locomotive.TrainBrakeController.Notches.Add(new MSTSNotch(0, false, (int)ControllerState.Release));
            typeof(ScriptedBrakeController).GetField("Script", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(locomotive.TrainBrakeController, new MSTSBrakeController());
        }

        static MSTSLocomotive CreateTractionCutoffLocomotive(bool powerOnly = false)
        {
            var locomotive = CreateLocomotive(powerOnly ? 10 : 0);
            ConfigureBrakePipe(locomotive);
            locomotive.RemoteControlGroup = 0;
            locomotive.TrainControlSystem = new ScriptedTrainControlSystem(locomotive);
            locomotive.Train.MassKg = 600000;
            locomotive.CruiseControl.SelectedSpeedMpS = 40f / 3.6f;
            SetRate(locomotive, "TractionForceRampUpNpS", powerOnly ? 0 : 50000);
            SetRate(locomotive, "TractionPowerRampUpWpS", powerOnly ? 200000 : 0);
            SetRate(locomotive, "TractionForceRampDownNpS", 50000);
            return locomotive;
        }

        static void SetTractionAuthorization(MSTSLocomotive locomotive, bool authorized) =>
            typeof(ScriptedTrainControlSystem).GetProperty(nameof(ScriptedTrainControlSystem.TractionAuthorization))
                .SetValue(locomotive.TrainControlSystem, authorized);

        [Theory]
        [InlineData(30, false)]
        [InlineData(120, false)]
        [InlineData(30, true)]
        [InlineData(120, true)]
        public void BrakeCutoffReleaseStartsCCDemandAtZeroAndMatchesManualForceRamp(int updatesPerSecond, bool powerOnly)
        {
            var automatic = CreateTractionCutoffLocomotive(powerOnly);
            var manual = CreateTractionCutoffLocomotive(powerOnly);
            SetTractionAuthorization(automatic, false);
            SetTractionAuthorization(manual, false);
            float step = 1f / updatesPerSecond;
            // The brake pipe can be released while cylinder pressure still keeps
            // the TCS traction authorization off. Both modes must wait for it.
            for (int frame = 0; frame < 5 * updatesPerSecond; frame++)
            {
                float precedingDemand = automatic.CruiseControl.ThrottlePercent ?? 0;
                automatic.CruiseControl.Update(step);
                Assert.Equal(0f, UpdateForce(automatic, false, precedingDemand, step));
                Assert.Equal(0f, UpdateForce(manual, false, 100, step));
            }
            Assert.Equal(0f, automatic.CruiseControl.CCThrottleOrDynBrakePercent);
            Assert.Equal(100f, automatic.CruiseControl.SelectedMaxAccelerationPercent);
            SetTractionAuthorization(automatic, true);
            SetTractionAuthorization(manual, true);
            for (int frame = 0; frame < 2 * updatesPerSecond; frame++)
            {
                float precedingDemand = automatic.CruiseControl.ThrottlePercent ?? 0;
                automatic.CruiseControl.Update(step);
                float actual = UpdateForce(automatic, false, precedingDemand, step);
                float expected = UpdateForce(manual, false, 100, step);
                // CC sampling and controller publication can lag the manual
                // command by one sample, but the force rise must have the same slope.
                float rate = powerOnly ? 20000 : 50000;
                float tolerance = rate * (automatic.CruiseControl.SamplingPeriodS + 2 * step) + 2;
                Assert.InRange(actual, Math.Max(0, expected - tolerance), expected + 0.01f);
                Assert.InRange(automatic.CruiseControl.CCThrottleOrDynBrakePercent, 0, powerOnly ? 25 : 50);
            }
            Assert.True(automatic.TractionForceN > 0);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void BrakeCutoffDoesNotAccumulateDemandWithEitherRampMode(bool sharedRamps)
        {
            var locomotive = CreateTractionCutoffLocomotive();
            locomotive.CruiseControl.UseLocomotiveForceRamps = sharedRamps;
            SetTractionAuthorization(locomotive, false);
            for (int frame = 0; frame < 300; frame++)
                locomotive.CruiseControl.CalculateRequiredForce(0.02f, 0);
            Assert.Equal(0f, locomotive.CruiseControl.CCThrottleOrDynBrakePercent);
            Assert.Equal(100f, locomotive.CruiseControl.SelectedMaxAccelerationPercent);
        }

        [Fact]
        public void BrakeCutoffClearsExistingDemandBeforeTheNextCCSample()
        {
            var locomotive = CreateTractionCutoffLocomotive();
            locomotive.CruiseControl.CCThrottleOrDynBrakePercent = 60;
            locomotive.CruiseControl.SamplingPeriodS = 10;
            SetTractionAuthorization(locomotive, false);
            locomotive.CruiseControl.Update(0.01f);
            Assert.Equal(0f, locomotive.CruiseControl.CCThrottleOrDynBrakePercent);
            Assert.Equal(0f, locomotive.CruiseControl.ThrottlePercent);
            Assert.Equal(100f, locomotive.CruiseControl.SelectedMaxAccelerationPercent);
        }

        [Fact]
        public void TractionCutoffStillPermitsCCDynamicBraking()
        {
            var locomotive = CreateTractionCutoffLocomotive();
            locomotive.CruiseControl.SelectedSpeedMpS = 5;
            locomotive.CruiseControl.CurrentSelectedSpeedMpS = 5;
            locomotive.CruiseControl.StartReducingSpeedDeltaDownwards = 1;
            SetRate(locomotive, "DynamicBrakeForceRampUpNpS", 50000);
            locomotive.AbsTractionSpeedMpS = 10;
            locomotive.WheelSpeedMpS = locomotive.AbsWheelSpeedMpS = locomotive.SpeedMpS = 10;
            SetTractionAuthorization(locomotive, false);
            locomotive.CruiseControl.CalculateRequiredForce(0.02f, 10);
            Assert.True(locomotive.CruiseControl.CCThrottleOrDynBrakePercent < 0);
        }

        [Theory]
        [InlineData(30, 300000f, 20000f, 10000f, 0f, false)]
        [InlineData(120, 300000f, 20000f, 10000f, 0f, false)]
        [InlineData(30, 1000000f, 40000f, 20000f, 0f, false)]
        [InlineData(120, 1000000f, 40000f, 20000f, 0f, false)]
        [InlineData(30, 600000f, 50000f, 5000f, 0f, false)]
        [InlineData(120, 600000f, 50000f, 5000f, 0f, false)]
        [InlineData(30, 600000f, 50000f, 5000f, 0.25f, false)]
        [InlineData(120, 600000f, 50000f, 5000f, 0.25f, false)]
        [InlineData(30, 1000000f, 40000f, 20000f, 0.25f, true)]
        [InlineData(120, 1000000f, 40000f, 20000f, 0.25f, true)]
        public void ApproachesFortyKphWithoutRepeatedlyCuttingTraction(int updatesPerSecond, float massKg, float resistanceN, float rampDownNpS,
            float accelerationLagS, bool reverse)
        {
            var locomotive = CreateLocomotive(0);
            ConfigureBrakePipe(locomotive);
            var simulator = (Simulator)FormatterServices.GetUninitializedObject(typeof(Simulator));
            typeof(TrainCar).GetField(nameof(TrainCar.Simulator)).SetValue(locomotive, simulator);
            locomotive.Train.MassKg = massKg;
            locomotive.Train.MUDirection = reverse ? Direction.Reverse : Direction.Forward;
            SetRate(locomotive, "TractionForceRampUpNpS", 50000);
            SetRate(locomotive, "TractionForceRampDownNpS", rampDownNpS);
            SetRate(locomotive, "TractionForceRampDownToZeroNpS", 100000);
            SetRate(locomotive, "DynamicBrakeForceRampUpNpS", 50000);
            SetRate(locomotive, "DynamicBrakeForceRampDownNpS", 50000);
            SetRate(locomotive, "DynamicBrakeForceRampDownToZeroNpS", 100000);
            locomotive.CruiseControl.SelectedSpeedMpS = 40f / 3.6f;
            float step = 1f / updatesPerSecond;
            float speed = 0, maximumSpeed = 0;
            float sampleTime = 0;
            bool hadTraction = false, reachedSpeed = false;
            int zeroCuts = 0;
            var updateTraction = typeof(MSTSLocomotive).GetMethod("UpdateTractionForce", BindingFlags.Instance | BindingFlags.NonPublic);
            var updateBrake = typeof(MSTSLocomotive).GetMethod("UpdateDynamicBrakeForce", BindingFlags.Instance | BindingFlags.NonPublic);
            for (int frame = 0; frame < 180 * updatesPerSecond; frame++)
            {
                float previousDemand = locomotive.CruiseControl.CCThrottleOrDynBrakePercent;
                // Controllers publish the preceding CC output before CC samples again.
                locomotive.LocalThrottlePercent = Math.Max(previousDemand, 0);
                locomotive.LocalDynamicBrakePercent = previousDemand < 0 ? -previousDemand : -1;
                sampleTime += step;
                if (sampleTime >= locomotive.CruiseControl.SamplingPeriodS)
                {
                    locomotive.CruiseControl.CalculateRequiredForce(sampleTime, speed);
                    sampleTime = 0;
                }
                float demand = locomotive.CruiseControl.CCThrottleOrDynBrakePercent;
                if (!reachedSpeed && hadTraction && previousDemand > 0 && demand <= 0) zeroCuts++;
                hadTraction |= demand > 5;
                updateTraction.Invoke(locomotive, new object[] { step });
                updateBrake.Invoke(locomotive, new object[] { step });
                float acceleration = (locomotive.TractionForceN - locomotive.DynamicBrakeForceN - resistanceN) / massKg;
                if (speed <= 0) acceleration = Math.Max(acceleration, 0);
                speed = Math.Max(speed + acceleration * step, 0);
                float signedAcceleration = reverse ? -acceleration : acceleration;
                locomotive._AccelerationMpSS = accelerationLagS > 0
                    ? locomotive._AccelerationMpSS + (signedAcceleration - locomotive._AccelerationMpSS) * (1 - (float)Math.Exp(-step / accelerationLagS))
                    : signedAcceleration;
                locomotive.TractiveForceN = (reverse ? -1 : 1) * (locomotive.TractionForceN - locomotive.DynamicBrakeForceN);
                locomotive.PrevAbsTractionSpeedMpS = locomotive.AbsTractionSpeedMpS;
                locomotive.AbsTractionSpeedMpS = speed;
                locomotive.AbsWheelSpeedMpS = speed;
                locomotive.WheelSpeedMpS = locomotive.SpeedMpS = reverse ? -speed : speed;
                simulator.ClockTime += step;
                maximumSpeed = Math.Max(maximumSpeed, speed);
                reachedSpeed |= speed >= 40f / 3.6f - 0.05f;
            }
            Assert.True(zeroCuts == 0, $"Throttle reached zero {zeroCuts} times before 40 km/h; final {speed * 3.6f:F2}, peak {maximumSpeed * 3.6f:F2} km/h.");
            Assert.True(reachedSpeed);
            Assert.InRange(speed * 3.6f, 39.75f, 40.25f);
            Assert.InRange(maximumSpeed * 3.6f, 39.75f, 40.5f);
            Assert.True(locomotive.CruiseControl.CCThrottleOrDynBrakePercent > 0);
        }

        [Fact]
        public void BalancingEffortIncludesOnlyLocomotivesOnTheSameControlGroupAndHonorsTheForceLimit()
        {
            var lead = CreateLocomotive();
            var helper = CreateLocomotive();
            helper.Train = lead.Train;
            lead.Train.Cars.Add(helper);
            lead.RemoteControlGroup = helper.RemoteControlGroup = 0;
            lead.Train.MassKg = 1000000;
            lead.TractionForceN = helper.TractionForceN = 100000;
            lead._AccelerationMpSS = 0.1f;
            SetRate(lead, "TractionForceRampDownNpS", 20000);
            SetRate(helper, "TractionForceRampDownNpS", 20000);
            var calculate = typeof(CruiseControl).GetMethod("CalculateTractionDemand", BindingFlags.Instance | BindingFlags.NonPublic);
            // 200 kN delivered minus 100 kN accelerating the train leaves 100 kN
            // to hold speed: 50 kN per engine, or 25% on these nonlinear curves.
            float target = (float)calculate.Invoke(lead.CruiseControl, new object[] { 0f, 1f, 100f, 0f });
            Assert.InRange(target, 24.999f, 25.001f);
            Assert.Equal(10f, (float)calculate.Invoke(lead.CruiseControl, new object[] { 0f, 1f, 10f, 0f }));
            helper.RemoteControlGroup = 1;
            Assert.Equal(0f, (float)calculate.Invoke(lead.CruiseControl, new object[] { 0f, 1f, 100f, 0f }));
        }

        [Fact]
        public void LegacyComparisonSettingIsParsedAndCloned()
        {
            var locomotive = CreateLocomotive();
            Assert.True(locomotive.CruiseControl.UseLocomotiveForceRamps);
            using (var reader = new STFReader(new MemoryStream(Encoding.ASCII.GetBytes("( UseLocomotiveForceRamps ( false ) )")),
                "cruise-control.eng", Encoding.ASCII, false))
                locomotive.CruiseControl.Parse(reader);
            Assert.False(locomotive.CruiseControl.UseLocomotiveForceRamps);
            Assert.False(locomotive.CruiseControl.Clone(locomotive).UseLocomotiveForceRamps);
        }

        [Theory]
        [InlineData(false, 10, 30, true)]
        [InlineData(false, 10, 120, true)]
        [InlineData(true, 10, 30, true)]
        [InlineData(true, 10, 120, true)]
        [InlineData(false, 20, 30, false)]
        [InlineData(false, 20, 120, false)]
        [InlineData(true, 20, 30, false)]
        [InlineData(true, 20, 120, false)]
        public void AutomaticAndManualDemandHaveTheSamePhysicalRamp(bool braking, float speed, int updatesPerSecond, bool forceLimited)
        {
            var automatic = CreateLocomotive(speed);
            var manual = CreateLocomotive(speed);
            string prefix = braking ? "DynamicBrake" : "Traction";
            foreach (var locomotive in new[] { automatic, manual })
            {
                SetRate(locomotive, prefix + "ForceRampUpNpS", forceLimited ? 10000 : 0);
                SetRate(locomotive, prefix + "PowerRampUpWpS", 200000);
            }
            float demand = 0;
            for (int frame = 0; frame < updatesPerSecond; frame++)
            {
                demand = ChangeDemand(automatic, demand, braking ? -100 : 100, 1f / updatesPerSecond);
                Assert.InRange(Math.Abs(demand), 0.001f, 99.999f);
                float actual = UpdateForce(automatic, braking, Math.Abs(demand), 1f / updatesPerSecond);
                float expected = UpdateForce(manual, braking, 100, 1f / updatesPerSecond);
                Assert.InRange(actual, expected - 1, expected + 0.001f);
            }
            float force = braking ? automatic.DynamicBrakeForceN : automatic.TractionForceN;
            Assert.InRange(force, 9999, 10001);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ReleaseUsesSeparateOrdinaryAndZeroRamps(bool braking)
        {
            var locomotive = CreateLocomotive();
            string prefix = braking ? "DynamicBrake" : "Traction";
            SetRate(locomotive, prefix + "ForceRampDownNpS", 30000);
            SetRate(locomotive, prefix + "PowerRampDownWpS", 200000);
            SetRate(locomotive, prefix + "ForceRampDownToZeroNpS", 150000);
            SetRate(locomotive, prefix + "PowerRampDownToZeroWpS", 1000000);
            if (braking) locomotive.DynamicBrakeForceN = 300000;
            else locomotive.TractionForceN = 300000;
            float demand = ChangeDemand(locomotive, braking ? -100 : 100, braking ? -50 : 50, 1);
            Assert.InRange(Math.Abs(demand), 94.999f, 95.001f);
            Assert.InRange(UpdateForce(locomotive, braking, Math.Abs(demand), 1), 279999f, 280001f);
            demand = ChangeDemand(locomotive, demand, 0, 1);
            Assert.InRange(Math.Abs(demand), 69.999f, 70.001f);
            // While demand remains nonzero, physics still enforces the ordinary down ramp.
            Assert.InRange(UpdateForce(locomotive, braking, Math.Abs(demand), 1), 259999f, 260001f);
            for (int second = 0; second < 5; second++)
            {
                demand = ChangeDemand(locomotive, demand, 0, 1);
                UpdateForce(locomotive, braking, Math.Abs(demand), 1);
            }
            Assert.Equal(0f, demand);
            Assert.Equal(0f, braking ? locomotive.DynamicBrakeForceN : locomotive.TractionForceN);
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(true, false)]
        [InlineData(false, true)]
        [InlineData(true, true)]
        public void LegacyTimingRemainsAvailablePerDirection(bool braking, bool disableSharedRamps)
        {
            var locomotive = CreateLocomotive();
            string prefix = braking ? "DynamicBrake" : "Traction";
            SetRate(locomotive, prefix + "ForceRampUpNpS", 50000);
            locomotive.CruiseControl.UseLocomotiveForceRamps = !disableSharedRamps;
            float start = braking ? -50 : 50;
            // A ramp-up rate alone must not bypass the legacy release timing.
            Assert.InRange(Math.Abs(ChangeDemand(locomotive, start, 0, 1)), 49.899f, 49.901f);
            float applied = ChangeDemand(locomotive, 0, braking ? -100 : 100, 1);
            Assert.InRange(Math.Abs(applied), disableSharedRamps ? 0.099f : 24.999f, disableSharedRamps ? 0.101f : 25.001f);
        }

        [Fact]
        public void PowerOnlyRampAtStandstillRetainsLegacyDemandTiming()
        {
            var locomotive = CreateLocomotive(0);
            SetRate(locomotive, "TractionPowerRampUpWpS", 200000);
            Assert.InRange(ChangeDemand(locomotive, 0, 100, 1), 0.099f, 0.101f);
        }

        [Theory]
        [InlineData(false, true)]
        [InlineData(true, true)]
        [InlineData(false, false)]
        [InlineData(true, false)]
        public void UnavailableForceCurveStillReleasesDemandSmoothly(bool braking, bool forceReferenceAvailable)
        {
            var locomotive = CreateLocomotive();
            if (!forceReferenceAvailable) locomotive.MaxForceN = locomotive.MaxDynamicBrakeForceN = 0;
            locomotive.TractiveForceCurves = locomotive.DynamicBrakeForceCurves = new InterpolatorDiesel2D(2)
            {
                Size = 2,
                X = new[] { 0f, 1f },
                Y = new[]
                {
                    new Interpolator(new[] { 0f, 40f }, new[] { 0f, 0f }),
                    new Interpolator(new[] { 0f, 40f }, new[] { 0f, 0f }),
                },
            };
            SetRate(locomotive, (braking ? "DynamicBrake" : "Traction") + "ForceRampDownToZeroNpS", 10000);
            float demand = ChangeDemand(locomotive, braking ? -50 : 50, 0, 0.02f);
            Assert.InRange(Math.Abs(demand), forceReferenceAvailable ? 49.933f : 49.997f, forceReferenceAvailable ? 49.934f : 49.999f);
        }

        [Fact]
        public void AnticipationIncludesOrdinaryReleaseWhileDemandIsStillNonzero()
        {
            var locomotive = CreateLocomotive();
            locomotive.TractionForceN = 300000;
            SetRate(locomotive, "TractionForceRampDownNpS", 20000);
            SetRate(locomotive, "TractionForceRampDownToZeroNpS", 100000);
            float time = (float)typeof(MSTSLocomotive).GetMethod("GetCruiseControlForceReleaseTimeS",
                BindingFlags.Instance | BindingFlags.NonPublic).Invoke(locomotive, new object[] { false, 100f });
            // Demand takes 3 seconds; 240 kN of residual effort then needs 2.4 seconds.
            Assert.InRange(time, 5.399f, 5.401f);
        }

        [Fact]
        public void ElectricEffortSoundFollowsResidualForceAfterThrottleReachesZero()
        {
            var locomotive = CreateLocomotive(electric: true);
            locomotive.LocalThrottlePercent = 0;
            locomotive.TractionForceN = locomotive.TractiveForceN = 150000;
            locomotive.Variable2 = 50;
            UpdateElectricSound(locomotive);
            Assert.Equal(0f, locomotive.Variable1[0]);
            Assert.Equal(50f, locomotive.Variable2);
            locomotive.TractionForceN = locomotive.TractiveForceN = 120000;
            UpdateElectricSound(locomotive);
            Assert.Equal(48f, locomotive.Variable2);
            locomotive.TractionForceN = locomotive.TractiveForceN = 0;
            UpdateElectricSound(locomotive);
            Assert.Equal(46f, locomotive.Variable2);
            for (int sample = 0; sample < 24; sample++) UpdateElectricSound(locomotive);
            Assert.Equal(0f, locomotive.Variable2);
        }

        [Fact]
        public void DynamicBrakeForceDoesNotActivateTheTractionSoundVariable()
        {
            var locomotive = CreateLocomotive(electric: true);
            locomotive.LocalThrottlePercent = 0;
            locomotive.LocalDynamicBrakePercent = 50;
            locomotive.DynamicBrakeForceN = 150000;
            locomotive.TractiveForceN = -150000;
            UpdateElectricSound(locomotive);
            Assert.Equal(0f, locomotive.Variable2);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void AnticipationIncludesActualResidualForceAndBothReleaseLimits(bool braking)
        {
            var locomotive = CreateLocomotive();
            string prefix = braking ? "DynamicBrake" : "Traction";
            SetRate(locomotive, prefix + "ForceRampDownToZeroNpS", 100000);
            SetRate(locomotive, prefix + "PowerRampDownToZeroWpS", 200000);
            if (braking) locomotive.DynamicBrakeForceN = 100000;
            else locomotive.TractionForceN = 100000;
            // 1 second from the force limit, 5 seconds from the power limit, at 10 m/s.
            float time = (float)typeof(MSTSLocomotive).GetMethod("GetCruiseControlForceReleaseTimeS",
                BindingFlags.Instance | BindingFlags.NonPublic).Invoke(locomotive, new object[] { braking, 0f });
            Assert.Equal(5f, time);
            locomotive.CruiseControl.CCThrottleOrDynBrakePercent = 0;
            Assert.Equal(5f, (float)typeof(MSTSLocomotive).GetMethod("GetCruiseControlForceReleaseTimeS",
                BindingFlags.Instance | BindingFlags.NonPublic).Invoke(locomotive, new object[] { braking, 0f }));
        }

        [Fact]
        public void RegulatorSmoothlyReleasesTractionBeforeSetSpeedWhenPhysicalForceWillPersist()
        {
            var locomotive = CreateLocomotive(electric: true);
            locomotive.BrakeSystem = (AirSinglePipe)FormatterServices.GetUninitializedObject(typeof(AirSinglePipe));
            locomotive.BrakeSystem.BrakeLine1PressurePSI = 90;
            locomotive.TrainBrakeController = new ScriptedBrakeController(locomotive) { MaxPressurePSI = 90 };
            locomotive.TractionForceN = 100000;
            locomotive.TractiveForceN = 100000;
            locomotive.Variable2 = 100f / 3;
            locomotive._AccelerationMpSS = 0.1f;
            locomotive.Train.MassKg = 1000000;
            SetRate(locomotive, "TractionForceRampDownNpS", 10000);
            SetRate(locomotive, "TractionForceRampDownToZeroNpS", 10000);
            locomotive.CruiseControl.SelectedSpeedMpS = 10.5f;
            locomotive.CruiseControl.CurrentSelectedSpeedMpS = 10.5f;
            locomotive.CruiseControl.CCThrottleOrDynBrakePercent = 50;
            locomotive.CruiseControl.CalculateRequiredForce(0.02f, 10);
            Assert.InRange(locomotive.CruiseControl.CCThrottleOrDynBrakePercent, 49.899f, 49.901f);
            locomotive.LocalThrottlePercent = locomotive.CruiseControl.CCThrottleOrDynBrakePercent;
            UpdateElectricSound(locomotive);
            Assert.InRange(locomotive.Variable1[0], 49.899f, 49.901f);
            Assert.InRange(locomotive.Variable2, 33.32f, 33.34f);
            // The outstanding force keeps the following sample easing off rather than
            // reapplying power or cutting the throttle percentage (and sound) to zero.
            locomotive.CruiseControl.CalculateRequiredForce(0.02f, 10);
            Assert.InRange(locomotive.CruiseControl.CCThrottleOrDynBrakePercent, 49.799f, 49.801f);
        }
    }
}
