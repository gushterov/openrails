// COPYRIGHT 2026 by the Open Rails project. GNU GPL version 3 or later.
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using Orts.Formats.Msts;
using Orts.Viewer3D.RollingStock;
using Tests.Orts.Simulation;
using Xunit;

namespace Tests.RunActivity
{
    public class CoastingCabRendererTests
    {
        [Theory]
        [InlineData(CABViewControlTypes.ORTS_COASTING)]
        [InlineData(CABViewControlTypes.ORTS_PARKING_BRAKE)]
        public void IndicatorUsesUnlitAndLitFrames(CABViewControlTypes type)
        {
            var path = Path.GetTempFileName();
            try
            {
                File.WriteAllText(path, $@"
                    Tr_CabViewFile ( CabViewControls ( 1
                    TwoState ( Type ( {type} TWO_STATE ) Position ( 100 100 32 32 )
                        Graphic ( coasting.ace ) NumFrames ( 2 2 1 ) Style ( NONE ) MouseControl ( 0 ) )
                ) )");
                var control = (CVCDiscrete)new CabViewFile(path, Path.GetDirectoryName(path)).CabViewControls[0];
                var locomotive = type == CABViewControlTypes.ORTS_COASTING
                    ? CoastingCabControlTests.CreateLocomotive(0, -1, 0, true, true)
                    : ParkingBrakeCabControlTests.CreateLocomotive();
                var renderer = (CabViewDiscreteRenderer)FormatterServices.GetUninitializedObject(typeof(CabViewDiscreteRenderer));
                typeof(CabViewControlRenderer).GetField("Control").SetValue(renderer, control);
                typeof(CabViewControlRenderer).GetField("Locomotive", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(renderer, locomotive);
                typeof(CabViewDiscreteRenderer).GetField("ControlDiscrete", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(renderer, control);
                if (type == CABViewControlTypes.ORTS_COASTING)
                {
                    Assert.Equal(1, renderer.GetDrawIndex());
                    locomotive.CruiseControl.SelectedMaxAccelerationPercent = 50;
                    Assert.Equal(0, renderer.GetDrawIndex());
                    locomotive.CruiseControl.SelectedMaxAccelerationPercent = 0;
                    Assert.Equal(1, renderer.GetDrawIndex());
                }
                else
                {
                    Assert.Equal(0, renderer.GetDrawIndex());
                    locomotive.Train.SetHandbrakePercent(100);
                    Assert.Equal(1, renderer.GetDrawIndex());
                    locomotive.Train.SetHandbrakePercent(0);
                    Assert.Equal(0, renderer.GetDrawIndex());
                }
                Assert.True(type < CABViewControlTypes.EXTERNALWIPERS);
            }
            finally { File.Delete(path); }
        }
    }
}
