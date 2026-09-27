// COPYRIGHT 2009 - 2023 by the Open Rails project.
//
// This file is part of Open Rails.
//
// Open Rails is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// Open Rails is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with Open Rails.  If not, see <http://www.gnu.org/licenses/>.

// This file is the responsibility of the 3D & Environment Team.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ORTS.Common;
using Orts.Simulation;

namespace Orts.Viewer3D
{
    public class PrecipitationViewer
    {
        public const float MinIntensityPPSPM2 = 0;

        public const float MaxIntensityPPSPM2 = 0.015f;

        // Combine the route's fog with the additional loss of contrast through precipitation.
        // Keep this in the renderer so activity weather, saves and multiplayer values are unchanged.
        public static float GetVisibility(Weather weather, float visibilityM)
        {
            var intensity = MathHelper.Clamp(weather.PrecipitationIntensityPPSPM2 / MaxIntensityPPSPM2, 0, 1);
            if (intensity == 0)
                return visibilityM;

            var precipitationVisibility = MathHelper.Lerp(300, 900, MathHelper.Clamp(weather.PrecipitationLiquidity, 0, 1));
            return 1 / (1 / Math.Max(1, visibilityM) + intensity / precipitationVisibility);
        }

        readonly Viewer Viewer;
        readonly Weather Weather;

        readonly Material Material;
        readonly PrecipitationPrimitive Precipitation;

        public PrecipitationViewer(Viewer viewer)
        {
            Viewer = viewer;
            Weather = viewer.Simulator.Weather;

            Material = viewer.MaterialManager.Load("Precipitation");
            Precipitation = new PrecipitationPrimitive(Viewer.GraphicsDevice);

            Reset();
        }

        public void PrepareFrame(RenderFrame frame, ElapsedTime elapsedTime)
        {
            var gameTime = (float)Viewer.Simulator.GameTime;
            Precipitation.DynamicUpdate(Weather);
            Precipitation.Update(gameTime, elapsedTime, Weather.PrecipitationIntensityPPSPM2, Viewer);

            // Note: This is quite a hack. We ideally should be able to pass this through RenderItem somehow.
            var xnaWorldLocation = Matrix.Identity;
            xnaWorldLocation.M11 = gameTime;
            xnaWorldLocation.M21 = Viewer.Camera.TileX;
            xnaWorldLocation.M22 = Viewer.Camera.TileZ;

            frame.AddPrimitive(Material, Precipitation, RenderPrimitiveGroup.Precipitation, ref xnaWorldLocation);
        }

        public void Reset()
        {
            var gameTime = (float)Viewer.Simulator.GameTime;
            Precipitation.Initialize(Viewer.Simulator.WeatherType);
            Precipitation.DynamicUpdate(Weather);

            // Camera is null during first initialisation.
            if (Viewer.Camera != null)
            {
                Precipitation.Update(gameTime, null, Weather.PrecipitationIntensityPPSPM2, Viewer);
            }
        }

        [CallOnThread("Loader")]
        internal void Mark()
        {
            Material.Mark();
        }
    }

    public class PrecipitationPrimitive : RenderPrimitive
    {
        // http://www-das.uwyo.edu/~geerts/cwx/notes/chap09/hydrometeor.html
        // "Rain  1.8 - 2.2mm  6.1 - 6.9m/s"
        const float RainVelocityMpS = 6.9f;

        // "Snow flakes of any size falls at about 1 m/s"
        const float SnowVelocityMpS = 1.0f;

        // Accelerate the visual motion without thinning out the precipitation. Shorten
        // lifetimes by the same factor so travel distances and visible density stay stable.
        const float ParticleSpeedFactor = 6;

        // Replenish the whole volume, not just its ceiling. Short, fading lifetimes keep
        // slow snow around a moving train without dragging the flakes with the camera.
        const float ParticleDuration = 4 / ParticleSpeedFactor;
        const float ParticleBoxLengthM = 1000;
        const float ParticleBoxWidthM = 1000;
        const float ParticleBoxHeightM = 48;
        const float ParticleDensity = 1.5f;
        const float SnowDensityMultiplier = 1.0f;
        // Scale the visible range with the volume, leaving a margin for camera movement.
        internal const float ParticleFadeStartM = ParticleBoxLengthM / 4;
        internal const float ParticleFadeEndM = ParticleBoxLengthM * 5 / 12;

        const int IndicesPerParticle = 6;
        const int VerticiesPerParticle = 4;
        const int PrimitivesPerParticle = 2;

        readonly int MaxParticles;
        readonly ParticleVertex[] Vertices;
        readonly VertexDeclaration VertexDeclaration;
        readonly int VertexStride;
        readonly DynamicVertexBuffer VertexBuffer;
        readonly IndexBuffer IndexBuffer;

        struct ParticleVertex
        {
            public Vector4 StartPosition_StartTime;
            public Vector4 EndPosition_EndTime;
            public Vector4 TileXZ_Vertex;
            public Vector4 Appearance;

            public static readonly VertexElement[] VertexElements =
            {
                new VertexElement(0, VertexElementFormat.Vector4, VertexElementUsage.Position, 0),
                new VertexElement(16, VertexElementFormat.Vector4, VertexElementUsage.Position, 1),
                new VertexElement(16 + 16, VertexElementFormat.Vector4, VertexElementUsage.Position, 2),
                new VertexElement(48, VertexElementFormat.Vector4, VertexElementUsage.TextureCoordinate, 0),
            };

            public static int SizeInBytes = sizeof(float) * 16;
        }

        float Liquidity;
        float FallVelocity;
        bool Initialized;
        bool HasCameraLocation;
        WorldLocation LastCameraLocation;
        HeightCache Heights;

        // Particle buffer goes like this:
        //   +--active>-----new>--+
        //   |                    |
        //   +--<retired---<free--+
        int FirstActiveParticle;
        int FirstNewParticle;
        int FirstFreeParticle;
        int FirstRetiredParticle;

        float ParticlesToEmit;
        float TimeParticlesLastEmitted;
        int DrawCounter;

        public PrecipitationPrimitive(GraphicsDevice graphicsDevice)
        {
            // Slower snow needs more particles to fill the same volume at a given intensity.
            MaxParticles = (int)(PrecipitationViewer.MaxIntensityPPSPM2 * ParticleBoxLengthM * ParticleBoxWidthM * ParticleBoxHeightM * ParticleDensity * SnowDensityMultiplier / SnowVelocityMpS) + 1;
            // Keep the existing Reach fallback usable on older graphics devices.
            if (graphicsDevice.GraphicsProfile == GraphicsProfile.Reach)
                MaxParticles = Math.Min(MaxParticles, ushort.MaxValue / VerticiesPerParticle);

            Vertices = new ParticleVertex[MaxParticles * VerticiesPerParticle];
            VertexDeclaration = new VertexDeclaration(ParticleVertex.SizeInBytes, ParticleVertex.VertexElements);
            VertexStride = Marshal.SizeOf(typeof(ParticleVertex));
            VertexBuffer = new DynamicVertexBuffer(graphicsDevice, VertexDeclaration, MaxParticles * VerticiesPerParticle, BufferUsage.WriteOnly);
            IndexBuffer = InitIndexBuffer(graphicsDevice, MaxParticles);

            Heights = new HeightCache(8);

            // This Trace command is used to show how much memory is used.
            Trace.TraceInformation(string.Format("Allocation for {0:N0} particles:\n\n  {1,13:N0} B RAM vertex data\n  {2,13:N0} B RAM index data (temporary)\n  {1,13:N0} B VRAM DynamicVertexBuffer\n  {2,13:N0} B VRAM IndexBuffer", MaxParticles, Marshal.SizeOf(typeof(ParticleVertex)) * MaxParticles * VerticiesPerParticle, sizeof(uint) * MaxParticles * IndicesPerParticle));
        }

        void VertexBuffer_ContentLost()
        {
            VertexBuffer.SetData(0, Vertices, 0, Vertices.Length, VertexStride, SetDataOptions.NoOverwrite);
        }

        static IndexBuffer InitIndexBuffer(GraphicsDevice graphicsDevice, int numParticles)
        {
            var numIndices = numParticles * IndicesPerParticle;
            var indices = new int[numIndices];
            var index = 0;
            for (var i = 0; i < numIndices; i += IndicesPerParticle)
            {
                indices[i] = index;
                indices[i + 1] = index + 1;
                indices[i + 2] = index + 2;

                indices[i + 3] = index + 2;
                indices[i + 4] = index + 3;
                indices[i + 5] = index;

                index += VerticiesPerParticle;
            }

            var useShortIndices = numParticles * VerticiesPerParticle <= ushort.MaxValue;
            var indexBuffer = new IndexBuffer(graphicsDevice, useShortIndices ? typeof(ushort) : typeof(int), numIndices, BufferUsage.WriteOnly);
            if (useShortIndices)
                indexBuffer.SetData(Array.ConvertAll(indices, indexValue => (ushort)indexValue));
            else
                indexBuffer.SetData(indices);
            return indexBuffer;
        }

        void RetireActiveParticles(float currentTime)
        {
            while (FirstActiveParticle != FirstNewParticle)
            {
                var vertex = FirstActiveParticle * VerticiesPerParticle;
                var expiry = Vertices[vertex].EndPosition_EndTime.W;

                // Stop as soon as we find the first particle which hasn't expired.
                if (expiry > currentTime)
                {
                    break;
                }

                // Expire particle.
                Vertices[vertex].StartPosition_StartTime.W = (float)DrawCounter;
                FirstActiveParticle = (FirstActiveParticle + 1) % MaxParticles;
            }
        }

        void FreeRetiredParticles()
        {
            while (FirstRetiredParticle != FirstActiveParticle)
            {
                var vertex = FirstRetiredParticle * VerticiesPerParticle;
                var age = DrawCounter - (int)Vertices[vertex].StartPosition_StartTime.W;

                // Stop as soon as we find the first expired particle which hasn't been expired for at least 2 'ticks'.
                if (age < 2)
                {
                    break;
                }

                FirstRetiredParticle = (FirstRetiredParticle + 1) % MaxParticles;
            }
        }

        int GetCountFreeParticles()
        {
            var nextFree = (FirstFreeParticle + 1) % MaxParticles;

            if (nextFree <= FirstRetiredParticle)
            {
                return FirstRetiredParticle - nextFree;
            }

            return (MaxParticles - nextFree) + FirstRetiredParticle;
        }

        public void Initialize(Orts.Formats.Msts.WeatherType weather)
        {
            Liquidity = weather == Orts.Formats.Msts.WeatherType.Snow ? 0 : 1;
            FallVelocity = MathHelper.Lerp(SnowVelocityMpS, RainVelocityMpS, Liquidity);
            Initialized = false;
            HasCameraLocation = false;
            FirstActiveParticle = FirstNewParticle = FirstFreeParticle = FirstRetiredParticle = 0;
            ParticlesToEmit = TimeParticlesLastEmitted = 0;
            DrawCounter = 0;
        }

        public void DynamicUpdate(Weather weather)
        {
            Liquidity = MathHelper.Clamp(weather.PrecipitationLiquidity, 0, 1);
            FallVelocity = MathHelper.Lerp(SnowVelocityMpS, RainVelocityMpS, Liquidity);
        }

        public void Update(float currentTime, ElapsedTime elapsedTime, float particlesPerSecondPerM2, Viewer viewer)
        {
            var tiles = viewer.Tiles;
            var scenery = viewer.World.Scenery;
            var worldLocation = viewer.Camera.CameraWorldLocation;
            // These are world-space particles; camera movement is already in the view matrix.
            var particleDirection2D = viewer.Simulator.Weather.WindInstantaneousDirection * viewer.Simulator.Weather.WindInstantaneousSpeedMpS;
            var particleDirection3D = new Vector3(particleDirection2D.X, 0, particleDirection2D.Y);
            var emissionOffset = GetEmissionOffset(worldLocation, elapsedTime?.ClockSeconds ?? 0, particleDirection2D);
            var emissionRate = Math.Min(MathHelper.Clamp(particlesPerSecondPerM2, 0, PrecipitationViewer.MaxIntensityPPSPM2)
                * ParticleBoxLengthM * ParticleBoxWidthM * ParticleBoxHeightM * ParticleDensity
                * MathHelper.Lerp(SnowDensityMultiplier, 3, Liquidity) / FallVelocity,
                MaxParticles - 1) / ParticleDuration;

            if (!Initialized)
            {
                Initialized = true;
                TimeParticlesLastEmitted = currentTime - ParticleDuration;
                ParticlesToEmit += ParticleDuration * emissionRate;
            }
            else
            {
                RetireActiveParticles(currentTime);
                FreeRetiredParticles();

                ParticlesToEmit += Math.Min(elapsedTime?.ClockSeconds ?? 0, ParticleDuration) * emissionRate;
            }

            var numParticlesAdded = 0;
            var numToBeEmitted = (int)ParticlesToEmit;
            var numCanBeEmitted = GetCountFreeParticles();
            var numToEmit = Math.Min(numToBeEmitted, numCanBeEmitted);

            for (var i = 0; i < numToEmit; i++)
            {
                var temp = new WorldLocation(worldLocation.TileX, worldLocation.TileZ,
                    worldLocation.Location.X + emissionOffset.X + (float)((Viewer.Random.NextDouble() - 0.5) * ParticleBoxWidthM),
                    0, worldLocation.Location.Z + emissionOffset.Y + (float)((Viewer.Random.NextDouble() - 0.5) * ParticleBoxLengthM));
                temp.Location.Y = Heights.GetHeight(temp, tiles, scenery);
                var position = new WorldPosition(temp);

                var time = MathHelper.Lerp(Math.Max(TimeParticlesLastEmitted, currentTime - ParticleDuration), currentTime, (float)i / numToEmit);
                var seed = (float)Viewer.Random.NextDouble();
                var variation = (float)Viewer.Random.NextDouble();
                var velocity = FallVelocity * ParticleSpeedFactor * MathHelper.Lerp(0.75f, 1.25f, seed);
                var start = position.XNAMatrix.Translation;
                var groundHeight = start.Y;
                start.Y = Math.Max(groundHeight, worldLocation.Location.Y - ParticleBoxHeightM / 2)
                    + (float)Viewer.Random.NextDouble() * ParticleBoxHeightM;
                var end = start + particleDirection3D * ParticleDuration - Vector3.UnitY * velocity * ParticleDuration;

                AddParticle(position.TileX, position.TileZ, start, end, time, seed, new Vector4(Liquidity, groundHeight, variation, 0));
                ParticlesToEmit--;
                numParticlesAdded++;
            }

            if (numParticlesAdded > 0)
            {
                TimeParticlesLastEmitted = currentTime;
            }

            ParticlesToEmit -= (int)ParticlesToEmit;
        }

        Vector2 GetEmissionOffset(WorldLocation cameraLocation, float elapsedSeconds, Vector2 wind)
        {
            var cameraVelocity = Vector2.Zero;
            if (HasCameraLocation && elapsedSeconds > 0)
            {
                var movement = WorldLocation.GetDistance(LastCameraLocation, cameraLocation);
                var velocity = new Vector2(movement.X, movement.Z) / elapsedSeconds;
                // Camera changes/teleports must not displace the entire weather volume.
                if (velocity.LengthSquared() <= 150 * 150)
                    cameraVelocity = velocity;
            }
            LastCameraLocation = cameraLocation;
            HasCameraLocation = true;

            // Centre the population at its mean age. Only the emission position follows
            // the camera: existing particles continue to move naturally in world space.
            // WorldLocation has the opposite Z sign to the renderer's wind vector.
            var offset = (cameraVelocity - new Vector2(wind.X, -wind.Y)) * (ParticleDuration / 2);
            var margin = Math.Min(ParticleBoxWidthM, ParticleBoxLengthM) / 2 - ParticleFadeEndM;
            var length = offset.Length();
            return length > margin ? offset * (margin / length) : offset;
        }

        void AddParticle(int tileX, int tileZ, Vector3 start, Vector3 end, float time, float seed, Vector4 appearance)
        {
            // FirstFreeParticle is the next writable slot, not the last written slot.
            var vertex = FirstFreeParticle * VerticiesPerParticle;
            for (var j = 0; j < VerticiesPerParticle; j++)
            {
                Vertices[vertex + j].StartPosition_StartTime = new Vector4(start, time);
                Vertices[vertex + j].EndPosition_EndTime = new Vector4(end, time + ParticleDuration);
                Vertices[vertex + j].TileXZ_Vertex = new Vector4(tileX, tileZ, j, seed);
                Vertices[vertex + j].Appearance = appearance;
            }
            FirstFreeParticle = (FirstFreeParticle + 1) % MaxParticles;
        }

        void AddNewParticlesToVertexBuffer()
        {
            if (FirstNewParticle < FirstFreeParticle)
            {
                var numParticlesToAdd = FirstFreeParticle - FirstNewParticle;
                VertexBuffer.SetData(FirstNewParticle * VertexStride * VerticiesPerParticle, Vertices, FirstNewParticle * VerticiesPerParticle, numParticlesToAdd * VerticiesPerParticle, VertexStride, SetDataOptions.NoOverwrite);
            }
            else
            {
                var numParticlesToAddAtEnd = MaxParticles - FirstNewParticle;
                VertexBuffer.SetData(FirstNewParticle * VertexStride * VerticiesPerParticle, Vertices, FirstNewParticle * VerticiesPerParticle, numParticlesToAddAtEnd * VerticiesPerParticle, VertexStride, SetDataOptions.NoOverwrite);
                if (FirstFreeParticle > 0)
                {
                    VertexBuffer.SetData(0, Vertices, 0, FirstFreeParticle * VerticiesPerParticle, VertexStride, SetDataOptions.NoOverwrite);
                }
            }

            FirstNewParticle = FirstFreeParticle;
        }

        public bool HasParticlesToRender()
        {
            return FirstActiveParticle != FirstFreeParticle;
        }

        public override void Draw(GraphicsDevice graphicsDevice)
        {
            if (VertexBuffer.IsContentLost)
            {
                VertexBuffer_ContentLost();
            }

            if (FirstNewParticle != FirstFreeParticle)
            {
                AddNewParticlesToVertexBuffer();
            }

            if (HasParticlesToRender())
            {
                graphicsDevice.Indices = IndexBuffer;
                graphicsDevice.SetVertexBuffer(VertexBuffer);

                if (FirstActiveParticle < FirstFreeParticle)
                {
                    var numParticles = FirstFreeParticle - FirstActiveParticle;
                    graphicsDevice.DrawIndexedPrimitives(PrimitiveType.TriangleList, baseVertex: 0, startIndex: FirstActiveParticle * IndicesPerParticle, primitiveCount: numParticles * PrimitivesPerParticle);
                }
                else
                {
                    var numParticlesAtEnd = MaxParticles - FirstActiveParticle;
                    if (numParticlesAtEnd > 0)
                    {
                        graphicsDevice.DrawIndexedPrimitives(PrimitiveType.TriangleList, baseVertex: 0, startIndex: FirstActiveParticle * IndicesPerParticle, primitiveCount: numParticlesAtEnd * PrimitivesPerParticle);
                    }

                    if (FirstFreeParticle > 0)
                    {
                        graphicsDevice.DrawIndexedPrimitives(PrimitiveType.TriangleList, baseVertex: 0, startIndex: 0, primitiveCount: FirstFreeParticle * PrimitivesPerParticle);
                    }
                }
            }

            DrawCounter++;
        }

        class HeightCache
        {
            const int TileCount = 10;

            readonly int BlockSize;
            readonly int Divisions;
            readonly List<Tile> Tiles = new List<Tile>();

            public HeightCache(int blockSize)
            {
                BlockSize = blockSize;
                Divisions = (int)Math.Round(2048f / blockSize);
            }

            public float GetHeight(WorldLocation location, TileManager tiles, SceneryDrawer scenery)
            {
                location.Normalize();

                // First, ensure we have the tile in question cached.
                Tile tile = null;
                // This runs for every emitted particle; avoid allocating a LINQ closure.
                for (var i = 0; i < Tiles.Count; i++)
                {
                    if (Tiles[i].TileX == location.TileX && Tiles[i].TileZ == location.TileZ)
                    {
                        tile = Tiles[i];
                        break;
                    }
                }
                if (tile == null)
                {
                    Tiles.Add(tile = new Tile(location.TileX, location.TileZ, Divisions));
                }

                // Remove excess entries.
                if (Tiles.Count > TileCount)
                {
                    Tiles.RemoveAt(0);
                }

                // Add in double precision: the float immediately below 1024 would
                // otherwise round up to 2048 during the addition, producing index 256.
                var x = (int)((location.Location.X + 1024.0) / BlockSize);
                var z = (int)((location.Location.Z + 1024.0) / BlockSize);

                // Trace the case where x or z are out of bounds and fix
                var xSize = tile.Height.GetLength(0);
                var zSize = tile.Height.GetLength(1);
                if (x < 0 || x >= xSize || z < 0 || z >= zSize)
                {
                    Trace.TraceWarning(
                        "At least one precipitation index is out of bounds:  x = {0}, z = {1}, Location.X = {2}, Location.Z = {3}, BlockSize = {4}, HeightDimensionX = {5}, HeightDimensionZ = {6} ; fixing it",
                        x,
                        z,
                        location.Location.X,
                        location.Location.Z,
                        BlockSize,
                        xSize,
                        zSize);

                    if (x >= xSize)
                    {
                        x = xSize - 1;
                    }

                    if (z >= zSize)
                    {
                        z = zSize - 1;
                    }

                    if (x < 0)
                    {
                        x = 0;
                    }

                    if (z < 0)
                    {
                        z = 0;
                    }
                }

                // If we don't have it cached, load it.
                if (tile.Height[x, z] == float.MinValue)
                {
                    var position = new WorldLocation(location.TileX, location.TileZ, ((x + 0.5f) * BlockSize) - 1024, 0, ((z + 0.5f) * BlockSize) - 1024);
                    tile.Height[x, z] = Math.Max(tiles.GetElevation(position), scenery.GetBoundingBoxTop(position, BlockSize));
                    tile.Used++;
                }

                return tile.Height[x, z];
            }

            [DebuggerDisplay("Tile = {TileX},{TileZ} Used = {Used}")]
            class Tile
            {
                public readonly int TileX;
                public readonly int TileZ;
                public readonly float[,] Height;
                public int Used;

                public Tile(int tileX, int tileZ, int divisions)
                {
                    TileX = tileX;
                    TileZ = tileZ;
                    Height = new float[divisions, divisions];
                    for (var x = 0; x < divisions; x++)
                    {
                        for (var z = 0; z < divisions; z++)
                        {
                            Height[x, z] = float.MinValue;
                        }
                    }
                }
            }
        }
    }

    public class PrecipitationMaterial : Material
    {
        Texture2D RainTexture;
        Texture2D SnowTexture;
        IEnumerator<EffectPass> ShaderPasses;

        public PrecipitationMaterial(Viewer viewer)
            : base(viewer, null)
        {
            // TODO: This should happen on the loader thread.
            RainTexture = SharedTextureManager.LoadInternal(Viewer.RenderProcess.GraphicsDevice, System.IO.Path.Combine(Viewer.ContentPath, "Raindrop.png"));
            SnowTexture = SharedTextureManager.LoadInternal(Viewer.RenderProcess.GraphicsDevice, System.IO.Path.Combine(Viewer.ContentPath, "Snowflake.png"));
        }

        public override void SetState(GraphicsDevice graphicsDevice, Material previousMaterial)
        {
            var shader = Viewer.MaterialManager.PrecipitationShader;
            shader.CurrentTechnique = shader.Techniques["Precipitation"];
            if (ShaderPasses == null)
            {
                ShaderPasses = shader.Techniques["Precipitation"].Passes.GetEnumerator();
            }

            shader.LightVector.SetValue(Viewer.Settings.UseMSTSEnv ? Viewer.World.MSTSSky.mstsskysolarDirection : Viewer.World.Sky.SolarDirection);
            shader.RainTexture.SetValue(RainTexture);
            shader.SnowTexture.SetValue(SnowTexture);
            var visibility = Viewer.Settings.UseMSTSEnv ? Viewer.World.MSTSSky.mstsskyfogDistance : Viewer.Simulator.Weather.VisibilityM;
            shader.Visibility.SetValue(Math.Max(1, PrecipitationViewer.GetVisibility(Viewer.Simulator.Weather, visibility)));
            shader.FadeDistance.SetValue(new Vector2(PrecipitationPrimitive.ParticleFadeStartM, PrecipitationPrimitive.ParticleFadeEndM));

            graphicsDevice.BlendState = BlendState.NonPremultiplied;
            graphicsDevice.DepthStencilState = DepthStencilState.DepthRead;
        }

        public override void Render(GraphicsDevice graphicsDevice, IEnumerable<RenderItem> renderItems, ref Matrix XNAViewMatrix, ref Matrix XNAProjectionMatrix)
        {
            var shader = Viewer.MaterialManager.PrecipitationShader;

            ShaderPasses.Reset();
            while (ShaderPasses.MoveNext())
            {
                foreach (var item in renderItems)
                {
                    // Note: This is quite a hack. We ideally should be able to pass this through RenderItem somehow.
                    shader.CameraTileXZ.SetValue(new Vector2(item.XNAMatrix.M21, item.XNAMatrix.M22));
                    shader.CurrentTime.SetValue(item.XNAMatrix.M11);

                    shader.SetMatrix(Matrix.Identity, ref XNAViewMatrix, ref XNAProjectionMatrix);
                    ShaderPasses.Current.Apply();
                    item.RenderPrimitive.Draw(graphicsDevice);
                }
            }
        }

        public override void ResetState(GraphicsDevice graphicsDevice)
        {
            graphicsDevice.BlendState = BlendState.Opaque;
            graphicsDevice.DepthStencilState = DepthStencilState.Default;
        }

        public override bool GetBlending()
        {
            return true;
        }

        public override void Mark()
        {
            Viewer.TextureManager.Mark(RainTexture);
            Viewer.TextureManager.Mark(SnowTexture);

            base.Mark();
        }
    }

    [CallOnThread("Render")]
    public class PrecipitationShader : Shader
    {
        internal readonly EffectParameter WorldViewProjection;
        internal readonly EffectParameter InvView;
        internal readonly EffectParameter LightVector;
        internal readonly EffectParameter CameraTileXZ;
        internal readonly EffectParameter CurrentTime;
        internal readonly EffectParameter RainTexture;
        internal readonly EffectParameter SnowTexture;
        internal readonly EffectParameter Visibility;
        internal readonly EffectParameter FadeDistance;

        public PrecipitationShader(GraphicsDevice graphicsDevice)
            : base(graphicsDevice, "PrecipitationShader")
        {
            WorldViewProjection = Parameters["worldViewProjection"];
            InvView = Parameters["invView"];
            LightVector = Parameters["LightVector"];
            CameraTileXZ = Parameters["cameraTileXZ"];
            CurrentTime = Parameters["currentTime"];
            RainTexture = Parameters["rainTexture"];
            SnowTexture = Parameters["snowTexture"];
            Visibility = Parameters["visibility"];
            FadeDistance = Parameters["fadeDistance"];
        }

        public void SetMatrix(Matrix world, ref Matrix view, ref Matrix projection)
        {
            WorldViewProjection.SetValue(world * view * projection);
            InvView.SetValue(Matrix.Invert(view));
        }
    }
}
