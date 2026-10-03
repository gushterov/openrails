// COPYRIGHT 2026 by the Open Rails project. GNU GPL version 3 or later.
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Orts.Formats.Msts;
using Orts.Viewer3D.RollingStock;

namespace Orts.Viewer3D
{
    /// <summary>Precomputed alpha footprints and the strips swept between adjacent animation frames.</summary>
    public sealed class CabWiperSweep
    {
        readonly int[][] Frames;
        readonly int[][] Strips;
        readonly HashSet<int> Pending = new HashSet<int>();

        public CabWiperSweep(int width, int height, IReadOnlyList<bool[]> frames)
        {
            Frames = frames.Select(Pixels).ToArray();
            Strips = new int[Math.Max(0, frames.Count - 1)][];
            for (int i = 0; i < Strips.Length; i++)
            {
                var swept = new bool[width * height];
                for (int p = 0; p < swept.Length; p++) swept[p] = frames[i][p] || frames[i + 1][p];
                // Bridge matching opaque runs in both axes. Multiple blades remain separate;
                // do not join scanlines whose number of separate parts changes between frames.
                Bridge(frames[i], frames[i + 1], swept, width, height, 1, width);
                Bridge(frames[i], frames[i + 1], swept, height, width, width, 1);
                Strips[i] = Pixels(swept);
            }
        }

        static int[] Pixels(bool[] mask) => Enumerable.Range(0, mask.Length).Where(i => mask[i]).ToArray();

        static void Bridge(bool[] a, bool[] b, bool[] result, int length, int lines, int step, int lineStep)
        {
            var first = new List<(int Start, int End)>();
            var second = new List<(int Start, int End)>();
            for (int line = 0; line < lines; line++)
            {
                void Runs(bool[] mask, List<(int Start, int End)> runs)
                {
                    runs.Clear();
                    for (int p = 0; p < length; p++)
                    {
                        if (!mask[line * lineStep + p * step]) continue;
                        int start = p;
                        while (p + 1 < length && mask[line * lineStep + (p + 1) * step]) p++;
                        runs.Add((start, p));
                    }
                }
                Runs(a, first);
                Runs(b, second);
                if (first.Count != second.Count) continue;
                for (int r = 0; r < first.Count; r++)
                    for (int p = Math.Min(first[r].Start, second[r].Start); p <= Math.Max(first[r].End, second[r].End); p++)
                        result[line * lineStep + p * step] = true;
            }
        }

        void Queue(int from, int to)
        {
            from = Math.Clamp(from, 0, Frames.Length - 1);
            to = Math.Clamp(to, 0, Frames.Length - 1);
            if (from == to) Pending.Add(-from - 1);
            for (int i = Math.Min(from, to); i < Math.Max(from, to); i++) Pending.Add(i);
        }

        // Follow the renderer's own frame mapping. Include reversals and skipped frames,
        // even when a long update crosses a whole cycle and ends at the starting frame.
        public void Advance(float phase, float seconds, float cycle, Func<float, int> frameAt)
        {
            if (seconds <= 0 || cycle <= 0 || Frames.Length == 0) return;
            if (seconds >= cycle)
            {
                Queue(frameAt(0), frameAt(1));
                return;
            }
            double half = cycle / 2.0;
            double position = phase;
            double end = position + seconds;
            int Index(double t)
            {
                double p = t % cycle;
                return frameAt((float)(p <= half ? p / half : (cycle - p) / half));
            }
            while (position < end)
            {
                double next = Math.Min(end, (Math.Floor(position / half) + 1) * half);
                Queue(Index(position), Index(next));
                position = next;
            }
        }

        public bool Apply(float[] lastWiped, float time)
        {
            bool changed = Pending.Count > 0;
            foreach (int segment in Pending)
                foreach (int pixel in segment < 0 ? Frames[-segment - 1] : Strips[segment])
                    lastWiped[pixel] = time;
            Pending.Clear();
            return changed;
        }
    }

    internal static class CabWiperMask
    {
        internal const int Width = 512;
        internal const int Height = 384;

        // Loader thread only. Read the stable daytime alpha once, never read back the GPU
        // during driving. CABTextureManager supports Color and Dxt1 animation frames.
        internal static CabWiperSweep Load(CVCAnimatedDisplay control)
        {
            if (control.CycleTimeS <= 0 || control.FramesCount < 2 || control.Width <= 0 || control.Height <= 0) return null;
            var masks = new List<bool[]>();
            bool hasBlade = false;
            for (int i = 0; i < control.FramesCount; i++)
            {
                var texture = CABTextureManager.GetTextureByIndexes(control.ACEFile, i, false, false, out _, false);
                if (texture == SharedMaterialManager.MissingTexture) return null;
                byte[] alpha;
                if (texture.Format == SurfaceFormat.Color)
                {
                    var pixels = new Color[texture.Width * texture.Height];
                    texture.GetData(pixels);
                    alpha = pixels.Select(p => p.A).ToArray();
                }
                else if (texture.Format == SurfaceFormat.Dxt1)
                {
                    var blocks = new byte[((texture.Width + 3) / 4) * ((texture.Height + 3) / 4) * 8];
                    texture.GetData(blocks);
                    alpha = DecodeDxt1Alpha(blocks, texture.Width, texture.Height);
                }
                else return null;
                // A solid rectangle is not a usable blade mask; retain simplified clearing.
                if (alpha.All(a => a >= 32)) return null;
                var mask = new bool[Width * Height];
                double dx = Math.Min(control.Width, texture.Width) / texture.Width * Width / 640.0;
                double dy = Math.Min(control.Height, texture.Height) / texture.Height * Height / 480.0;
                double left = control.PositionX * 1.0001 * Width / 640.0;
                double top = control.PositionY * 1.0001 * Height / 480.0;
                for (int y = 0; y < texture.Height; y++)
                    for (int x = 0; x < texture.Width; x++)
                    {
                        if (alpha[y * texture.Width + x] < 32) continue;
                        // Conservative coverage keeps thin blades when reducing resolution.
                        int x0 = Math.Max(0, (int)Math.Floor(left + x * dx));
                        int x1 = Math.Min(Width, (int)Math.Ceiling(left + (x + 1) * dx));
                        int y0 = Math.Max(0, (int)Math.Floor(top + y * dy));
                        int y1 = Math.Min(Height, (int)Math.Ceiling(top + (y + 1) * dy));
                        for (int py = y0; py < y1; py++)
                            for (int px = x0; px < x1; px++) mask[py * Width + px] = hasBlade = true;
                    }
                masks.Add(mask);
            }
            return hasBlade ? new CabWiperSweep(Width, Height, masks) : null;
        }

        internal static byte[] DecodeDxt1Alpha(byte[] blocks, int width, int height)
        {
            var alpha = new byte[width * height];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    int offset = ((y / 4) * ((width + 3) / 4) + x / 4) * 8;
                    int c0 = blocks[offset] | blocks[offset + 1] << 8;
                    int c1 = blocks[offset + 2] | blocks[offset + 3] << 8;
                    int selector = (blocks[offset + 4 + y % 4] >> ((x % 4) * 2)) & 3;
                    alpha[y * width + x] = c0 <= c1 && selector == 3 ? (byte)0 : (byte)255;
                }
            return alpha;
        }
    }
}
