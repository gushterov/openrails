// COPYRIGHT 2026 by the Open Rails project. GNU GPL version 3 or later.
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Xna.Framework;
using Orts.Formats.Msts;
using Orts.Viewer3D;
using Xunit;

namespace Tests.RunActivity
{
    public class CabWiperMaskRotationTests
    {
        [Theory]
        [InlineData(0f, 96, 96, 70)]
        [InlineData(90f, 74, 75, 85)]
        [InlineData(-90f, 84, 85, 42)]
        public void WiperMaskFollowsScreenRotationOnAWideCab(float degrees, int firstColumn, int lastColumn, int row)
        {
            var filePath = Path.GetTempFileName();
            try
            {
                File.WriteAllText(filePath, @"
                    Tr_CabViewFile ( CabViewControls ( 1 ORTSAnimatedDisplay (
                        Type ( ORTS_2DEXTERNALWIPERS MULTI_STATE_DISPLAY )
                        Position ( 100 80 40 40 ) Graphic ( dummy.ace )
                        States ( 2 2 1 State ( Style ( 0 ) SwitchVal ( 0 ) ) State ( Style ( 0 ) SwitchVal ( 1 ) ) )
                    ) ) )");
                var cab = new CabViewFile(filePath, Path.GetDirectoryName(filePath));
                var control = Assert.IsType<CVCAnimatedDisplay>(Assert.Single(cab.CabViewControls));
                control.Rotation = MathHelper.ToRadians(degrees);
                var alpha = new byte[100 * 100];
                alpha[20 * 100 + 50] = 255; // One thin blade pixel; remaining pixels are transparent.
                var method = typeof(CabWiperSweep).Assembly.GetType("Orts.Viewer3D.CabWiperMask")
                    .GetMethod("CreateMask", BindingFlags.NonPublic | BindingFlags.Static);
                var mask = (bool[])method.Invoke(null, new object[] { control, alpha, 100, 100, 16f / 9 });
                var expected = Enumerable.Range(firstColumn, lastColumn - firstColumn + 1).Select(x => row * 512 + x);
                Assert.Equal(expected, mask.Select((pixel, index) => new { pixel, index }).Where(p => p.pixel).Select(p => p.index));
            }
            finally { File.Delete(filePath); }
        }
    }
}
