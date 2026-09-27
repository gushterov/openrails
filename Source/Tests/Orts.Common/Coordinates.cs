// COPYRIGHT 2026 by the Open Rails project.
// Licensed under the GNU General Public License, version 3 or later.

using ORTS.Common;
using Xunit;

namespace Tests.Orts.Common
{
    public class Coordinates
    {
        [Theory]
        [InlineData(-3072f, -1, -1024f)]
        [InlineData(-1024.0001f, -1, 1023.9999f)]
        [InlineData(-1024f, 0, -1024f)]
        [InlineData(0f, 0, 0f)]
        [InlineData(1023.99994f, 0, 1023.99994f)]
        [InlineData(1024f, 1, -1024f)]
        [InlineData(3072f, 2, -1024f)]
        public void NormalizationPreservesPositionAndUsesHalfOpenTileBounds(float coordinate, int tileOffset, float expected)
        {
            var original = new WorldLocation(7, -3, coordinate, 20, coordinate);
            var location = original;
            location.Normalize();
            Assert.Equal(7 + tileOffset, location.TileX);
            Assert.Equal(-3 + tileOffset, location.TileZ);
            Assert.Equal(expected, location.Location.X);
            Assert.Equal(expected, location.Location.Z);
            Assert.True(location.Location.X >= -1024 && location.Location.X < 1024);
            Assert.Equal(0, WorldLocation.GetDistanceSquared(original, location));
            var normalized = location;
            location.Normalize();
            Assert.Equal(normalized, location);
        }
    }
}
