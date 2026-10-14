// COPYRIGHT 2026 by the Open Rails project. GNU GPL version 3 or later.
using System;
using System.IO;
using Microsoft.Xna.Framework;
using Orts.Formats.Msts;
using Orts.Formats.OR;
using Orts.MultiPlayer;
using Orts.Simulation;
using Xunit;

namespace Tests.Orts.Simulation
{
    public class WeatherPresetTests
    {
        [Theory]
        [InlineData(0, WeatherType.Clear)]
        [InlineData(1, WeatherType.Snow)]
        [InlineData(2, WeatherType.Rain)]
        public void ExistingMenuChoicesRetainMstsWeatherValues(int key, WeatherType type)
        {
            Assert.Equal(type, WeatherPresetSelection.BaseWeather(key, SeasonType.Summer));
            Assert.Equal(WeatherPreset.Default, WeatherPresetSelection.FromMenuKey(key));
        }

        [Fact]
        public void AdvancedMenuChoicesNeverEscapeTheMstsWeatherEnum()
        {
            foreach (SeasonType season in Enum.GetValues(typeof(SeasonType)))
            for (var key = 3; key <= 9; key++)
            {
                Assert.True(Enum.IsDefined(typeof(WeatherType), WeatherPresetSelection.BaseWeather(key, season)));
                Assert.NotEqual(WeatherPreset.Default, WeatherPresetSelection.FromMenuKey(key));
            }
            Assert.Equal(WeatherType.Snow, WeatherPresetSelection.BaseWeather(8, SeasonType.Winter));
            Assert.Equal(WeatherType.Rain, WeatherPresetSelection.BaseWeather(8, SeasonType.Summer));
        }

        [Fact]
        public void AllSeasonalPresetsRespectRendererBoundsAndTemperature()
        {
            var catalog = WeatherPresetCatalog.Defaults();
            foreach (SeasonType season in Enum.GetValues(typeof(SeasonType)))
            foreach (var name in catalog.Presets.Keys)
            {
                var state = catalog.Create(name, season, 250, Vector2.UnitX);
                Assert.InRange(state.CloudCoverFactor, 0, 1);
                Assert.InRange(state.VisibilityM, 10, 100000);
                Assert.InRange(state.PrecipitationIntensityPPSPM2, 0, 0.015f);
                Assert.Equal(season == SeasonType.Winter ? 0 : 1, state.PrecipitationLiquidity);
                Assert.Equal(250, state.FogBaseHeightM);
            }
        }

        [Fact]
        public void InvalidConfigurationCannotReplaceSafeDefaults()
        {
            var path = Path.Combine(Path.GetTempPath(), "weather-" + Guid.NewGuid() + ".json");
            try
            {
                File.WriteAllText(path, "{\"Presets\":{\"Storm\":{\"CloudCover\":2,\"VisibilityM\":-1}}}");
                WeatherPresetCatalog catalog = null;
                AssertWarnings.Matching("Invalid weather preset", () => catalog = WeatherPresetCatalog.Load(path));
                Assert.Equal(1, catalog.Presets["Storm"].CloudCover);
                Assert.Equal(1000, catalog.Presets["Storm"].VisibilityM);
                File.WriteAllText(path, "broken json");
                AssertWarnings.Matching("Cannot load weather presets", () => Assert.True(WeatherPresetCatalog.Load(path).Presets["Rainy"].IsValid));
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void ClearFreeRunsUseTheEditableClearProfile()
        {
            var catalog = WeatherPresetCatalog.Defaults();
            catalog.Presets["Clear"].VisibilityM = 35000;
            var weather = new Weather();
            new WeatherPresetController(catalog, WeatherPreset.Default, SeasonType.Winter, 88, weather, 300);
            Assert.Equal(31500, weather.VisibilityM);
            Assert.Equal(0, weather.PrecipitationIntensityPPSPM2);
            Assert.Equal(-4, weather.TemperatureC);
        }

        [Fact]
        public void WeatherSnapshotIsIndependentOfLaterSimulationUpdates()
        {
            var weather = new Weather { CloudCoverFactor = 0.4f, VisibilityM = 8000 };
            var snapshot = weather.Capture();
            weather.CloudCoverFactor = 1;
            weather.VisibilityM = 100;
            Assert.Equal(0.4f, snapshot.CloudCoverFactor);
            Assert.Equal(8000, snapshot.VisibilityM);
        }

        [Fact]
        public void DynamicWeatherResumesItsRandomSequenceAndTransition()
        {
            var catalog = WeatherPresetCatalog.Defaults();
            var original = new Weather();
            var controller = new WeatherPresetController(catalog, WeatherPreset.Dynamic, SeasonType.Autumn, 12345, original, 270);
            controller.Update(2100, original);
            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
            {
                original.Capture().Save(writer);
                controller.Save(writer);
            }
            stream.Position = 0;
            using var reader = new BinaryReader(stream);
            var restored = new Weather();
            WeatherSnapshot.Restore(reader).Apply(restored);
            var resumed = WeatherPresetController.Restore(catalog, reader);
            for (var second = 0; second < 12000; second += 7)
            {
                controller.Update(7, original);
                resumed.Update(7, restored);
                Assert.Equal(original.CloudCoverFactor, restored.CloudCoverFactor);
                Assert.Equal(original.VisibilityM, restored.VisibilityM);
                Assert.Equal(original.PrecipitationIntensityPPSPM2, restored.PrecipitationIntensityPPSPM2);
                Assert.Equal(original.WindAverageSpeedMpS, restored.WindAverageSpeedMpS);
            }
        }

        [Fact]
        public void DynamicTransitionsAreIndependentOfUpdateFrequency()
        {
            var catalog = WeatherPresetCatalog.Defaults();
            var fast = new Weather(); var slow = new Weather();
            var a = new WeatherPresetController(catalog, WeatherPreset.Dynamic, SeasonType.Spring, 77, fast, 10);
            var b = new WeatherPresetController(catalog, WeatherPreset.Dynamic, SeasonType.Spring, 77, slow, 10);
            for (var i = 0; i < 10800; i++) a.Update(1, fast);
            for (var i = 0; i < 1080; i++) b.Update(10, slow);
            Assert.InRange(Math.Abs(fast.CloudCoverFactor - slow.CloudCoverFactor), 0, 0.001f);
            Assert.InRange(Math.Abs(fast.VisibilityM - slow.VisibilityM), 0, 2);
        }

        [Fact]
        public void CloudsThickenBeforeRainStartsAndPausedWeatherDoesNotAdvance()
        {
            var catalog = WeatherPresetCatalog.Defaults();
            catalog.Seasons["Summer"].WetWeatherProbability = 1;
            var weather = new Weather();
            var controller = new WeatherPresetController(catalog, WeatherPreset.Dynamic, SeasonType.Summer, 77, weather, 0);
            controller.Update(0, weather);
            Assert.Equal(0.4f, weather.CloudCoverFactor);
            for (var i = 0; i < 6000; i++)
            {
                controller.Update(1, weather);
                if (weather.PrecipitationIntensityPPSPM2 > 0)
                {
                    Assert.True(weather.CloudCoverFactor >= 0.6f);
                    return;
                }
            }
            Assert.True(false, "Wet climate never developed precipitation");
        }

        [Theory]
        [InlineData(0, 0)]
        [InlineData(0, 300)]
        [InlineData(500, 100)]
        [InlineData(-50, 400)]
        public void HeightFogIntegratesTheWholeRayAndIsReciprocal(float startHeight, float endHeight)
        {
            var density = AtmosphereState.FogDensity(startHeight, endHeight, 50, 100);
            Assert.InRange(density, 0, 1);
            Assert.Equal(density, AtmosphereState.FogDensity(endHeight, startHeight, 50, 100), 5);
            if (startHeight != endHeight)
            {
                var endpointDensity = (float)Math.Exp(-Math.Max(endHeight - 50, 0) / 100);
                Assert.NotEqual(endpointDensity, density);
            }
        }

        [Fact]
        public void FogDensitySupportsHorizontalAndUniformFogWithoutSingularities()
        {
            Assert.Equal(1, AtmosphereState.FogDensity(0, 1000, 0, 0));
            Assert.Equal((float)Math.Exp(-1), AtmosphereState.FogDensity(100, 100, 0, 100), 5);
            Assert.Equal(1, AtmosphereState.FogDensity(-100, -20, 0, 100));
        }

        [Fact]
        public void SkyAndFogUseOneWeatherSnapshotAcrossDayNightAndOvercast()
        {
            foreach (var sunHeight in new[] { -1f, -0.1f, 0f, 0.2f, 1f })
            foreach (var cover in new[] { 0f, 0.5f, 1f })
            {
                var atmosphere = new AtmosphereState(new Weather { VisibilityM = 10000, CloudCoverFactor = cover }.Capture(),
                    new Vector3(0, sunHeight, (float)Math.Sqrt(1 - sunHeight * sunHeight)), 100);
                Assert.InRange(atmosphere.HorizonColor.X, 0, 1);
                Assert.InRange(atmosphere.HorizonColor.Y, 0, 1);
                Assert.InRange(atmosphere.ZenithColor.Z, 0, 1);
                Assert.Equal(new Color(atmosphere.HorizonColor), atmosphere.FogColor);
            }
        }

        [Fact]
        public void MultiplayerReadsOldPacketsAndRoundTripsAtmosphericFields()
        {
            var legacy = new MSGWeather("2 0.8 0.01 1500");
            Assert.False(legacy.Atmosphere.HasValue);
            var weather = new Weather { VisibilityM = 3000, CloudCoverFactor = 0.8f, PrecipitationIntensityPPSPM2 = 0.01f,
                PrecipitationLiquidity = 0, TemperatureC = -4, FogBaseHeightM = 400, FogHeightScaleM = 50,
                WindAverageDirection = Vector2.UnitX, WindAverageSpeedMpS = 8, WindInstantaneousSpeedMpS = 10 };
            var serialized = new MSGWeather(1, weather.Capture()).ToString();
            var payload = serialized.Substring(serialized.IndexOf("WEATHER ", StringComparison.Ordinal) + 8);
            var message = new MSGWeather(payload);
            Assert.Equal(1, message.weather);
            Assert.Equal(weather.VisibilityM, message.fog);
            Assert.Equal(weather.TemperatureC, message.Atmosphere.Value.TemperatureC);
            Assert.Equal(weather.FogHeightScaleM, message.Atmosphere.Value.FogHeightScaleM);
            Assert.Equal(weather.PrecipitationLiquidity, message.Atmosphere.Value.PrecipitationLiquidity);
        }

        [Fact]
        public void ExistingAmbientBrightnessControlsSkyAndCloudsWithoutChangingWeather()
        {
            var snapshot = new Weather { VisibilityM = 20000, CloudCoverFactor = 0.4f }.Capture();
            var sun = Vector3.Normalize(new Vector3(0, 1, -1));
            var full = new AtmosphereState(snapshot, sun, 100, 20);
            var dim = new AtmosphereState(snapshot, sun, 100, 10);
            Assert.InRange(Vector3.Distance(full.ZenithColor * 0.5f, dim.ZenithColor), 0, 0.000001f);
            Assert.InRange(Vector3.Distance(full.CloudLightColor * 0.5f, dim.CloudLightColor), 0, 0.000001f);
            Assert.Equal(full.Weather.CloudCoverFactor, dim.Weather.CloudCoverFactor);
        }
    }
}
