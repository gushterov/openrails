// COPYRIGHT 2026 by the Open Rails project. GNU GPL version 3 or later.
using System;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using Microsoft.Xna.Framework;
using Orts.Viewer3D;
using Xunit;

namespace Tests.RunActivity
{
    public class CabNightTimingTests
    {
        // Model different day lengths and sun speeds, with crossings between sky samples.
        static Vector3[] SunPositions(double dawnHours, double duskHours, double speed = 1)
        {
            return Enumerable.Range(0, 72).Select(i => new Vector3(0,
                -0.085f + (float)(speed * Math.Min(i / 3.0 - dawnHours, duskHours - i / 3.0) / 24), 0)).ToArray();
        }

        [Theory]
        [InlineData(5.1, 20.3, 1)]
        [InlineData(8.2, 16.1, 0.4)]
        [InlineData(3.1, 22.2, 2)]
        [InlineData(0.2, 23.8, 1)]
        public void MorningSwitchIsThirtyMinutesLaterAndEveningSwitchIsThirtyMinutesEarlier(
            double dawnHours, double duskHours, double speed)
        {
            var positions = SunPositions(dawnHours, duskHours, speed);
            double morning = (dawnHours + 0.5) * 3600;
            double evening = (duskHours - 0.5) * 3600;

            Assert.True(SkyInterpolation.IsCabDark(positions, morning - 1));
            Assert.False(SkyInterpolation.IsCabDark(positions, morning + 1));
            Assert.False(SkyInterpolation.IsCabDark(positions, evening - 1));
            Assert.True(SkyInterpolation.IsCabDark(positions, evening + 1));
            Assert.False(SkyInterpolation.IsCabDark(positions, (dawnHours + duskHours) * 1800));
        }

        [Theory]
        [InlineData(-172800)]
        [InlineData(-86400)]
        [InlineData(0)]
        [InlineData(86400)]
        [InlineData(172800)]
        public void MidnightAndDayBoundariesWrapInBothDirections(double dayOffset)
        {
            var positions = SunPositions(0.2, 23.8);
            Assert.True(SkyInterpolation.IsCabDark(positions, dayOffset));
            Assert.True(SkyInterpolation.IsCabDark(positions, dayOffset + 0.7 * 3600 - 1));
            Assert.False(SkyInterpolation.IsCabDark(positions, dayOffset + 0.7 * 3600 + 1));
            Assert.False(SkyInterpolation.IsCabDark(positions, dayOffset + 23.3 * 3600 - 1));
            Assert.True(SkyInterpolation.IsCabDark(positions, dayOffset + 23.3 * 3600 + 1));
        }

        [Theory]
        [InlineData(0.5f, false)]
        [InlineData(-0.5f, true)]
        [InlineData(-0.085f, true)]
        public void PolarDayAndNightKeepTheirState(float height, bool expected)
        {
            var positions = Enumerable.Repeat(new Vector3(0, height, 0), 72).ToArray();
            Assert.Equal(expected, SkyInterpolation.IsCabDark(positions, 0));
            Assert.Equal(expected, SkyInterpolation.IsCabDark(positions, 43200));
        }

        [Fact]
        public void VeryShortNightBetweenWindowEndpointsStillKeepsCabDark()
        {
            var positions = Enumerable.Repeat(new Vector3(0, 0.1f, 0), 72).ToArray();
            positions[0].Y = -0.1f;
            Assert.True(SkyInterpolation.IsCabDark(positions, 0));
            Assert.False(SkyInterpolation.IsCabDark(positions, 3600));
        }

        [Theory]
        [InlineData(-2f)]
        [InlineData(0f)]
        [InlineData(2f)]
        public void CabUsesTheSameDaylightOffsetAsTheVisibleSky(float daylightOffset)
        {
            T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
            var viewer = Empty<Viewer>();
            var world = Empty<World>();
            world.WeatherControl = Empty<WeatherControl>();
            world.WeatherControl.DaylightOffset = daylightOffset;
            typeof(Viewer).GetField("<World>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(viewer, world);
            var programViewer = typeof(Viewer).Assembly.GetType("Orts.Program").GetField("Viewer");
            var previousViewer = programViewer.GetValue(null);
            programViewer.SetValue(null, viewer);
            try
            {
                var positions = SunPositions(6.1, 18.2);
                var interpolation = new SkyInterpolation { Step1 = 19, Step2 = 20, OldClockTime = 19 * 1200 };
                var sun = Vector3.Zero;
                var moon = Vector3.Zero;
                double switchTime = 6.6 * 3600 - daylightOffset * 3600;
                interpolation.SetSunAndMoonDirection(ref sun, ref moon, positions, positions, switchTime - 1);
                Assert.True(sun.Y > -0.085f); // The world is already in daylight.
                Assert.True(interpolation.CabIsDark);
                interpolation.SetSunAndMoonDirection(ref sun, ref moon, positions, positions, switchTime + 1);
                Assert.False(interpolation.CabIsDark);
            }
            finally
            {
                programViewer.SetValue(null, previousViewer);
            }
        }
    }
}
