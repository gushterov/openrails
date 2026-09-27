// COPYRIGHT 2026 by the Open Rails project.
// Licensed under the GNU General Public License, version 3 or later.

using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using Microsoft.Xna.Framework;
using Orts.Formats.Msts;
using Orts.Simulation;
using Orts.Viewer3D;
using ORTS.Common;
using Xunit;

namespace Tests.RunActivity
{
    // Exercise the CPU particle queue without requiring a graphics device on CI.
    public class PrecipitationTests
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        static FieldInfo Field(string name) => typeof(PrecipitationPrimitive).GetField(name, Private);
        static object Call(PrecipitationPrimitive particles, string method, params object[] args) =>
            typeof(PrecipitationPrimitive).GetMethod(method, Private).Invoke(particles, args);

        static PrecipitationPrimitive CreateQueue()
        {
            var particles = (PrecipitationPrimitive)FormatterServices.GetUninitializedObject(typeof(PrecipitationPrimitive));
            Field("MaxParticles").SetValue(particles, 4);
            Field("Vertices").SetValue(particles, Array.CreateInstance(Field("Vertices").FieldType.GetElementType(), 16));
            particles.Initialize(WeatherType.Snow);
            return particles;
        }

        static void Add(PrecipitationPrimitive particles, float time) => Call(particles, "AddParticle",
            7, -3, new Vector3(1, 12, 3), new Vector3(2, 8, 4), time, 0.25f, new Vector4(0, 5, 0.75f, 0));

        [Fact]
        public void FirstParticleOccupiesFirstUploadedSlot()
        {
            var particles = CreateQueue();
            Add(particles, 10);
            var vertices = (Array)Field("Vertices").GetValue(particles);
            for (int corner = 0; corner < 4; corner++)
            {
                var vertex = vertices.GetValue(corner);
                Vector4 Value(string name) => (Vector4)vertex.GetType().GetField(name).GetValue(vertex);
                Assert.Equal(new Vector4(1, 12, 3, 10), Value("StartPosition_StartTime"));
                Assert.Equal(new Vector4(2, 8, 4, 10 + 4f / 6), Value("EndPosition_EndTime"));
                Assert.Equal(new Vector4(7, -3, corner, 0.25f), Value("TileXZ_Vertex"));
                Assert.Equal(new Vector4(0, 5, 0.75f, 0), Value("Appearance"));
            }
            Assert.True(particles.HasParticlesToRender());
            Assert.Equal(1, Field("FirstFreeParticle").GetValue(particles));
        }

        [Fact]
        public void FullQueueRetiresInOrderAndWaitsForGpuBeforeWrapping()
        {
            var particles = CreateQueue();
            Add(particles, 0);
            Add(particles, 1);
            Add(particles, 2);
            Assert.Equal(0, Call(particles, "GetCountFreeParticles"));
            Field("FirstNewParticle").SetValue(particles, 3); // uploaded for rendering
            Call(particles, "RetireActiveParticles", 1f);
            Assert.Equal(1, Field("FirstActiveParticle").GetValue(particles));
            Call(particles, "FreeRetiredParticles");
            Assert.Equal(0, Call(particles, "GetCountFreeParticles"));
            Field("DrawCounter").SetValue(particles, 2);
            Call(particles, "FreeRetiredParticles");
            Assert.Equal(1, Call(particles, "GetCountFreeParticles"));
            Add(particles, 4);
            Assert.Equal(0, Field("FirstFreeParticle").GetValue(particles));
            Assert.True(particles.HasParticlesToRender());
            Assert.Equal(0, Call(particles, "GetCountFreeParticles"));
        }

        [Fact]
        public void WeatherTransitionReachesBothEndpointSpeeds()
        {
            var particles = CreateQueue();
            var weather = new Weather { PrecipitationLiquidity = 0.5f };
            particles.DynamicUpdate(weather);
            float mixedSpeed = (float)Field("FallVelocity").GetValue(particles);
            weather.PrecipitationLiquidity = 1;
            particles.DynamicUpdate(weather);
            Assert.True((float)Field("FallVelocity").GetValue(particles) > mixedSpeed);
            weather.PrecipitationLiquidity = 0;
            particles.DynamicUpdate(weather);
            Assert.Equal(1f, Field("FallVelocity").GetValue(particles));
            Assert.Equal(0f, Field("Liquidity").GetValue(particles));
        }

        [Theory]
        [InlineData(130)]
        [InlineData(-130)]
        [InlineData(300)]
        public void MovingTrainKeepsFullVisibleVolumePopulated(float speedKph)
        {
            var particles = CreateQueue();
            var speed = speedKph / 3.6f;
            const float elapsed = 1f / 30;
            var origin = new WorldLocation(0, 0, 0, 3, 0);
            Call(particles, "GetEmissionOffset", origin, elapsed, Vector2.Zero);
            var location = new WorldLocation(0, 0, speed * elapsed, 3, 0);
            var offset = (Vector2)Call(particles, "GetEmissionOffset", location, elapsed, Vector2.Zero);
            Assert.Equal(Math.Sign(speed), Math.Sign(offset.X));

            // Check both edges for every age, including the oldest particles. This catches
            // the cab outrunning the leading edge of the precipitation at line speed.
            var constants = BindingFlags.Static | BindingFlags.NonPublic;
            var halfWidth = (float)typeof(PrecipitationPrimitive).GetField("ParticleBoxWidthM", constants).GetRawConstantValue() / 2;
            var visibleDistance = (float)typeof(PrecipitationPrimitive).GetField("ParticleFadeEndM", constants).GetRawConstantValue();
            var duration = (float)typeof(PrecipitationPrimitive).GetField("ParticleDuration", constants).GetRawConstantValue();
            for (int i = 0; i <= 10; i++)
            {
                var relativeCentre = offset.X - speed * duration * i / 10;
                Assert.True(relativeCentre + halfWidth >= visibleDistance);
                Assert.True(relativeCentre - halfWidth <= -visibleDistance);
            }
        }

        [Fact]
        public void EmissionLeadHandlesTileCrossingsWindPauseAndTeleports()
        {
            var particles = CreateQueue();
            var before = new WorldLocation(0, 0, 1018, 3, 1018);
            var after = new WorldLocation(1, 1, -1018, 3, -1018);
            Call(particles, "GetEmissionOffset", before, 1f / 3, Vector2.Zero);
            var offset = (Vector2)Call(particles, "GetEmissionOffset", after, 1f / 3, new Vector2(6, -6));
            Assert.InRange(offset.X, 9.99f, 10.01f);
            Assert.InRange(offset.Y, 9.99f, 10.01f);
            Assert.Equal(Vector2.Zero, Call(particles, "GetEmissionOffset", after, 0f, Vector2.Zero));
            Assert.Equal(Vector2.Zero, Call(particles, "GetEmissionOffset", before, 0.001f, Vector2.Zero));
        }

        [Fact]
        public void PrecipitationHazePreservesClearWeatherAndExistingFog()
        {
            var weather = new Weather { VisibilityM = 2000, PrecipitationLiquidity = 1 };
            Assert.Equal(2000, PrecipitationViewer.GetVisibility(weather, weather.VisibilityM));
            weather.PrecipitationIntensityPPSPM2 = PrecipitationViewer.MaxIntensityPPSPM2 / 2;
            var rain = PrecipitationViewer.GetVisibility(weather, weather.VisibilityM);
            weather.PrecipitationIntensityPPSPM2 = PrecipitationViewer.MaxIntensityPPSPM2;
            var heavyRain = PrecipitationViewer.GetVisibility(weather, weather.VisibilityM);
            weather.PrecipitationLiquidity = 0;
            var snow = PrecipitationViewer.GetVisibility(weather, weather.VisibilityM);
            Assert.True(snow < heavyRain && heavyRain < rain && rain < weather.VisibilityM);
            Assert.InRange(PrecipitationViewer.GetVisibility(weather, 50), 1, 50);
            Assert.Equal(2000, weather.VisibilityM); // Rendering never rewrites activity weather.
            weather.PrecipitationIntensityPPSPM2 = 0;
            Assert.Equal(2000, PrecipitationViewer.GetVisibility(weather, weather.VisibilityM));
        }

        [Theory]
        [InlineData(1024f, 0f, 1, 0, 0, 128)]
        [InlineData(0f, 1024f, 0, 1, 128, 0)]
        [InlineData(-1024f, 0f, 0, 0, 0, 128)]
        [InlineData(0f, -1024f, 0, 0, 128, 0)]
        [InlineData(1024f, 1024f, 1, 1, 0, 0)]
        [InlineData(1023.99994f, 0f, 0, 0, 255, 128)]
        [InlineData(0f, 1023.99994f, 0, 0, 128, 255)]
        public void HeightCacheReadsCorrectBoundaryCellWithoutWarnings(float x, float z, int tileOffsetX, int tileOffsetZ, int cellX, int cellZ)
        {
            var cacheType = typeof(PrecipitationPrimitive).GetNestedType("HeightCache", BindingFlags.NonPublic);
            var cache = Activator.CreateInstance(cacheType, new object[] { 8 });
            var tileType = cacheType.GetNestedType("Tile", BindingFlags.NonPublic);
            var tile = Activator.CreateInstance(tileType, new object[] { 7 + tileOffsetX, -3 + tileOffsetZ, 256 });
            var heights = (float[,])tileType.GetField("Height").GetValue(tile);
            heights[cellX, cellZ] = 123;
            ((IList)cacheType.GetField("Tiles", Private).GetValue(cache)).Add(tile);

            using var messages = new StringWriter();
            using var listener = new TextWriterTraceListener(messages);
            Trace.Listeners.Add(listener);
            try
            {
                // A cache hit needs neither terrain nor scenery. A wrong tile/cell fails
                // instead of hiding the error behind a terrain lookup or index clamp.
                var height = cacheType.GetMethod("GetHeight").Invoke(cache, new object[]
                    { new WorldLocation(7, -3, x, 0, z), null, null });
                Assert.Equal(123f, height);
                Assert.DoesNotContain("precipitation index is out of bounds", messages.ToString());
            }
            finally
            {
                Trace.Listeners.Remove(listener);
            }
        }
    }
}
