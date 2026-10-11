// COPYRIGHT 2026 by the Open Rails project. GNU GPL version 3 or later.
using Microsoft.Xna.Framework;
using Orts.Viewer3D;
using Xunit;

namespace Tests.RunActivity
{
    public class CabGaugeGeometryTests
    {
        static void EqualPosition(Vector2 expected, Vector2 actual)
        {
            Assert.InRange(Vector2.Distance(expected, actual), 0, 0.001f);
        }

        [Theory]
        [InlineData(13f, 3f, 2.25f)] // ORTSAngle (-13), 16:9 cab texture.
        [InlineData(-13f, 3f, 2.25f)]
        [InlineData(45f, 2f, 4f)]
        [InlineData(-45f, 4f, 2f)]
        [InlineData(0f, 3f, 3f)]
        public void VerticalFillKeepsBottomAtTheSamePosition(float degrees, float xratio, float yratio)
        {
            var position = new Vector2(418.7f * xratio, 305.5f * yratio);
            var size = new Vector2(2.2f * xratio, 33f * yratio);
            var rotation = MathHelper.ToRadians(degrees);
            var expectedBottom = position + new Vector2(0, size.Y);
            foreach (var fraction in new[] { 0f, 0.05f, 0.25f, 0.625f, 1f, 0.25f, 0f })
            {
                var filledSize = new Vector2((int)size.X, (int)(size.Y * fraction));
                var top = CabGaugeGeometry.GetFillPosition(position, size, filledSize, true, true, rotation);
                var bottom = top + Vector2.Transform(new Vector2(0, filledSize.Y), Matrix.CreateRotationZ(rotation));
                EqualPosition(expectedBottom, bottom);
            }
        }

        [Theory]
        [InlineData(13f)]
        [InlineData(-13f)]
        [InlineData(45f)]
        public void HorizontalFillKeepsRightEndAtTheSamePosition(float degrees)
        {
            var position = new Vector2(123, 456);
            var size = new Vector2(120, 6);
            var rotation = MathHelper.ToRadians(degrees);
            foreach (var fraction in new[] { 0f, 0.1f, 0.5f, 1f })
            {
                var filledSize = new Vector2((int)(size.X * fraction), size.Y);
                var left = CabGaugeGeometry.GetFillPosition(position, size, filledSize, false, true, rotation);
                var right = left + Vector2.Transform(new Vector2(filledSize.X, 0), Matrix.CreateRotationZ(rotation));
                EqualPosition(position + new Vector2(size.X, 0), right);
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ForwardFillKeepsItsOrigin(bool vertical)
        {
            var position = new Vector2(123, 456);
            foreach (var fraction in new[] { 0f, 0.1f, 0.5f, 1f })
            {
                var size = new Vector2(120, 60);
                var filledSize = vertical ? new Vector2(size.X, size.Y * fraction) : new Vector2(size.X * fraction, size.Y);
                EqualPosition(position, CabGaugeGeometry.GetFillPosition(position, size, filledSize, vertical, false, -0.2f));
            }
        }
    }
}
