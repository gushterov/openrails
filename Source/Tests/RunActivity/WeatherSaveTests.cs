// COPYRIGHT 2026 by the Open Rails project. GNU GPL version 3 or later.
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using Microsoft.Xna.Framework;
using Orts.Formats.Msts;
using Orts.Formats.OR;
using Orts.Simulation;
using Orts.Viewer3D;
using Orts.Viewer3D.Processes;
using ORTS.Common;
using Xunit;

namespace Tests.RunActivity
{
    public class WeatherSaveTests
    {
        static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
        static void Set(object instance, string name, object value) => instance.GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SetValue(instance, value);

        static WeatherControl Control()
        {
            var control = Empty<WeatherControl>();
            var viewer = Empty<Viewer>();
            var simulation = Empty<Simulator>();
            simulation.Weather = new Weather();
            var sound = Empty<SoundProcess>();
            Set(sound, "SoundSources", new Dictionary<object, List<SoundSourceBase>>());
            Set(viewer, "<SoundProcess>k__BackingField", sound);
            Set(viewer, "<Simulator>k__BackingField", simulation);
            Set(control, "Viewer", viewer); Set(control, "Weather", simulation.Weather);
            Set(control, "RainSound", new List<SoundSourceBase>());
            Set(control, "SnowSound", new List<SoundSourceBase>());
            Set(control, "ClearSound", new List<SoundSourceBase>());
            return control;
        }

        [Fact]
        public void LoadingAnOldWeatherBlockConsumesExactlyItsOriginalFields()
        {
            var control = Control();
            // Simulate a newly selected menu preset before loading an old saved run.
            Set(control, "PresetController", new WeatherPresetController(WeatherPresetCatalog.Defaults(), WeatherPreset.Storm,
                SeasonType.Summer, 123, control.Weather, 200));
            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
            {
                writer.Write(0); writer.Write(8500f); writer.Write(0.45f); writer.Write(0f); writer.Write(1f);
                writer.Write(false); writer.Write(false); writer.Write(123456);
            }
            stream.Position = 0;
            using var reader = new BinaryReader(stream);
            control.RestoreWeatherParameters(reader);
            Assert.Equal(123456, reader.ReadInt32());
            Assert.Equal(8500, control.Weather.VisibilityM);
            Assert.Equal(0.45f, control.Weather.CloudCoverFactor);
            Assert.Equal(0, control.Weather.FogHeightScaleM);
            Assert.Null(typeof(WeatherControl).GetField("PresetController", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(control));
        }

        [Fact]
        public void NewWeatherBlocksRestoreAtmosphereCloudPositionAndController()
        {
            var control = Control();
            Set(control, "PresetController", new WeatherPresetController(WeatherPresetCatalog.Defaults(), WeatherPreset.Dynamic,
                SeasonType.Winter, 456, control.Weather, 300));
            Set(control, "CloudPositionM", new Vector2(100, 200));
            Set(control, "CameraWorldLocation", new WorldLocation(150, -220, 25, 300, 50));
            Set(control, "PresetCatalog", WeatherPresetCatalog.Defaults());
            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true)) control.SaveWeatherParameters(writer);
            stream.Position = 0;
            var restored = Control(); Set(restored, "PresetCatalog", WeatherPresetCatalog.Defaults());
            using var reader = new BinaryReader(stream);
            restored.RestoreWeatherParameters(reader);
            Assert.Equal(stream.Length, stream.Position);
            Assert.Equal(control.Weather.TemperatureC, restored.Weather.TemperatureC);
            Assert.Equal(control.Weather.FogBaseHeightM, restored.Weather.FogBaseHeightM);
            Assert.Equal(control.Weather.FogHeightScaleM, restored.Weather.FogHeightScaleM);
            var position = typeof(WeatherControl).GetField("CloudPositionM", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.Equal(position.GetValue(control), position.GetValue(restored));
            var anchor = typeof(WeatherControl).GetField("CameraWorldLocation", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.Equal(anchor.GetValue(control), anchor.GetValue(restored));
            Assert.NotNull(typeof(WeatherControl).GetField("PresetController", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(restored));
        }
    }
}
