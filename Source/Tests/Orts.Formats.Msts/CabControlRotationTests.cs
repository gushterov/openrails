// COPYRIGHT 2026 by the Open Rails project. GNU GPL version 3 or later.
using System.IO;
using Microsoft.Xna.Framework;
using Orts.Formats.Msts;
using Xunit;

namespace Tests.Orts.Formats.Msts
{
    public class CabControlRotationTests
    {
        [Theory]
        [InlineData("Lever", "LEVER", false)]
        [InlineData("TwoState", "TWO_STATE", false)]
        [InlineData("TriState", "TRI_STATE", false)]
        [InlineData("MultiState", "MULTI_STATE", false)]
        [InlineData("CombinedControl", "COMBINED_CONTROL", false)]
        [InlineData("MultiStateDisplay", "MULTI_STATE_DISPLAY", true)]
        [InlineData("ORTSAnimatedDisplay", "MULTI_STATE_DISPLAY", true)]
        public void FramedControlsParseAnglesWithTheExistingSignConvention(string block, string type, bool display)
        {
            var filePath = Path.GetTempFileName();
            try
            {
                foreach (var degrees in new[] { 0, -13, 13 })
                {
                    // An omitted angle must preserve existing cab files.
                    var angle = degrees == 0 ? "" : $"ORTSAngle ( {degrees} )";
                    var frames = display
                        ? "States ( 2 2 1 State ( Style ( 0 ) SwitchVal ( 0 ) ) State ( Style ( 0 ) SwitchVal ( 1 ) ) )"
                        : "NumFrames ( 2 2 1 ) NumPositions ( 2 0 1 ) NumValues ( 2 0 1 )";
                    File.WriteAllText(filePath, $@"
                        Tr_CabViewFile ( CabViewControls ( 1 {block} (
                            Type ( ORTS_POWER_SUPPLY2 {type} )
                            Position ( 418.7 305.5 20 33 ) Graphic ( dummy.ace )
                            ScaleRange ( 0 1 ) Style ( NONE ) {angle} {frames}
                        ) ) )");
                    var cab = new CabViewFile(filePath, Path.GetDirectoryName(filePath));
                    var control = Assert.IsAssignableFrom<CVCWithFrames>(Assert.Single(cab.CabViewControls));
                    Assert.Equal(-MathHelper.ToRadians(degrees), control.Rotation);
                    Assert.Equal(2, control.FramesCount);
                    Assert.Equal(418.7, control.PositionX, 3);
                }
            }
            finally { File.Delete(filePath); }
        }
    }
}
