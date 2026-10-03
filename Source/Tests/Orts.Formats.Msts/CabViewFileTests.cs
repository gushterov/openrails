// COPYRIGHT 2026 by the Open Rails project.
//
// This file is part of Open Rails.
//
// Open Rails is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Orts.Formats.Msts;
using System.IO;
using Xunit;

namespace Tests.Orts.Formats.Msts
{
    public class CabViewFileTests
    {
        [Theory]
        [InlineData("", 0f, true)]
        [InlineData("ORTSCycleDelay ( 0 )", 0f, true)]
        [InlineData("ORTSCycleDelay ( 2.5 )", 2.5f, true)]
        [InlineData("ORTSCycleDelay ( -2 )", 0f, true)]
        [InlineData("ORTSWiperFrameBlend ( 0 )", 0f, false)]
        [InlineData("ORTSWiperFrameBlend ( 1 )", 0f, true)]
        public void WiperTimingAndBlendingOptionsHaveCompatibleDefaults(string delay, float expected, bool blend)
        {
            var filePath = Path.GetTempFileName();
            try
            {
                File.WriteAllText(filePath, $@"
                    Tr_CabViewFile ( CabViewControls ( 1
                        ORTSAnimatedDisplay (
                            Type ( ORTS_2DEXTERNALWIPERS MULTI_STATE_DISPLAY )
                            Position ( 0 0 100 100 ) Graphic ( wiper.ace )
                            ORTSCycleTime ( 1.25 ) {delay}
                            States ( 2 2 1 State ( Style ( 0 ) SwitchVal ( 0 ) )
                                State ( Style ( 0 ) SwitchVal ( 1 ) ) )
                        )
                    ) )");
                var cab = new CabViewFile(filePath, Path.GetDirectoryName(filePath));
                var control = Assert.IsType<CVCAnimatedDisplay>(Assert.Single(cab.CabViewControls));
                Assert.Equal(expected, control.CycleDelayS);
                Assert.Equal(1.25f, control.CycleTimeS);
                Assert.Equal(blend, control.WiperFrameBlend);
            }
            finally { File.Delete(filePath); }
        }

        [Fact]
        public void WindowMasksFollowTheirViewsIncludingMissingAndEmptyEntries()
        {
            var filePath = Path.GetTempFileName();
            try
            {
                File.WriteAllText(filePath, @"
                Tr_CabViewFile (
                    CabViewWindowFile ( orphan.ace )
                    CabViewFile ( front.ace ) CabViewWindowFile ( front.ace )
                    CabViewFile ( left.ace )
                    CabViewFile ( right.ace ) CabViewWindowFile ( """" )
                    CabViewFile ( rear.ace ) CabViewWindowFile ( glass.dds )
                )");
                var folder = Path.GetDirectoryName(filePath);
                var cab = new CabViewFile(filePath, folder);
                Assert.Equal(4, cab.WindowViews.Count);
                Assert.Equal(cab.TwoDViews[0], cab.WindowViews[0]);
                Assert.Null(cab.WindowViews[1]);
                Assert.Null(cab.WindowViews[2]);
                Assert.Equal(Path.Combine(folder, "glass.dds"), cab.WindowViews[3]);
            }
            finally { File.Delete(filePath); }
        }

        [Fact]
        public void WindowMaskUsesMatchingHighResolutionVariant()
        {
            var folder = Path.Combine(Path.GetTempPath(), System.Guid.NewGuid().ToString());
            Directory.CreateDirectory(folder);
            try
            {
                var filePath = Path.Combine(folder, "cab.cvf");
                File.WriteAllText(Path.Combine(folder, "front1024.ace"), "");
                File.WriteAllText(filePath, "\nTr_CabViewFile ( CabViewFile ( front.ace ) CabViewWindowFile ( front.ace ) )");
                var cab = new CabViewFile(filePath, folder);
                Assert.Equal(Path.Combine(folder, "front1024.ace"), cab.WindowViews[0]);
                Assert.Equal(cab.TwoDViews[0], cab.WindowViews[0]);
            }
            finally { Directory.Delete(folder, true); }
        }

        [Theory]
        [InlineData("PANTOGRAPH")]
        [InlineData("PANTOGRAPH2")]
        [InlineData("ORTS_PANTOGRAPH3")]
        [InlineData("ORTS_PANTOGRAPH4")]
        public void SprungPantographTriStatePreservesStyle(string controlType)
        {
            var control = ParseControl("TriState", controlType, "TRI_STATE", "SPRUNG");

            Assert.Equal(DiscreteStates.TRI_STATE, control.DiscreteState);
            Assert.Equal(CABViewControlStyles.SPRUNG, control.ControlStyle);
            Assert.Equal(2, control.MaxValue);
        }

        [Fact]
        public void SprungPantographTwoStateRetainsLegacyOnOffStyle()
        {
            var control = ParseControl("TwoState", "PANTOGRAPH", "TWO_STATE", "SPRUNG");

            Assert.Equal(DiscreteStates.TWO_STATE, control.DiscreteState);
            Assert.Equal(CABViewControlStyles.ONOFF, control.ControlStyle);
        }

        [Fact]
        public void SprungCircuitBreakerTriStatePreservesStyle()
        {
            var control = ParseControl("TriState", "ORTS_CIRCUIT_BREAKER_DRIVER_COMMAND", "TRI_STATE", "SPRUNG");

            Assert.Equal(CABViewControlTypes.ORTS_CIRCUIT_BREAKER_DRIVER_COMMAND, control.ControlType.Type);
            Assert.Equal(DiscreteStates.TRI_STATE, control.DiscreteState);
            Assert.Equal(CABViewControlStyles.SPRUNG, control.ControlStyle);
            Assert.Equal(2, control.MaxValue);
        }

        static CVCDiscrete ParseControl(string controlBlock, string controlType, string discreteType, string style)
        {
            var filePath = Path.GetTempFileName();
            try
            {
                File.WriteAllText(filePath, $@"
                    Tr_CabViewFile (
                        CabViewControls ( 1
                            {controlBlock} (
                                Type ( {controlType} {discreteType} )
                                Position ( 0 0 16 16 )
                                Graphic ( dummy.ace )
                                NumFrames ( 3 3 1 )
                                Style ( {style} )
                                MouseControl ( 1 )
                            )
                        )
                    )");

                var cabViewFile = new CabViewFile(filePath, Path.GetDirectoryName(filePath));
                return Assert.IsType<CVCDiscrete>(Assert.Single(cabViewFile.CabViewControls));
            }
            finally
            {
                File.Delete(filePath);
            }
        }
    }
}
