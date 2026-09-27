// COPYRIGHT 2026 by the Open Rails project.
// Licensed under the GNU General Public License, version 3 or later.

using Orts.Viewer3D;
using Xunit;

namespace Tests.RunActivity
{
    public class AmbientLightingTests
    {
        [Theory]
        [InlineData(0, -0.5f, 0f)]
        [InlineData(50, -0.5f, 0.5f)]
        [InlineData(100, -0.5f, 1f)]
        [InlineData(50, 0f, 0.75f)]
        [InlineData(0, 0.1f, 1f)]
        [InlineData(50, 0.5f, 1f)]
        [InlineData(-20, -0.5f, 0f)]
        [InlineData(200, -0.5f, 1f)]
        public void ExteriorLightingFadesOnlyAtNight(int setting, float sunHeight, float expected)
        {
            Assert.Equal(expected, AmbientLighting.NightMultiplier(setting, sunHeight), 5);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(50)]
        [InlineData(100)]
        public void CabInteriorRetainsOriginalBrightness(int setting)
        {
            Assert.Equal(1f, AmbientLighting.NightMultiplier(setting, -0.5f, ShapeFlags.Interior | ShapeFlags.ShadowCaster));
        }
    }
}
