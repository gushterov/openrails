// COPYRIGHT 2026 by the Open Rails project. GNU GPL version 3 or later.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Microsoft.Xna.Framework;
using Newtonsoft.Json;
using Orts.Formats.Msts;
using Orts.Formats.OR;

namespace Orts.Simulation
{
    public sealed class WeatherPresetDefinition
    {
        public float CloudCover { get; set; }
        public float VisibilityM { get; set; }
        public float PrecipitationPPSPM2 { get; set; }
        public float WindSpeedMpS { get; set; }
        public float FogHeightScaleM { get; set; }

        public bool IsValid => Finite(CloudCover) && CloudCover >= 0 && CloudCover <= 1
            && Finite(VisibilityM) && VisibilityM >= 10 && VisibilityM <= 100000
            && Finite(PrecipitationPPSPM2) && PrecipitationPPSPM2 >= 0 && PrecipitationPPSPM2 <= 0.015f
            && Finite(WindSpeedMpS) && WindSpeedMpS >= 0 && WindSpeedMpS <= 40
            && Finite(FogHeightScaleM) && FogHeightScaleM >= 0 && FogHeightScaleM <= 5000;

        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }

    public sealed class SeasonalWeatherDefinition
    {
        public float TemperatureC { get; set; }
        public float VisibilityMultiplier { get; set; } = 1;
        public float WetWeatherProbability { get; set; } = 0.4f;
        public bool IsValid => !float.IsNaN(TemperatureC) && TemperatureC >= -60 && TemperatureC <= 60
            && !float.IsNaN(VisibilityMultiplier) && VisibilityMultiplier >= 0.1f && VisibilityMultiplier <= 3
            && !float.IsNaN(WetWeatherProbability) && WetWeatherProbability >= 0 && WetWeatherProbability <= 1;
    }

    /// <summary>Editable visual weather profiles, with bounded defaults when a content file is absent or invalid.</summary>
    public sealed class WeatherPresetCatalog
    {
        public Dictionary<string, WeatherPresetDefinition> Presets { get; set; } = new Dictionary<string, WeatherPresetDefinition>();
        public Dictionary<string, SeasonalWeatherDefinition> Seasons { get; set; } = new Dictionary<string, SeasonalWeatherDefinition>();

        public static WeatherPresetCatalog Defaults()
        {
            var result = new WeatherPresetCatalog();
            result.Presets["Clear"] = Definition(0.05f, 24000, 0, 2, 600);
            result.Presets["Cloudy"] = Definition(0.4f, 18000, 0, 4, 450);
            result.Presets["Overcast"] = Definition(0.95f, 10000, 0, 5, 250);
            result.Presets["Foggy"] = Definition(0.3f, 500, 0, 1, 45);
            result.Presets["Rainy"] = Definition(0.85f, 5500, 0.006f, 6, 180);
            result.Presets["HeavyPrecipitation"] = Definition(0.98f, 1800, 0.012f, 9, 120);
            result.Presets["Storm"] = Definition(1, 1000, 0.015f, 15, 100);
            result.Seasons["Spring"] = Season(10, 1, 0.4f);
            result.Seasons["Summer"] = Season(22, 1.2f, 0.25f);
            result.Seasons["Autumn"] = Season(7, 0.8f, 0.55f);
            result.Seasons["Winter"] = Season(-4, 0.9f, 0.45f);
            return result;
        }

        static WeatherPresetDefinition Definition(float cover, float visibility, float rain, float wind, float fogHeight)
            => new WeatherPresetDefinition { CloudCover = cover, VisibilityM = visibility, PrecipitationPPSPM2 = rain, WindSpeedMpS = wind, FogHeightScaleM = fogHeight };
        static SeasonalWeatherDefinition Season(float temperature, float visibility, float wetProbability)
            => new SeasonalWeatherDefinition { TemperatureC = temperature, VisibilityMultiplier = visibility, WetWeatherProbability = wetProbability };

        public static WeatherPresetCatalog Load(string path)
        {
            var result = Defaults();
            if (!File.Exists(path)) return result;
            try
            {
                var custom = JsonConvert.DeserializeObject<WeatherPresetCatalog>(File.ReadAllText(path));
                if (custom == null) throw new InvalidDataException("Empty weather preset catalog");
                foreach (var entry in custom.Presets ?? new Dictionary<string, WeatherPresetDefinition>())
                {
                    if (result.Presets.ContainsKey(entry.Key) && entry.Value?.IsValid == true) result.Presets[entry.Key] = entry.Value;
                    else Trace.TraceWarning("Invalid weather preset '{0}' in {1}; using defaults", entry.Key, path);
                }
                foreach (var entry in custom.Seasons ?? new Dictionary<string, SeasonalWeatherDefinition>())
                {
                    if (result.Seasons.ContainsKey(entry.Key) && entry.Value?.IsValid == true) result.Seasons[entry.Key] = entry.Value;
                    else Trace.TraceWarning("Invalid weather season '{0}' in {1}; using defaults", entry.Key, path);
                }
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is JsonException)
            {
                Trace.TraceWarning("Cannot load weather presets from {0}: {1}; using defaults", path, error.Message);
            }
            return result;
        }

        public WeatherSnapshot Create(string preset, SeasonType season, float groundHeight, Vector2 windDirection)
        {
            var definition = Presets[preset];
            var climate = Seasons[season.ToString()];
            return new WeatherSnapshot(new Weather
            {
                CloudCoverFactor = definition.CloudCover,
                VisibilityM = MathHelper.Clamp(definition.VisibilityM * climate.VisibilityMultiplier, 10, 100000),
                PrecipitationIntensityPPSPM2 = definition.PrecipitationPPSPM2,
                PrecipitationLiquidity = MathHelper.Clamp((climate.TemperatureC + 1) / 3, 0, 1),
                TemperatureC = climate.TemperatureC,
                FogBaseHeightM = groundHeight, FogHeightScaleM = definition.FogHeightScaleM,
                WindAverageDirection = windDirection, WindInstantaneousDirection = windDirection,
                WindAverageSpeedMpS = definition.WindSpeedMpS, WindInstantaneousSpeedMpS = definition.WindSpeedMpS,
            });
        }
    }

    /// <summary>A simulation-time controller. Its private PRNG and transition endpoints survive save/resume.</summary>
    public sealed class WeatherPresetController
    {
        public WeatherPreset Preset { get; private set; }
        readonly WeatherPresetCatalog Catalog;
        readonly SeasonType Season;
        uint RandomState;
        float HoldSeconds, TransitionSeconds, ElapsedSeconds;
        WeatherSnapshot Start, Target;

        public WeatherPresetController(WeatherPresetCatalog catalog, WeatherPreset preset, SeasonType season, uint seed, Weather weather, float groundHeight)
        {
            Catalog = catalog; Preset = preset; Season = season; RandomState = seed == 0 ? 1u : seed;
            var angle = Next() * MathHelper.TwoPi;
            var direction = new Vector2((float)Math.Sin(angle), -(float)Math.Cos(angle));
            Target = Catalog.Create(preset == WeatherPreset.Dynamic ? "Cloudy" : preset == WeatherPreset.Default ? "Clear" : preset.ToString(), season, groundHeight, direction);
            Target.Apply(weather);
            Start = Target;
            HoldSeconds = 600 + Next() * 1200;
        }

        WeatherPresetController(WeatherPresetCatalog catalog, BinaryReader reader)
        {
            Catalog = catalog; Preset = (WeatherPreset)reader.ReadInt32(); Season = (SeasonType)reader.ReadInt32();
            RandomState = reader.ReadUInt32(); HoldSeconds = reader.ReadSingle();
            TransitionSeconds = reader.ReadSingle(); ElapsedSeconds = reader.ReadSingle();
            Start = WeatherSnapshot.Restore(reader); Target = WeatherSnapshot.Restore(reader);
        }

        float Next()
        {
            RandomState ^= RandomState << 13; RandomState ^= RandomState >> 17; RandomState ^= RandomState << 5;
            return (RandomState >> 8) / 16777216f;
        }

        public void Update(float seconds, Weather weather)
        {
            if (Preset != WeatherPreset.Dynamic || seconds <= 0 || float.IsNaN(seconds) || float.IsInfinity(seconds)) return;
            while (seconds > 0)
            {
                if (HoldSeconds > 0)
                {
                    var step = Math.Min(seconds, HoldSeconds);
                    HoldSeconds -= step; seconds -= step;
                    if (HoldSeconds > 0 || seconds == 0) return;
                }
                if (TransitionSeconds == 0)
                {
                    Start = weather.Capture();
                    var wet = Next() < Catalog.Seasons[Season.ToString()].WetWeatherProbability;
                    var choice = Next();
                    var name = wet ? (choice < 0.75f ? "Rainy" : choice < 0.95f ? "HeavyPrecipitation" : "Storm")
                        : (choice < 0.35f ? "Clear" : choice < 0.75f ? "Cloudy" : "Overcast");
                    Target = Catalog.Create(name, Season, Start.FogBaseHeightM, Start.WindAverageDirection);
                    TransitionSeconds = 300 + Next() * 600;
                    ElapsedSeconds = 0;
                }
                var advance = Math.Min(seconds, TransitionSeconds - ElapsedSeconds);
                ElapsedSeconds += advance; seconds -= advance;
                var progress = MathHelper.Clamp(ElapsedSeconds / TransitionSeconds, 0, 1);
                var blend = progress * progress * (3 - 2 * progress);
                weather.CloudCoverFactor = MathHelper.Lerp(Start.CloudCoverFactor, Target.CloudCoverFactor, blend);
                // Interpolate extinction, avoiding an abrupt visibility collapse near the end of a transition.
                weather.VisibilityM = 1 / MathHelper.Lerp(1 / Start.VisibilityM, 1 / Target.VisibilityM, blend);
                var rainBlend = Target.PrecipitationIntensityPPSPM2 > Start.PrecipitationIntensityPPSPM2
                    ? MathHelper.Clamp((progress - 0.35f) / 0.65f, 0, 1) : MathHelper.Clamp(progress / 0.65f, 0, 1);
                rainBlend *= rainBlend * (3 - 2 * rainBlend);
                if (Start.PrecipitationIntensityPPSPM2 == 0 && Target.PrecipitationIntensityPPSPM2 > 0)
                    rainBlend = Math.Min(rainBlend, MathHelper.Clamp((weather.CloudCoverFactor - 0.6f) / Math.Max(0.01f, Target.CloudCoverFactor - 0.6f), 0, 1));
                weather.PrecipitationIntensityPPSPM2 = MathHelper.Lerp(Start.PrecipitationIntensityPPSPM2, Target.PrecipitationIntensityPPSPM2, rainBlend);
                weather.PrecipitationLiquidity = MathHelper.Lerp(Start.PrecipitationLiquidity, Target.PrecipitationLiquidity, blend);
                weather.TemperatureC = MathHelper.Lerp(Start.TemperatureC, Target.TemperatureC, blend);
                weather.FogHeightScaleM = MathHelper.Lerp(Start.FogHeightScaleM, Target.FogHeightScaleM, blend);
                weather.WindAverageSpeedMpS = MathHelper.Lerp(Start.WindAverageSpeedMpS, Target.WindAverageSpeedMpS, blend);
                if (progress >= 1)
                {
                    Target.Apply(weather); TransitionSeconds = 0; ElapsedSeconds = 0;
                    HoldSeconds = 600 + Next() * 1800;
                }
            }
        }

        public void Save(BinaryWriter writer)
        {
            writer.Write((int)Preset); writer.Write((int)Season); writer.Write(RandomState);
            writer.Write(HoldSeconds); writer.Write(TransitionSeconds); writer.Write(ElapsedSeconds);
            Start.Save(writer); Target.Save(writer);
        }

        public static WeatherPresetController Restore(WeatherPresetCatalog catalog, BinaryReader reader) => new WeatherPresetController(catalog, reader);
    }
}
