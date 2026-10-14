// COPYRIGHT 2026 by the Open Rails project. GNU GPL version 3 or later.
using System;
using Microsoft.Xna.Framework;

namespace Orts.Simulation
{
    /// <summary>Pure atmospheric lighting derived once from the frame's weather and solar position.</summary>
    public sealed class AtmosphereState
    {
        public WeatherSnapshot Weather { get; }
        public Vector3 SolarDirection { get; }
        public Vector3 ZenithColor { get; }
        public Vector3 HorizonColor { get; }
        public Vector3 SunColor { get; }
        public Vector3 CloudLightColor { get; }
        public Vector3 CloudShadeColor { get; }
        public float Daylight { get; }
        public float AmbientScale { get; }
        public float VisibilityM { get; }
        public Color FogColor => new Color(HorizonColor);

        public AtmosphereState(WeatherSnapshot weather, Vector3 sunDirection, int nightBrightness, int dayBrightness = 20)
        {
            Weather = weather;
            SolarDirection = sunDirection;
            var cover = MathHelper.Clamp(weather.CloudCoverFactor, 0, 1);
            Daylight = SmoothStep(-0.16f, 0.08f, sunDirection.Y);
            var twilight = (1 - SmoothStep(0, 0.25f, Math.Abs(sunDirection.Y))) * SmoothStep(-0.2f, 0, sunDirection.Y);
            var night = MathHelper.Clamp(nightBrightness / 100f, 0, 1);
            var day = Math.Max(0, dayBrightness / 20f);
            var daylightZenith = Vector3.Lerp(new Vector3(0.12f, 0.32f, 0.64f), new Vector3(0.37f, 0.42f, 0.48f), cover * 0.7f);
            var daylightHorizon = Vector3.Lerp(new Vector3(0.62f, 0.76f, 0.9f), new Vector3(0.58f, 0.62f, 0.66f), cover);
            ZenithColor = Vector3.Lerp(new Vector3(0.008f, 0.012f, 0.025f) * night, daylightZenith * day, Daylight);
            HorizonColor = Vector3.Lerp(new Vector3(0.018f, 0.023f, 0.038f) * night, daylightHorizon * day, Daylight);
            HorizonColor = Vector3.Lerp(HorizonColor, new Vector3(0.58f, 0.34f, 0.24f) * Daylight * day,
                twilight * (1 - cover) * 0.35f);
            SunColor = Vector3.Lerp(new Vector3(1, 0.38f, 0.12f), new Vector3(1, 0.96f, 0.87f), SmoothStep(0, 0.3f, sunDirection.Y));
            var rain = MathHelper.Clamp(weather.PrecipitationIntensityPPSPM2 / 0.015f, 0, 1);
            CloudLightColor = Vector3.Lerp(new Vector3(0.022f, 0.027f, 0.043f) * night,
                Vector3.Lerp(new Vector3(MathHelper.Lerp(0.93f, 0.65f, cover)), SunColor,
                    twilight * (1 - cover) * 0.55f) * (1 - rain * 0.15f) * day, Daylight);
            CloudShadeColor = Vector3.Lerp(new Vector3(0.009f, 0.013f, 0.023f) * night,
                Vector3.Lerp(new Vector3(0.36f, 0.39f, 0.44f), new Vector3(0.2f, 0.23f, 0.28f), rain) * day, Daylight);
            AmbientScale = 1 - cover * 0.25f;
            VisibilityM = EffectiveVisibility(weather, weather.VisibilityM);
        }

        public static float EffectiveVisibility(WeatherSnapshot weather, float visibility)
        {
            var intensity = MathHelper.Clamp(weather.PrecipitationIntensityPPSPM2 / 0.015f, 0, 1);
            if (intensity == 0) return Math.Max(1, visibility);
            var precipitationVisibility = MathHelper.Lerp(300, 900, MathHelper.Clamp(weather.PrecipitationLiquidity, 0, 1));
            return 1 / (1 / Math.Max(1, visibility) + intensity / precipitationVisibility);
        }

        static float SmoothStep(float lower, float upper, float value)
        {
            var x = MathHelper.Clamp((value - lower) / (upper - lower), 0, 1);
            return x * x * (3 - 2 * x);
        }

        /// <summary>Average density along a ray through a flat-bottomed exponential fog layer.</summary>
        public static float FogDensity(float cameraHeight, float surfaceHeight, float baseHeight, float heightScale)
        {
            if (heightScale <= 0) return 1;
            var a = cameraHeight - baseHeight;
            var b = surfaceHeight - baseHeight;
            if (a <= 0 && b <= 0) return 1;
            if (a >= 0 && b >= 0)
            {
                var delta = (b - a) / heightScale;
                if (Math.Abs(delta) < 0.001f) return (float)Math.Exp(-(a + b) / (2 * heightScale));
                return (float)((Math.Exp(-a / heightScale) - Math.Exp(-b / heightScale)) / delta);
            }
            var above = Math.Max(a, b);
            var below = -Math.Min(a, b);
            return (below + heightScale * (1 - (float)Math.Exp(-above / heightScale))) / (above + below);
        }
    }
}
