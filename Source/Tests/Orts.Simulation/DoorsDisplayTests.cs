// COPYRIGHT 2026 by the Open Rails project. GNU GPL version 3 or later.
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using Orts.Formats.Msts;
using Orts.Simulation;
using Orts.Simulation.Physics;
using Orts.Simulation.RollingStocks;
using Orts.Simulation.RollingStocks.SubSystems;
using Xunit;

namespace Tests.Orts.Simulation
{
    public class DoorsDisplayTests
    {
        static T Uninitialized<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));

        static MSTSLocomotive CreateLocomotive(double gameTime)
        {
            var simulator = Uninitialized<Simulator>();
            simulator.GameTime = gameTime;
            var locomotive = Uninitialized<MSTSLocomotive>();
            typeof(TrainCar).GetField(nameof(TrainCar.Simulator)).SetValue(locomotive, simulator);
            locomotive.Doors = new Doors(locomotive);
            locomotive.Train = Uninitialized<Train>();
            locomotive.Train.Cars = new List<TrainCar> { locomotive };
            return locomotive;
        }

        static void SetState(Door door, DoorState state) =>
            typeof(Door).GetProperty(nameof(Door.State)).SetValue(door, state);

        static CVCMultiStateDisplay ReadControl(int frames, bool flashOnClose)
        {
            var path = Path.GetTempFileName();
            try
            {
                var states = "";
                for (int i = 0; i < frames; i++)
                    states += $"State ( Style ( 0 ) SwitchVal ( {i} ) ) ";
                File.WriteAllText(path, $@"
                    Tr_CabViewFile ( CabViewControls ( 1
                        MultiStateDisplay (
                            Type ( DOORS_DISPLAY MULTI_STATE_DISPLAY )
                            Position ( 100 100 32 32 ) Graphic ( doors.ace )
                            {(flashOnClose ? "Feature ( FlashOnClose )" : "")}
                            States ( {frames} {frames} 1 {states} )
                        )
                    ) )");
                var cab = new CabViewFile(path, Path.GetDirectoryName(path));
                return Assert.IsType<CVCMultiStateDisplay>(Assert.Single(cab.CabViewControls));
            }
            finally { File.Delete(path); }
        }

        [Theory]
        [InlineData(DoorState.Closed, 0.0, 0f)]
        [InlineData(DoorState.Closed, 0.8, 0f)]
        [InlineData(DoorState.Opening, 0.0, 1f)]
        [InlineData(DoorState.Opening, 0.8, 1f)]
        [InlineData(DoorState.Open, 0.0, 1f)]
        [InlineData(DoorState.Open, 0.8, 1f)]
        [InlineData(DoorState.Closing, 0.0, 2f)]
        [InlineData(DoorState.Closing, 0.699, 2f)]
        [InlineData(DoorState.Closing, 0.7, 3f)]
        [InlineData(DoorState.Closing, 0.999, 3f)]
        [InlineData(DoorState.Closing, 1.0, 2f)]
        public void FourFramesUseDedicatedClosingImagesWithOrWithoutFeature(DoorState state, double time, float expected)
        {
            var locomotive = CreateLocomotive(time);
            SetState(locomotive.Doors.RightDoor, state);
            Assert.Equal(expected, locomotive.GetDataOf(ReadControl(4, false)));
            Assert.Equal(expected, locomotive.GetDataOf(ReadControl(4, true)));
        }

        [Theory]
        [InlineData(DoorState.Closed, 0.0, 0f, 0f)]
        [InlineData(DoorState.Opening, 0.8, 1f, 1f)]
        [InlineData(DoorState.Open, 0.8, 1f, 1f)]
        [InlineData(DoorState.Closing, 0.0, 1f, 1f)]
        [InlineData(DoorState.Closing, 0.7, 1f, 0f)]
        [InlineData(DoorState.Closing, 1.0, 1f, 1f)]
        public void TwoFramesRetainExistingBehavior(DoorState state, double time, float steady, float flashing)
        {
            var locomotive = CreateLocomotive(time);
            SetState(locomotive.Doors.LeftDoor, state);
            Assert.Equal(steady, locomotive.GetDataOf(ReadControl(2, false)));
            Assert.Equal(flashing, locomotive.GetDataOf(ReadControl(2, true)));
        }

        [Fact]
        public void FourFramesWaitForEveryDoorInTrainToClose()
        {
            var locomotive = CreateLocomotive(0.8);
            var wagon = Uninitialized<MSTSWagon>();
            wagon.Doors = new Doors(wagon);
            locomotive.Train.Cars.Add(wagon);
            var control = ReadControl(4, false);

            SetState(locomotive.Doors.LeftDoor, DoorState.Open);
            SetState(wagon.Doors.RightDoor, DoorState.Closing);
            Assert.Equal(1f, locomotive.GetDataOf(control));
            SetState(locomotive.Doors.LeftDoor, DoorState.Closed);
            Assert.Equal(3f, locomotive.GetDataOf(control));
            SetState(wagon.Doors.RightDoor, DoorState.Closed);
            Assert.Equal(0f, locomotive.GetDataOf(control));
        }
    }
}
