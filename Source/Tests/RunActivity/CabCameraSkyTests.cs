// COPYRIGHT 2026 by the Open Rails project. GNU GPL version 3 or later.
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Orts.Viewer3D;
using Orts.Viewer3D.Processes;
using ORTS.Common;
using ORTS.Settings;
using Xunit;
using Game = Orts.Viewer3D.Processes.Game;

namespace Tests.RunActivity
{
    public class CabCameraSkyTests
    {
        static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));

        static void Set(object instance, string name, object value)
        {
            instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                .SetValue(instance, value);
        }

        static (Viewer viewer, Game game) Scene(bool distantMountains)
        {
            var settings = Empty<UserSettings>();
            settings.ViewingFOV = 60;
            settings.ViewingDistance = 2000;
            settings.DistantMountains = distantMountains;
            settings.DistantMountainsViewingDistance = 10000;
            var process = Empty<RenderProcess>();
            Set(process, "<DisplaySize>k__BackingField", new Point(1280, 720));
            var viewer = Empty<Viewer>();
            Set(viewer, "<Settings>k__BackingField", settings);
            Set(viewer, "<RenderProcess>k__BackingField", process);
            var game = Empty<Game>();
            Set(game, "<Settings>k__BackingField", settings);
            Set(game, "<RenderProcess>k__BackingField", process);
            return (viewer, game);
        }

        sealed class TestCamera : Camera
        {
            public TestCamera(Viewer viewer, float fov) : base(viewer)
            {
                FieldOfView = fov;
                ScreenChanged();
            }
            public void Resize(float fov) { FieldOfView = fov; ScreenChanged(); }
            protected override Matrix GetCameraView() => Matrix.Identity;
        }

        // Records the matrix delivered to a real sky-material type without requiring a GPU in unit tests.
        sealed class RecordingSky : MSTSSkyMaterial
        {
            public int Calls;
            public Matrix Projection;
            public RecordingSky() : base(null) { } // Created without invoking the texture-loading constructor.
            public override void SetState(GraphicsDevice graphicsDevice, Material previousMaterial) { }
            public override void ResetState(GraphicsDevice graphicsDevice) { }
            public override bool GetBlending() => false;
            public override void Render(GraphicsDevice graphicsDevice, IEnumerable<RenderItem> items, ref Matrix view, ref Matrix projection)
            {
                Calls++;
                Projection = projection;
            }
        }

        static void Draw(RenderFrame frame, string pass) => typeof(RenderFrame)
            .GetMethod(pass, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(frame, new object[] { null, false });

        [Fact]
        public void ChangingAnotherCameraCannotReplaceDriverSkyOrMountainProjection()
        {
            var scene = Scene(true);
            var driver = new TestCamera(scene.viewer, 60);
            var sky = driver.SkyProjection;
            var mountains = driver.XnaDistantMountainProjection;
            Task.Run(() => new TestCamera(scene.viewer, 95)).Wait();
            Assert.Equal(sky, driver.SkyProjection);
            Assert.Equal(mountains, driver.XnaDistantMountainProjection);
            Assert.NotEqual(default(Matrix), driver.SkyProjection);
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(true, false)]
        [InlineData(false, true)]
        [InlineData(true, true)]
        public void EveryViewDrawsSkyOnceWithItsCapturedProjection(bool distantMountains, bool cameraFeed)
        {
            var scene = Scene(distantMountains);
            var camera = new TestCamera(scene.viewer, cameraFeed ? 65 : 60);
            // Use the offscreen constructor to avoid allocating a window-sized GPU surface.
            var frame = (RenderFrame)Activator.CreateInstance(typeof(RenderFrame),
                BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { scene.game, true }, null);
            Set(frame, "IsCameraFeed", cameraFeed);
            camera.PrepareFrame(frame, ElapsedTime.Zero);
            var captured = camera.SkyProjection;
            var material = Empty<RecordingSky>();
            var matrix = Matrix.Identity;
            frame.AddPrimitive(material, null, RenderPrimitiveGroup.Sky, ref matrix);
            // The updater may resize/change a camera after a render frame has been captured.
            camera.Resize(100);
            if (distantMountains && !cameraFeed) Draw(frame, "DrawSequencesDistantMountains");
            Draw(frame, "DrawSequences");
            Assert.Equal(1, material.Calls);
            Assert.Equal(captured, material.Projection);
            // A dome beyond the scenery viewing distance must still be inside the sky's far plane.
            var dome = Vector4.Transform(new Vector4(0, 0, -SkyPrimitive.RadiusM, 1), material.Projection);
            Assert.InRange(dome.Z / dome.W, 0, 1);
        }
    }
}
