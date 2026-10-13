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

        [Theory]
        [InlineData(-3000f, 0f, 13f, 3f, 2.25f, true, true)] // Reported CVF: ORTSAngle (-13), DirIncrease (1).
        [InlineData(-3000f, 0f, -13f, 3f, 2.25f, true, true)]
        [InlineData(-3000f, 0f, 45f, 2f, 4f, true, false)]
        [InlineData(-3000f, 1500f, 13f, 3f, 2.25f, true, true)]
        [InlineData(-3000f, 1500f, -45f, 4f, 2f, true, false)]
        [InlineData(-3000f, 0f, 13f, 3f, 2.25f, false, false)]
        [InlineData(-3000f, 1500f, -13f, 3f, 2.25f, false, true)]
        public void SignedFillKeepsZeroFixedOnBothSides(float minimum, float maximum, float degrees,
            float xratio, float yratio, bool vertical, bool reverseDirection)
        {
            var position = new Vector2(414.1f * xratio, 300.3f * yratio);
            var size = new Vector2(2.2f * xratio, 36f * yratio);
            var rotation = MathHelper.ToRadians(degrees);
            var zeroFraction = -minimum / (maximum - minimum);
            var zeroPosition = position + (vertical ? new Vector2(0, size.Y * zeroFraction) : new Vector2(size.X * zeroFraction, 0));
            var axis = Vector2.Transform(vertical ? Vector2.UnitY : Vector2.UnitX, Matrix.CreateRotationZ(rotation));
            foreach (var value in new[] { 0f, minimum * 0.05f, minimum * 0.5f, minimum, minimum * 0.25f, 0f, maximum * 0.5f, maximum, 0f })
            {
                var fraction = (value - minimum) / (maximum - minimum);
                var zeroOffset = (vertical ? size.Y : size.X) * zeroFraction;
                var valueOffset = (vertical ? size.Y : size.X) * fraction;
                // Match the renderer's screen-pixel truncation for each orientation.
                var length = vertical ? System.Math.Abs((int)(valueOffset - zeroOffset)) : System.Math.Abs((int)valueOffset - (int)zeroOffset);
                var filledSize = vertical ? new Vector2((int)size.X, length) : new Vector2(length, (int)size.Y);
                var reverse = vertical ? (reverseDirection ? value < 0 : value > 0) : value < 0;
                var topLeft = CabGaugeGeometry.GetFillPositionFromZero(zeroPosition, filledSize, vertical, reverse, rotation);
                var farEnd = topLeft + axis * length;
                EqualPosition(zeroPosition, reverse ? farEnd : topLeft);
                EqualPosition(zeroPosition + axis * (reverse ? -length : length), reverse ? topLeft : farEnd);

                // SpriteBatch receives integer coordinates, so its zero end may vary by at most a pixel per axis.
                var renderedZero = new Vector2((int)topLeft.X, (int)topLeft.Y) + (reverse ? axis * length : Vector2.Zero);
                Assert.InRange(Vector2.Distance(zeroPosition, renderedZero), 0, 1.415f);
            }
        }
    }
}
