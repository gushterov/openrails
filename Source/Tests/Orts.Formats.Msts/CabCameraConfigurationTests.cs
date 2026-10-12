// COPYRIGHT 2026 by the Open Rails project. GNU GPL version 3 or later.
using System.IO;
using Microsoft.Xna.Framework;
using Orts.Formats.Msts;
using Xunit;

namespace Tests.Orts.Formats.Msts
{
    public class CabCameraConfigurationTests
    {
        static CabViewControls Parse(string content)
        {
            var path = Path.GetTempFileName();
            try
            {
                File.WriteAllText(path, "\nTr_CabViewFile ( CabViewControls ( 2 " + content + " ) )");
                return new CabViewFile(path, Path.GetDirectoryName(path)).CabViewControls;
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void CameraDefaultsAreSmallAndManual()
        {
            var controls = Parse("ORTSCamera ( Position ( 0 0 160 120 ) )");
            var camera = Assert.IsType<CVCCamera>(Assert.Single(controls));
            Assert.Equal(CABViewControlTypes.ORTS_CAMERA, camera.ControlType.Type);
            Assert.Equal(320, camera.ImageWidth);
            Assert.Equal(240, camera.ImageHeight);
            Assert.Equal(10, camera.FramesPerSecond);
            Assert.Equal(60, camera.FieldOfView);
            Assert.Empty(controls.CameraDisplays);
        }

        [Fact]
        public void SplitScreenItemsShareOneAutomaticDisplayPolicy()
        {
            var controls = Parse(@"
                ORTSCameraDisplay ( ORTSDisplay ( 2 ) CameraPage ( Cameras )
                    ActivateOnDoors ( 1 ) HideAboveSpeed ( 7km/h ) ReturnPage ( Status ) )
                ORTSCamera ( Position ( 10 20 160 120 ) ORTSDisplay ( 2 ) ORTSScreenPage ( Cameras )
                    CameraPosition ( -1.5 2 9 ) CameraDirection ( -175 -10 0 )
                    ImageSize ( 256 192 ) UpdateRate ( 8 ) FieldOfView ( 75 ) Mirror ( 1 ) CarOffset ( -1 ) )
                ORTSCamera ( Position ( 170 20 160 120 ) ORTSDisplay ( 2 ) ORTSScreenPage ( Cameras ) )
            ");
            Assert.Equal(2, controls.Count);
            var configuration = Assert.Single(controls.CameraDisplays);
            Assert.Equal(2, configuration.Display);
            Assert.Equal("cameras", configuration.CameraPage);
            Assert.True(configuration.ActivateOnDoors);
            Assert.Equal("status", configuration.ReturnPage);
            Assert.InRange(configuration.HideAboveSpeedMpS, 1.9444f, 1.9445f);
            var camera = Assert.IsType<CVCCamera>(controls[0]);
            Assert.Equal(new Vector3(-1.5f, 2, 9), camera.CameraPosition);
            Assert.Equal(new Vector3(-175, -10, 0), camera.CameraDirection);
            Assert.Equal(-1, camera.CarOffset);
            Assert.Equal(256, camera.ImageWidth);
            Assert.Equal(192, camera.ImageHeight);
            Assert.Equal(8, camera.FramesPerSecond);
            Assert.Equal(75, camera.FieldOfView);
            Assert.True(camera.Mirror);
            Assert.Equal("cameras", Assert.Single(camera.Screens));
            Assert.Equal(170, controls[1].PositionX);
        }

        [Fact]
        public void RearViewpointsCornersAndSelectionHotspotsAreParsed()
        {
            var controls = Parse(@"
                ORTSCamera ( RearCameraPosition ( 1.6 2.8 -10 ) RearCameraDirection ( 0 -10 0 )
                    ScreenCorners ( 10 20 100 25 95 90 15 85 ) )
                ORTSCameraSelect ( Position ( 20 30 15 10 ) ORTSDisplay ( 3 )
                    ORTSScreenPage ( Cameras ) ORTSScreenPage ( Default ) TargetPage ( Camera_Left )
                    Label ( ""Left"" ) DrawLabel ( 1 ) )
            ");
            var camera = Assert.IsType<CVCCamera>(controls[0]);
            Assert.Equal(new Vector3(1.6f, 2.8f, -10), camera.RearCameraPosition.Value);
            Assert.Equal(new Vector3(0, -10, 0), camera.RearCameraDirection.Value);
            Assert.Equal(4, camera.ScreenCorners.Length);
            Assert.Equal(new Vector2(95, 90), camera.ScreenCorners[2]);
            var button = Assert.IsType<CVCCameraSelect>(controls[1]);
            Assert.Equal(3, button.Display);
            Assert.Equal("camera_left", button.TargetPage);
            Assert.Equal("Left", button.Label);
            Assert.True(button.DrawLabel);
            Assert.Equal(new[] { "cameras", "default" }, button.Screens);
        }

        [Fact]
        public void InvalidCaptureSettingsAreBounded()
        {
            var controls = Parse("ORTSCamera ( ImageSize ( -1 99999 ) UpdateRate ( 0 ) FieldOfView ( 200 ) )");
            var camera = Assert.IsType<CVCCamera>(Assert.Single(controls));
            Assert.Equal(16, camera.ImageWidth);
            Assert.Equal(1024, camera.ImageHeight);
            Assert.Equal(1, camera.FramesPerSecond);
            Assert.Equal(120, camera.FieldOfView);
        }
    }
}
