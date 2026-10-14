// COPYRIGHT 2026 by the Open Rails project. GNU GPL version 3 or later.
using Orts.Formats.Msts;

namespace Orts.Formats.OR
{
    public enum WeatherPreset
    {
        Default = 0, Cloudy, Overcast, Foggy, Rainy, HeavyPrecipitation, Storm, Dynamic,
    }

    /// <summary>The first three menu keys retain MSTS weather values; additional keys select presets.</summary>
    public static class WeatherPresetSelection
    {
        public static WeatherPreset FromMenuKey(int key) => key >= 3 && key <= 9 ? (WeatherPreset)(key - 2) : WeatherPreset.Default;

        public static WeatherType BaseWeather(int key, SeasonType season)
        {
            if (key >= 0 && key <= 2) return (WeatherType)key;
            var preset = FromMenuKey(key);
            return preset >= WeatherPreset.Rainy && preset <= WeatherPreset.Storm
                ? (season == SeasonType.Winter ? WeatherType.Snow : WeatherType.Rain) : WeatherType.Clear;
        }
    }
}
