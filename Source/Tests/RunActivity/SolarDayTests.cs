using System;
using System.IO;
using System.Reflection;
using Microsoft.Xna.Framework;
using Orts.Viewer3D;
using Xunit;

namespace Tests.RunActivity
{
    public class SolarDayTests
    {
        static readonly Assembly Runner = typeof(SkyViewer).Assembly;
        static readonly Type DayType = Runner.GetType("Orts.Viewer3D.Common.SolarDay", true);
        static readonly MethodInfo Parse = DayType.GetMethod("Parse", BindingFlags.Static | BindingFlags.NonPublic);
        static readonly MethodInfo SolarAngle = Runner.GetType("Orts.Viewer3D.Common.SunMoonPos", true).GetMethod("SolarAngle");

        static object FromApi(string sunrise, string sunset) =>
            Parse.Invoke(null, new object[] { $$"""{"sunrise":{{sunrise}},"sunset":{{sunset}}}""", new DateOnly(2026, 6, 21), 60.0 });

        static float Height(object day, float hour) =>
            ((Vector3)SolarAngle.Invoke(null, new object[] { 0.0, 0.0, null, hour / 24, new SkyViewer.SkyDate(), day })).Y;

        [Fact]
        public void UsesLocalClockTimesForSunsetAfterMidnight()
        {
            var day = FromApi("\"2026-06-21T04:30:00+03:00\"", "\"2026-06-22T00:15:00+03:00\"");
            Assert.NotNull(day);
            Assert.InRange(Height(day, 4.5f), -0.00001f, 0.00001f);
            Assert.InRange(Height(day, 0.25f), -0.00001f, 0.00001f);
            Assert.True(Height(day, 0) > 0);
            Assert.True(Height(day, 1) < 0);
            Assert.True(Height(day, 12) > 0);
        }

        [Fact]
        public void MissingSunsetFallsBackToRoute()
        {
            Assert.Null(FromApi("\"2026-06-21T04:30:00+03:00\"", "null"));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void SaveFooterRestoresDaylightWithoutMovingTheReader(bool hasDaylight)
        {
            var day = FromApi("\"2026-06-21T04:30:00+03:00\"", "\"2026-06-21T21:15:00+03:00\"");
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            writer.Write(new byte[100]); // Existing save data.
            writer.Write(stream.Position);
            if (hasDaylight)
                DayType.GetMethod("WriteToSave").Invoke(day, new object[] { writer });
            stream.Position = 10;
            using var reader = new BinaryReader(stream);

            var restored = DayType.GetMethod("ReadFromSave").Invoke(null, new object[] { reader });

            Assert.Equal(10L, stream.Position);
            if (hasDaylight)
                Assert.Equal(Height(day, 21), Height(restored, 21));
            else
                Assert.Null(restored);
        }
    }
}
