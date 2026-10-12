// COPYRIGHT 2026 by the Open Rails project. GNU GPL version 3 or later.
using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Orts.Formats.Msts;
using Orts.Simulation.RollingStocks;
using ORTS.Common;

namespace Orts.Viewer3D
{
    public static class CabCameraGeometry
    {
        public static Matrix DirectionTransform(Vector3 degrees) => Matrix.CreateFromYawPitchRoll(
            -MathHelper.ToRadians(degrees.X), MathHelper.ToRadians(degrees.Y), -MathHelper.ToRadians(degrees.Z));
    }

    /// <summary>A fixed vehicle camera, never activated as the driver's camera.</summary>
    internal sealed class CabFeedCamera : Camera
    {
        readonly MSTSLocomotive Owner;
        readonly CVCCamera Configuration;
        TrainCar Mount;
        Matrix View;
        readonly Matrix FeedSkyProjection;
        public override TrainCar AttachedCar => Mount;
        public override float NearPlane => 0.05f;
        public override Matrix SkyProjection => FeedSkyProjection;

        public CabFeedCamera(Viewer viewer, MSTSLocomotive owner, CVCCamera configuration) : base(viewer)
        {
            Owner = owner;
            Configuration = configuration;
            FieldOfView = configuration.FieldOfView;
            float aspect = (float)configuration.ImageWidth / configuration.ImageHeight;
            float fov = MathHelper.ToRadians(FieldOfView);
            xnaProjection = Matrix.CreatePerspectiveFieldOfView(fov, aspect, NearPlane, viewer.Settings.ViewingDistance);
            FeedSkyProjection = Matrix.CreatePerspectiveFieldOfView(fov, aspect, NearPlane, SkyPrimitive.RadiusM + 100);
            // Match the actual horizontal field of view, including non-square feeds.
            float horizontal = (float)Math.Atan(Math.Tan(fov / 2) * aspect);
            frustumRightProjected = new Vector3((float)Math.Cos(horizontal), 0, (float)Math.Sin(horizontal));
        }

        public bool UpdateMount()
        {
            var train = Owner.Train;
            if (train == null) return false;
            int ownerIndex = train.Cars.IndexOf(Owner);
            long index = (long)ownerIndex + Configuration.CarOffset;
            if (ownerIndex < 0 || index < 0 || index >= train.Cars.Count) return false;
            Mount = train.Cars[(int)index];
            // Train physics already includes the consist's Flipped rotation in WorldPosition.
            // Applying it again would exchange sides and move the camera to the other vehicle end.
            Matrix transform = Mount.WorldPosition.XNAMatrix;
            var mountPosition = Owner.UsingRearCab ? Configuration.RearCameraPosition ?? Configuration.CameraPosition : Configuration.CameraPosition;
            var mountDirection = Owner.UsingRearCab ? Configuration.RearCameraDirection ?? Configuration.CameraDirection : Configuration.CameraDirection;
            var local = mountPosition * new Vector3(1, 1, -1);
            var eye = Vector3.Transform(local, transform);
            Matrix direction = CabCameraGeometry.DirectionTransform(mountDirection) * transform;
            var forward = Vector3.TransformNormal(Vector3.Forward, direction);
            var up = Vector3.TransformNormal(Vector3.Up, direction);
            cameraLocation = new WorldLocation(Mount.WorldPosition.TileX, Mount.WorldPosition.TileZ,
                eye.X, eye.Y, -eye.Z);
            View = Matrix.CreateLookAt(eye, eye + forward, up);
            return true;
        }

        protected override Matrix GetCameraView() => View;
    }

    /// <summary>GPU resources are created and used only by the render thread.</summary>
    internal sealed class CabCameraFeed
    {
        public readonly MSTSLocomotive Owner;
        public readonly CabFeedCamera Camera;
        public readonly CVCCamera Configuration;
        public RenderTarget2D Image { get; private set; }
        public CabCameraFeed(Viewer viewer, MSTSLocomotive owner, CVCCamera configuration)
        {
            Owner = owner;
            Configuration = configuration;
            Camera = new CabFeedCamera(viewer, owner, configuration);
        }

        public RenderTarget2D GetRenderTarget(GraphicsDevice device)
        {
            if (Image == null || Image.IsDisposed)
                Image = new RenderTarget2D(device, Configuration.ImageWidth, Configuration.ImageHeight,
                    false, SurfaceFormat.Color, DepthFormat.Depth24, 0, RenderTargetUsage.PreserveContents);
            return Image;
        }
    }
}
