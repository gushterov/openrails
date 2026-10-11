// COPYRIGHT 2026 by the Open Rails project. GNU GPL version 3 or later.
using Microsoft.Xna.Framework;
using Orts.Viewer3D;
using Xunit;

namespace Tests.RunActivity
{
    public class CabControlGeometryTests
    {
        [Theory]
        [InlineData(13f)]
        [InlineData(-13f)]
        [InlineData(45f)]
        [InlineData(-45f)]
        public void PickingFollowsTheRotatedSpriteRatherThanItsUnrotatedRectangle(float degrees)
        {
            // Width and height are already scaled independently for a wide cab texture.
            var rectangle = new Rectangle(1200, 700, 30, 100);
            float rotation = MathHelper.ToRadians(degrees);
            var transform = Matrix.CreateRotationZ(rotation);
            var origin = new Vector2(rectangle.X, rectangle.Y);
            foreach (var local in new[] { new Vector2(1, 1), new Vector2(15, 50), new Vector2(15, 99) })
                Assert.True(CabControlGeometry.Contains(rectangle, origin + Vector2.Transform(local, transform), rotation));
            foreach (var local in new[] { new Vector2(-1, 50), new Vector2(31, 50), new Vector2(15, -1), new Vector2(15, 101) })
                Assert.False(CabControlGeometry.Contains(rectangle, origin + Vector2.Transform(local, transform), rotation));

            var bounds = CabControlGeometry.GetBounds(rectangle, rotation);
            foreach (var local in new[] { Vector2.Zero, new Vector2(30, 0), new Vector2(0, 100), new Vector2(30, 100) })
            {
                var point = origin + Vector2.Transform(local, transform);
                Assert.InRange(point.X, bounds.Left, bounds.Right);
                Assert.InRange(point.Y, bounds.Top, bounds.Bottom);
            }
        }

        [Theory]
        [InlineData(true, false, 13f)]
        [InlineData(true, true, -13f)]
        [InlineData(false, false, -13f)]
        [InlineData(false, true, 13f)]
        public void DraggingUsesTheLocalAxisAndWheelRetainsItsDirection(bool vertical, bool reverse, float degrees)
        {
            var size = new Vector2(40, 100);
            float rotation = MathHelper.ToRadians(degrees);
            var localMovement = vertical ? new Vector2(0, 25) : new Vector2(10, 0);
            var screenMovement = Vector2.Transform(localMovement, Matrix.CreateRotationZ(rotation));
            float expected = reverse ? -0.25f : 0.25f;
            Assert.Equal(expected, CabControlGeometry.NormalizeMovement(screenMovement, 0, size, vertical, reverse, rotation), 5);
            var perpendicular = Vector2.Transform(vertical ? new Vector2(10, 0) : new Vector2(0, 25), Matrix.CreateRotationZ(rotation));
            Assert.Equal(0, CabControlGeometry.NormalizeMovement(perpendicular, 0, size, vertical, reverse, rotation), 5);
            float wheel = vertical ? 25 : 10;
            Assert.Equal(expected, CabControlGeometry.NormalizeMovement(Vector2.Zero, wheel, size, vertical, reverse, rotation), 5);
        }

        [Fact]
        public void UnrotatedControlsPreserveRectangleEdgesAndMouseScaling()
        {
            var rectangle = new Rectangle(123, 456, 30, 100);
            Assert.True(CabControlGeometry.Contains(rectangle, new Vector2(123, 456), 0));
            Assert.True(CabControlGeometry.Contains(rectangle, new Vector2(152, 555), 0));
            Assert.False(CabControlGeometry.Contains(rectangle, new Vector2(153, 500), 0));
            Assert.False(CabControlGeometry.Contains(rectangle, new Vector2(130, 556), 0));
            Assert.Equal(rectangle, CabControlGeometry.GetBounds(rectangle, 0));
            Assert.Equal(-0.3f, CabControlGeometry.NormalizeMovement(new Vector2(8, 22), 8, new Vector2(40, 100), true, true, 0));
            Assert.Equal(0.4f, CabControlGeometry.NormalizeMovement(new Vector2(8, 22), 8, new Vector2(40, 100), false, false, 0));
        }
    }
}
