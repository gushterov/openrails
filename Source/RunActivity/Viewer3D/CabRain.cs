// COPYRIGHT 2026 by the Open Rails project.
// This file is part of Open Rails, distributed under the GNU GPL version 3 or later.

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Orts.Formats.Msts;

namespace Orts.Viewer3D
{
    /// <summary>Rain retained on one pane, independent of the currently selected cab view.</summary>
    public class CabRainState
    {
        public float Wetness { get; private set; }
        public float Time { get; private set; }

        public void Update(float seconds, float rain, bool wipers)
        {
            seconds = Math.Max(0, seconds);
            rain = MathHelper.Clamp(rain, 0, 1);
            Time += seconds;
            // Exponential approach is independent of frame rate. Wipers provide an approximate
            // front-pane clearing effect: legacy CVFs do not describe their swept glass area.
            float target = rain * (wipers ? 0.16f : 1f);
            float rate = wipers ? 2f : target > Wetness ? 0.35f : 0.055f;
            Wetness = MathHelper.Lerp(Wetness, target, 1 - (float)Math.Exp(-seconds * rate));
        }
    }

    /// <summary>One glass overlay, between the 2D cab background and its animated controls.</summary>
    internal sealed class CabRain : RenderPrimitive
    {
        sealed class Pane
        {
            internal Texture2D Mask;
            internal bool Front;
            internal readonly CabRainState State = new CabRainState();
            internal readonly List<CabWiperSweep> Wipers = new List<CabWiperSweep>();
            internal readonly object WipeLock = new object();
            internal Texture2D WipeTexture;
            internal float[] LastWiped;
            internal float RecoveryTime;
            internal bool WipeDirty;
        }

        readonly Viewer Viewer;
        readonly Effect Effect;
        readonly SpriteBatchMaterial Material;
        readonly Dictionary<CabViewFile, List<Pane>> Panes = new Dictionary<CabViewFile, List<Pane>>();
        Matrix Identity = Matrix.Identity;
        Pane CurrentPane;
        Texture2D CabTexture;

        internal CabRain(Viewer viewer)
        {
            Viewer = viewer;
            Effect = new CabRainShader(viewer.GraphicsDevice);
            Material = (SpriteBatchMaterial)viewer.MaterialManager.Load("SpriteBatch", effect: Effect);
            SortIndex = 1;
        }

        // Called on the loader thread, alongside CABTextureManager.LoadTextures.
        internal void Load(CabViewFile cab)
        {
            if (Panes.ContainsKey(cab)) return;
            var panes = new List<Pane>();
            for (int i = 0; i < cab.TwoDViews.Count; i++)
            {
                var mask = cab.WindowViews[i] == null ? SharedMaterialManager.MissingTexture
                    : Viewer.TextureManager.Get(cab.WindowViews[i]);
                if (mask == SharedMaterialManager.MissingTexture)
                    mask = Viewer.TextureManager.Get(cab.TwoDViews[i], true);
                panes.Add(new Pane { Mask = mask, Front = i == 0 });
            }
            Panes.Add(cab, panes);
        }

        internal CabWiperSweep AddWiper(CabViewFile cab, CVCAnimatedDisplay control)
        {
            if (control.ControlType.Type != CABViewControlTypes.ORTS_2DEXTERNALWIPERS
                || control.CabViewpoint < 0 || control.CabViewpoint >= Panes[cab].Count) return null;
            var sweep = CabWiperMask.Load(control);
            if (sweep == null) return null;
            var pane = Panes[cab][control.CabViewpoint];
            if (pane.WipeTexture == null)
            {
                pane.WipeTexture = new Texture2D(Viewer.GraphicsDevice, CabWiperMask.Width, CabWiperMask.Height, false, SurfaceFormat.Single);
                pane.LastWiped = new float[CabWiperMask.Width * CabWiperMask.Height];
                Array.Fill(pane.LastWiped, -10000f);
                pane.WipeDirty = true;
            }
            pane.Wipers.Add(sweep);
            return sweep;
        }

        internal void PrepareFrame(RenderFrame frame, CabViewFile cab, int location, Texture2D texture, float seconds, bool wipers)
        {
            var weather = Viewer.Simulator.Weather;
            // Both rain and melting snow leave water on the cab glass.
            float rain = Viewer.Camera.IsUnderground ? 0
                : weather.PrecipitationIntensityPPSPM2 / PrecipitationViewer.MaxIntensityPPSPM2;
            foreach (var panes in Panes.Values)
                foreach (var pane in panes)
                {
                    pane.State.Update(seconds, rain, wipers && pane.Front && pane.Wipers.Count == 0);
                    if (pane.WipeTexture == null) continue;
                    lock (pane.WipeLock)
                    {
                        // Advance recovery only when fresh precipitation can wet the glass.
                        // Stopping the rain must not resurrect drops which were already wiped.
                        pane.RecoveryTime += Math.Max(0, seconds) * MathHelper.Clamp(rain, 0, 1) * 0.45f;
                        foreach (var sweep in pane.Wipers)
                            pane.WipeDirty |= sweep.Apply(pane.LastWiped, pane.RecoveryTime);
                    }
                }

            CurrentPane = Panes[cab][location];
            CabTexture = texture;
            if (CurrentPane.State.Wetness > 0.002f && CurrentPane.Mask != SharedMaterialManager.MissingTexture)
                frame.AddPrimitive(Material, this, RenderPrimitiveGroup.Cab, ref Identity);
        }

        public override void Draw(GraphicsDevice graphicsDevice)
        {
            var pane = CurrentPane;
            // Use the same source rectangle, origin and scaling as the background, including
            // stretched, scrolled and letterboxed cabs. UVs stay attached to the glass.
            var scale = new Vector2((float)Viewer.CabWidthPixels / CabTexture.Width, (float)Viewer.CabHeightPixels / CabTexture.Height);
            var rect = new Rectangle((int)Math.Round(Viewer.CabXOffsetPixels / scale.X),
                (int)Math.Round(-Viewer.CabYOffsetPixels / scale.Y),
                (int)Math.Round((Viewer.CabWidthPixels - Viewer.CabExceedsDisplayHorizontally) / scale.X),
                (int)Math.Round((Viewer.CabHeightPixels - Viewer.CabExceedsDisplay) / scale.Y));
            var position = new Vector2(Viewer.CabWidthPixels / 2 + Viewer.CabXLetterboxPixels,
                Viewer.CabHeightPixels / 2 + Viewer.CabYLetterboxPixels);
            Effect.Parameters["WindowTexture"].SetValue(pane.Mask);
            Effect.Parameters["UseWipeMask"].SetValue(pane.WipeTexture != null ? 1f : 0f);
            Effect.Parameters["WipeTexture"].SetValue(pane.WipeTexture ?? pane.Mask);
            lock (pane.WipeLock)
            {
                if (pane.WipeDirty)
                {
                    pane.WipeTexture.SetData(pane.LastWiped);
                    pane.WipeDirty = false;
                }
                Effect.Parameters["WipeRecoveryTime"].SetValue(pane.RecoveryTime);
            }
            Effect.Parameters["RainTime"].SetValue(pane.State.Time);
            Effect.Parameters["Wetness"].SetValue(pane.State.Wetness);
            Effect.Parameters["PaneAspect"].SetValue((float)Viewer.CabWidthPixels / Viewer.CabHeightPixels);
            Effect.Parameters["Daylight"].SetValue(MathHelper.Clamp((Viewer.MaterialManager.sunDirection.Y + 0.1f) / 0.3f, 0, 1));
            Material.SpriteBatch.Draw(CabTexture, position, rect, Color.White, 0,
                new Vector2(CabTexture.Width / 2, CabTexture.Height / 2), scale, SpriteEffects.None, 0);
        }

        internal void Mark()
        {
            foreach (var panes in Panes.Values)
                foreach (var pane in panes)
                    Viewer.TextureManager.Mark(pane.Mask);
        }

        sealed class CabRainShader : Shader
        {
            internal CabRainShader(GraphicsDevice graphicsDevice) : base(graphicsDevice, "CabRainShader") { }
        }
    }
}
