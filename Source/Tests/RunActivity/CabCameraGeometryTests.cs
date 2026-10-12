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
    public class CabCameraGeometryTests
    {
        static void Near(Vector3 expected, Vector3 actual) => Assert.InRange(Vector3.Distance(expected, actual), 0, 0.0001f);

        [Fact]
        public void ZeroLooksForwardAndPositiveYawLooksRight()
        {
            Near(Vector3.Forward, Vector3.TransformNormal(Vector3.Forward, CabCameraGeometry.DirectionTransform(Vector3.Zero)));
            Near(Vector3.Right, Vector3.TransformNormal(Vector3.Forward, CabCameraGeometry.DirectionTransform(new Vector3(90, 0, 0))));
            Near(Vector3.Left, Vector3.TransformNormal(Vector3.Forward, CabCameraGeometry.DirectionTransform(new Vector3(-90, 0, 0))));
        }

        [Fact]
        public void PositivePitchLooksUpAndRollTurnsImageClockwise()
        {
            Near(Vector3.Up, Vector3.TransformNormal(Vector3.Forward, CabCameraGeometry.DirectionTransform(new Vector3(0, 90, 0))));
            Near(Vector3.Right, Vector3.TransformNormal(Vector3.Up, CabCameraGeometry.DirectionTransform(new Vector3(0, 0, 90))));
        }

        [Theory]
        [InlineData(false, false, 13, 21, -23, -1)]
        [InlineData(true, false, 7, 21, -37, 1)]
        [InlineData(false, true, 7, 21, -37, 1)]
        [InlineData(true, true, 13, 21, -23, -1)]
        public void MountUsesVehicleOrientationOnceAndSelectsOverridesOnlyForActualRearCab(
            bool flipped, bool rearCab, float x, float y, float z, float forwardZ)
        {
            // Match Train.ComputePosition: flip the vehicle matrix before applying its world position.
            var matrix = Matrix.Identity;
            if (flipped) matrix.M11 = matrix.M33 = -1;
            matrix *= Matrix.CreateTranslation(10, 20, 30);
            var camera = FeedCamera(matrix, flipped, rearCab);
            Near(new Vector3(x, y, z), camera.Location);
            Near(new Vector3(0, 0, forwardZ), Matrix.Invert(camera.XnaView).Forward);
        }

        [Fact]
        public void MountFollowsVehicleRotationOnCurves()
        {
            var camera = FeedCamera(Matrix.CreateRotationY(MathHelper.PiOver2), false, false);
            Near(Vector3.Left, Matrix.Invert(camera.XnaView).Forward);
        }

        static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));

        static Camera FeedCamera(Matrix worldMatrix, bool flipped, bool rearCab)
        {
            var settings = Empty<UserSettings>();
            settings.ViewingDistance = 2000;
            settings.ViewingFOV = 60;
            var viewer = Empty<Viewer>();
            typeof(Viewer).GetField("<Settings>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(viewer, settings);
            var owner = Empty<MSTSLocomotive>();
            owner.Flipped = flipped;
            owner.UsingRearCab = rearCab;
            owner.WorldPosition = new WorldPosition { XNAMatrix = worldMatrix };
            owner.Train = Empty<Train>();
            owner.Train.Cars = new List<TrainCar> { owner };
            var configuration = Empty<CVCCamera>();
            configuration.FieldOfView = 60;
            configuration.ImageWidth = 320;
            configuration.ImageHeight = 240;
            configuration.CameraPosition = new Vector3(3, 1, 7);
            configuration.RearCameraPosition = new Vector3(-3, 1, -7);
            configuration.RearCameraDirection = new Vector3(180, 0, 0);
            var type = typeof(Camera).Assembly.GetType("Orts.Viewer3D.CabFeedCamera");
            var camera = (Camera)Activator.CreateInstance(type, new object[] { viewer, owner, configuration });
            Assert.True((bool)type.GetMethod("UpdateMount").Invoke(camera, null));
            camera.PrepareFrame(Empty<RenderFrame>(), ElapsedTime.Zero);
            return camera;
        }
    }
}
