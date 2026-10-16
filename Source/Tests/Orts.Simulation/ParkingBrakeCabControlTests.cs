// COPYRIGHT 2026 by the Open Rails project. GNU GPL version 3 or later.
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using Orts.Formats.Msts;
using Orts.Parsers.Msts;
using Orts.Simulation.RollingStocks;
using Orts.Simulation.RollingStocks.SubSystems.Brakes.MSTS;
using Xunit;

namespace Tests.Orts.Simulation
{
    public class ParkingBrakeCabControlTests
    {
        public static MSTSLocomotive CreateLocomotive(bool fitted = true)
        {
            var locomotive = CoastingCabControlTests.CreateLocomotive(0, -1, 0, false, false);
            var brake = (AirSinglePipe)FormatterServices.GetUninitializedObject(typeof(AirSinglePipe));
            brake.HandBrakePresent = fitted;
            locomotive.BrakeSystem = brake;
            locomotive.Train.Cars = new List<TrainCar> { locomotive };
            return locomotive;
        }

        static float ReadIndicator(MSTSLocomotive locomotive) => locomotive.GetDataOf(new CabViewControl
        {
            ControlType = new CabViewControlType(CABViewControlTypes.ORTS_PARKING_BRAKE),
        });

        static void ParseStartWithParkingBrake(MSTSLocomotive locomotive, string value)
        {
            using var stream = new MemoryStream(Encoding.ASCII.GetBytes("( " + value + " )"));
            using var reader = new STFReader(stream, "test.eng", Encoding.ASCII, false);
            locomotive.Parse("engine(ortsstartwithparkingbrake", reader);
        }

        [Theory]
        [InlineData("1", true)]
        [InlineData("0", false)]
        [InlineData("true", true)]
        [InlineData("false", false)]
        [InlineData("", false)]
        public void StartupParameterParsesBooleanValues(string value, bool expected)
        {
            var locomotive = CreateLocomotive();
            Assert.False(locomotive.StartWithParkingBrake);
            ParseStartWithParkingBrake(locomotive, value);
            Assert.Equal(expected, locomotive.StartWithParkingBrake);
            locomotive.Train.InitializeParkingBrakes();
            Assert.Equal(expected, locomotive.BrakeSystem.GetHandbrakeStatus());
            Assert.Equal(expected ? 1f : 0f, ReadIndicator(locomotive));
        }

        [Theory]
        [InlineData(-10f)]
        [InlineData(10f)]
        [InlineData(-0.01f)]
        [InlineData(0.01f)]
        public void MovingStartDoesNotApplyParkingBrake(float speed)
        {
            var locomotive = CreateLocomotive();
            ParseStartWithParkingBrake(locomotive, "1");
            locomotive.Train.SpeedMpS = speed;
            locomotive.Train.InitializeParkingBrakes();
            Assert.False(locomotive.BrakeSystem.GetHandbrakeStatus());
        }

        [Theory]
        [InlineData("air", true)]
        [InlineData("air", false)]
        [InlineData("vacuum", true)]
        [InlineData("vacuum", false)]
        [InlineData("manual", true)]
        [InlineData("manual", false)]
        public void StartupUsesTheFittedHandbrake(string system, bool fitted)
        {
            var locomotive = CreateLocomotive();
            var type = system == "air" ? typeof(AirSinglePipe)
                : system == "vacuum" ? typeof(VacuumSinglePipe) : typeof(ManualBraking);
            var brake = (MSTSBrakeSystem)FormatterServices.GetUninitializedObject(type);
            brake.HandBrakePresent = fitted;
            locomotive.BrakeSystem = brake;
            ParseStartWithParkingBrake(locomotive, "1");
            locomotive.Train.InitializeParkingBrakes();
            Assert.Equal(fitted, brake.GetHandbrakeStatus());
            locomotive.Train.SetHandbrakePercent(0);
            Assert.False(brake.GetHandbrakeStatus());
        }

        [Fact]
        public void StartupOnlyAppliesToLocomotivesWithTheOptionEnabled()
        {
            var locomotive = CreateLocomotive();
            var otherLocomotive = CreateLocomotive();
            var wagon = (MSTSWagon)FormatterServices.GetUninitializedObject(typeof(MSTSWagon));
            var brake = (AirSinglePipe)FormatterServices.GetUninitializedObject(typeof(AirSinglePipe));
            brake.HandBrakePresent = true;
            wagon.BrakeSystem = brake;
            locomotive.Train.Cars.Add(otherLocomotive);
            locomotive.Train.Cars.Add(wagon);
            ParseStartWithParkingBrake(locomotive, "1");
            locomotive.Train.InitializeParkingBrakes();
            Assert.True(locomotive.BrakeSystem.GetHandbrakeStatus());
            Assert.False(otherLocomotive.BrakeSystem.GetHandbrakeStatus());
            Assert.False(wagon.BrakeSystem.GetHandbrakeStatus());
        }

        [Theory]
        [InlineData(0f, 0f)]
        [InlineData(0.01f, 1f)]
        [InlineData(50f, 1f)]
        [InlineData(100f, 1f)]
        public void IndicatorReadsActualHandbrakeApplication(float percentage, float expected)
        {
            var locomotive = CreateLocomotive();
            locomotive.BrakeSystem.SetHandbrakePercent(percentage);
            Assert.Equal(expected, ReadIndicator(locomotive));
            locomotive.BrakeSystem.SetHandbrakePercent(0);
            Assert.Equal(0f, ReadIndicator(locomotive));
        }

        [Fact]
        public void IndividualCarAndTrainWideCommandsUpdateIndicator()
        {
            var locomotive = CreateLocomotive();
            var wagon = (MSTSWagon)FormatterServices.GetUninitializedObject(typeof(MSTSWagon));
            var brake = (AirSinglePipe)FormatterServices.GetUninitializedObject(typeof(AirSinglePipe));
            brake.HandBrakePresent = true;
            wagon.BrakeSystem = brake;
            locomotive.Train.Cars.Add(wagon);
            wagon.BrakeSystem.SetHandbrakePercent(50);
            Assert.False(locomotive.BrakeSystem.GetHandbrakeStatus());
            Assert.Equal(1f, ReadIndicator(locomotive));
            locomotive.Train.SetHandbrakePercent(0);
            Assert.Equal(0f, ReadIndicator(locomotive));
            locomotive.Train.SetHandbrakePercent(100);
            Assert.Equal(1f, ReadIndicator(locomotive));
            locomotive.BrakeSystem.SetHandbrakePercent(0);
            locomotive.Train.Cars.Remove(wagon);
            Assert.Equal(0f, ReadIndicator(locomotive));
        }

        [Fact]
        public void UnfittedOrMissingBrakeStaysOffAndDetachedLocomotiveUsesItsOwnBrake()
        {
            var locomotive = CreateLocomotive(false);
            locomotive.Train.SetHandbrakePercent(100);
            Assert.Equal(0f, ReadIndicator(locomotive));
            locomotive = CreateLocomotive();
            locomotive.Train = null;
            locomotive.BrakeSystem.SetHandbrakePercent(100);
            Assert.Equal(1f, ReadIndicator(locomotive));
            locomotive.BrakeSystem = null;
            Assert.Equal(0f, ReadIndicator(locomotive));
        }
    }
}
