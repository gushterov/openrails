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
