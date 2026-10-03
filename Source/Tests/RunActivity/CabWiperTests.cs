// COPYRIGHT 2026 by the Open Rails project. GNU GPL version 3 or later.
using System;
using System.Linq;
using System.Reflection;
using Orts.Viewer3D;
using Xunit;

namespace Tests.RunActivity
{
    public class CabWiperTests
    {
        const int Width = 20;
        const int Height = 10;

        static bool[] Blade(params int[] columns)
        {
            var mask = new bool[Width * Height];
            foreach (int x in columns)
                for (int y = 2; y <= 7; y++) mask[y * Width + x] = true;
            return mask;
        }

        static float[] DryMap() => Enumerable.Repeat(-10000f, Width * Height).ToArray();
        static int Frame(float phase) => Math.Min(2, (int)(phase * 3));

        [Theory]
        [InlineData(0f, 1.9f)] // skips the middle frame
        [InlineData(2f, 1.9f)] // return stroke
        [InlineData(0f, 4f)] // one entire cycle ends on the same frame
        [InlineData(0.8f, 12f)] // several cycles in a long update
        public void SweepsSkippedFramesWithoutClearingTheWholeRectangle(float phase, float elapsed)
        {
            var sweep = new CabWiperSweep(Width, Height, new[] { Blade(2), Blade(6), Blade(10) });
            var map = DryMap();
            sweep.Advance(phase, elapsed, 4, Frame);
            Assert.True(sweep.Apply(map, 8));
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                    Assert.Equal(y >= 2 && y <= 7 && x >= 2 && x <= 10 ? 8f : -10000f, map[y * Width + x]);
            Assert.False(sweep.Apply(map, 9)); // consumed movement must not wipe again
        }

        [Fact]
        public void ReversalIncludesTheOuterBladePosition()
        {
            var sweep = new CabWiperSweep(Width, Height, new[] { Blade(2), Blade(6), Blade(10) });
            var map = DryMap();
            sweep.Advance(1.1f, 1.8f, 4, Frame); // middle -> end -> middle
            sweep.Apply(map, 3);
            Assert.Equal(3, map[4 * Width + 10]);
            Assert.Equal(3, map[4 * Width + 8]);
            Assert.Equal(-10000, map[4 * Width + 2]);
        }

        [Fact]
        public void SeparateBladesDoNotClearTheGapBetweenThem()
        {
            var sweep = new CabWiperSweep(Width, Height, new[] { Blade(2, 12), Blade(4, 14) });
            var map = DryMap();
            sweep.Advance(0, 1, 2, p => p < 0.5f ? 0 : 1);
            sweep.Apply(map, 1);
            Assert.Equal(1, map[4 * Width + 3]);
            Assert.Equal(1, map[4 * Width + 13]);
            Assert.Equal(-10000, map[4 * Width + 8]);
        }

        [Fact]
        public void PausedAnimationDoesNotWipe()
        {
            var sweep = new CabWiperSweep(Width, Height, new[] { Blade(2), Blade(6) });
            sweep.Advance(0, 0, 2, p => 0);
            Assert.False(sweep.Apply(DryMap(), 1));
        }

        [Fact]
        public void CompressedFrameAlphaRecognizesTransparentDxt1Selectors()
        {
            var method = typeof(CabWiperSweep).Assembly.GetType("Orts.Viewer3D.CabWiperMask")
                .GetMethod("DecodeDxt1Alpha", BindingFlags.NonPublic | BindingFlags.Static);
            byte[] block = { 0, 0, 255, 255, 0xE4, 0xE4, 0xE4, 0xE4 };
            var alpha = (byte[])method.Invoke(null, new object[] { block, 4, 4 });
            Assert.Equal(new byte[] { 255, 255, 255, 0 }, alpha.Take(4));
            block[0] = block[1] = 255;
            block[2] = block[3] = 0;
            alpha = (byte[])method.Invoke(null, new object[] { block, 4, 4 });
            Assert.All(alpha, a => Assert.Equal(255, a));
        }
    }
}
