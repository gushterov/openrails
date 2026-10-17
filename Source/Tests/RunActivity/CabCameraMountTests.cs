// COPYRIGHT 2026 by the Open Rails project. GNU GPL version 3 or later.
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using Microsoft.Xna.Framework;
using Orts.Formats.Msts;
using Orts.Simulation.Physics;
using Orts.Simulation.RollingStocks;
using Orts.Viewer3D;
using ORTS.Common;
using ORTS.Settings;
using Xunit;
using Camera = Orts.Viewer3D.Camera;

namespace Tests.RunActivity
{
    public class CabCameraMountTests
    {
        static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));

        static MSTSLocomotive Car(bool cab, bool flipped, int index)
        {
            var car = Empty<MSTSLocomotive>();
            car.CabViewList = new List<CabView>();
            if (cab) car.CabViewList.Add(new CabView(null, new List<ViewPoint> { Empty<ViewPoint>() }, null, CabViewType.Front, false));
            car.Flipped = flipped;
            var matrix = Matrix.Identity;
            if (flipped) matrix.M11 = matrix.M33 = -1;
            matrix *= Matrix.CreateTranslation(index * 10, 0, index * 100);
            car.WorldPosition = new WorldPosition { XNAMatrix = matrix };
            return car;
        }

        static List<TrainCar> TwoUnits() => new List<TrainCar> {
            Car(true, false, 0), Car(false, false, 1), Car(false, true, 2), Car(true, true, 3),
            Car(true, false, 4), Car(false, false, 5), Car(false, true, 6), Car(true, true, 7) };

        static Camera Feed(List<TrainCar> cars, int ownerIndex, CabCameraMount mount, int offset, out bool available)
        {
            var settings = Empty<UserSettings>();
            settings.ViewingDistance = 2000;
            settings.ViewingFOV = 60;
            var viewer = Empty<Viewer>();
            typeof(Viewer).GetField("<Settings>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(viewer, settings);
            var owner = (MSTSLocomotive)cars[ownerIndex];
            owner.Train = Empty<Train>();
            owner.Train.Cars = cars;
            var configuration = Empty<CVCCamera>();
            configuration.CameraMount = mount;
            configuration.CarOffset = offset;
            configuration.CameraPosition = new Vector3(3, 1, 7);
            configuration.RearCameraPosition = new Vector3(99, 99, 99);
            configuration.RearCameraDirection = new Vector3(180, 0, 0);
            configuration.FieldOfView = 60;
            configuration.ImageWidth = 160;
            configuration.ImageHeight = 120;
            var type = typeof(Camera).Assembly.GetType("Orts.Viewer3D.CabFeedCamera");
            var camera = (Camera)Activator.CreateInstance(type, new object[] { viewer, owner, configuration });
            available = (bool)type.GetMethod("UpdateMount").Invoke(camera, null);
            return camera;
        }

        [Theory]
        [InlineData(0, 3, false)]
        [InlineData(3, 0, false)]
        [InlineData(4, 7, false)]
        [InlineData(7, 4, false)]
        [InlineData(0, 3, true)]
        [InlineData(3, 0, true)]
        [InlineData(4, 7, true)]
        [InlineData(7, 4, true)]
        public void OppositeCabStaysInSameUnitAfterCabSwitchAndConsistReversal(int owner, int target, bool reversed)
        {
            var cars = TwoUnits();
            if (reversed)
            {
                cars.Reverse();
                foreach (var car in cars) car.Flipped = !car.Flipped;
                owner = 7 - owner;
                target = 7 - target;
            }
            var camera = Feed(cars, owner, CabCameraMount.OppositeCab, 100, out bool available);
            Assert.True(available);
            Assert.Same(cars[target], camera.AttachedCar);
        }

        [Fact]
        public void OppositeCabUsesTargetOrientationAndFrontCoordinatesRegardlessOfItsCabState()
        {
            var cars = TwoUnits();
            ((MSTSLocomotive)cars[0]).UsingRearCab = true;
            ((MSTSLocomotive)cars[3]).UsingRearCab = true;
            var camera = Feed(cars, 0, CabCameraMount.OppositeCab, 0, out bool available);
            Assert.True(available);
            Assert.InRange(Vector3.Distance(new Vector3(27, 1, -307), camera.Location), 0, .0001f);
            camera.PrepareFrame(Empty<RenderFrame>(), ElapsedTime.Zero);
            Assert.InRange(Vector3.Distance(Vector3.Backward, Matrix.Invert(camera.XnaView).Forward), 0, .0001f);
        }

        [Fact]
        public void OppositeCabStopsAtFirstCabEvenWhenItFacesTheWrongWay()
        {
            var cars = TwoUnits();
            cars[1] = Car(true, false, 1);
            Feed(cars, 0, CabCameraMount.OppositeCab, 0, out bool available);
            Assert.False(available);
        }

        [Fact]
        public void MissingOppositeCabDoesNotFallBackToCurrentCar()
        {
            var cars = new List<TrainCar> { Car(true, false, 0), Car(false, false, 1) };
            Feed(cars, 0, CabCameraMount.OppositeCab, 0, out bool available);
            Assert.False(available);
        }

        [Theory]
        [InlineData(0, 3, 3, true)]
        [InlineData(3, -3, 0, true)]
        [InlineData(0, -1, 0, false)]
        [InlineData(0, int.MaxValue, 0, false)]
        public void ExistingFixedOffsetsStillWork(int owner, int offset, int target, bool expectedAvailable)
        {
            var cars = TwoUnits();
            var camera = Feed(cars, owner, CabCameraMount.CarOffset, offset, out bool available);
            Assert.Equal(expectedAvailable, available);
            if (available) Assert.Same(cars[target], camera.AttachedCar);
        }
    }
}
