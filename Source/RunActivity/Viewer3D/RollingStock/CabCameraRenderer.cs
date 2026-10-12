// COPYRIGHT 2026 by the Open Rails project. GNU GPL version 3 or later.
using System;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Orts.Formats.Msts;
using Orts.Simulation.RollingStocks;
using ORTS.Common;

namespace Orts.Viewer3D.RollingStock
{
    public sealed class CabCameraRenderer : CabViewControlRenderer
    {
        readonly CVCCamera Configuration;
        readonly CabRenderer Cab;
        readonly int CabIndex;
        internal readonly CabCameraFeed Feed;
        readonly BasicBlendedMaterial ImageMaterial;
        readonly BasicEffect ImageEffect;
        readonly Texture2D Black;
        readonly VertexPositionTexture[] Vertices = new VertexPositionTexture[4];
        static readonly short[] Indices = { 0, 1, 2, 0, 2, 3 };
        bool Texture3D;
        bool Available;
        double TimeSinceCapture = double.PositiveInfinity;
        public bool IsPageVisible => Cab.IsCameraCabActive(CabIndex) && (Control.Screens == null || Control.Screens.Count == 0 ||
            Control.Screens.Contains("all") || (Control.Display >= 0 && Control.Display < Cab.ActiveScreen.Length &&
            Control.Screens.Contains(Cab.ActiveScreen[Control.Display])));

        public CabCameraRenderer(Viewer viewer, MSTSLocomotive locomotive, CVCCamera control, CabRenderer cab, int cabIndex)
            : base(viewer, locomotive, control, null)
        {
            Configuration = control;
            Cab = cab;
            CabIndex = cabIndex;
            Feed = new CabCameraFeed(viewer, locomotive, control);
            ImageMaterial = new BasicBlendedMaterial(viewer, "CabCamera");
            ImageEffect = new BasicEffect(viewer.GraphicsDevice) { TextureEnabled = true, LightingEnabled = false };
            Black = new Texture2D(viewer.GraphicsDevice, 1, 1);
            Black.SetData(new[] { Color.Black });
        }

        internal Vector2[] Corners
        {
            get
            {
                if (Configuration.ScreenCorners != null) return Configuration.ScreenCorners;
                var corners = new[] { Vector2.Zero, new Vector2((float)Control.Width, 0),
                    new Vector2((float)Control.Width, (float)Control.Height), new Vector2(0, (float)Control.Height) };
                var rotation = Matrix.CreateRotationZ(Configuration.Rotation);
                var position = new Vector2((float)Control.PositionX, (float)Control.PositionY);
                return corners.Select(corner => Vector2.Transform(corner, rotation) + position).ToArray();
            }
        }

        void SetVertices(Vector2 scale, Vector2 offset)
        {
            var corners = Corners;
            for (int i = 0; i < 4; i++)
            {
                float u = i == 0 || i == 3 ? 0 : 1;
                Vertices[i] = new VertexPositionTexture(new Vector3(corners[i] * scale + offset, 0),
                    new Vector2(Configuration.Mirror ? 1 - u : u, i < 2 ? 0 : 1));
            }
        }

        public void SetTexture3D(Point origin)
        {
            Texture3D = true;
            SetVertices(Vector2.One, new Vector2(-origin.X, -origin.Y));
        }

        public override void PrepareFrame(RenderFrame frame, ElapsedTime elapsedTime)
        {
            if (frame.IsCameraFeed || !Control.IsVisible || !IsPageVisible || (!IsPowered && Control.HideIfDisabled) ||
                Control.Width <= 0 || Control.Height <= 0) return;
            Available = IsPowered && Feed.Camera.UpdateMount();
            TimeSinceCapture += elapsedTime.RealSeconds;
            if (Available && TimeSinceCapture >= 1 / Configuration.FramesPerSecond)
            {
                frame.RequestCameraFeed(Feed);
                TimeSinceCapture = 0;
            }
            if (Texture3D) return;
            float xScale = (float)Viewer.CabWidthPixels / 640;
            float yScale = (float)Viewer.CabHeightPixels / 480;
            SetVertices(new Vector2(xScale, yScale), new Vector2(-Viewer.CabXOffsetPixels + Viewer.CabXLetterboxPixels,
                Viewer.CabYOffsetPixels + Viewer.CabYLetterboxPixels));
            var matrix = Matrix.Identity;
            // Camera monitors are self-lit and do not use the cab's day/night shader.
            frame.AddPrimitive(ImageMaterial, this, RenderPrimitiveGroup.Cab, ref matrix);
        }

        public override void Draw(GraphicsDevice graphicsDevice) => DrawImage(graphicsDevice);

        internal void DrawImage(GraphicsDevice graphicsDevice)
        {
            if (!Control.IsVisible || !IsPageVisible || (!IsPowered && Control.HideIfDisabled)) return;
            Texture2D image = Available && IsPowered ? Feed.Image : null;
            graphicsDevice.BlendState = BlendState.Opaque;
            graphicsDevice.DepthStencilState = DepthStencilState.None;
            graphicsDevice.RasterizerState = RasterizerState.CullNone;
            ImageEffect.Texture = image ?? Black;
            ImageEffect.Projection = Matrix.CreateOrthographicOffCenter(0, graphicsDevice.Viewport.Width,
                graphicsDevice.Viewport.Height, 0, 0, 1);
            foreach (var pass in ImageEffect.CurrentTechnique.Passes)
            {
                pass.Apply();
                graphicsDevice.SamplerStates[0] = SamplerState.LinearClamp;
                graphicsDevice.DrawUserIndexedPrimitives(PrimitiveType.TriangleList, Vertices, 0, 4, Indices, 0, 2);
            }
            graphicsDevice.DepthStencilState = DepthStencilState.Default;
            graphicsDevice.RasterizerState = RasterizerState.CullCounterClockwise;
        }
    }
}
