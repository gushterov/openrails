using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using Orts.Common;
using ORTS.Common;
using ORTS.Settings;

namespace Orts.Viewer3D.Common
{
    internal sealed class SolarDay
    {
        const int SecondsPerDay = 86400;
        // The optional footer follows the existing save end marker.
        const uint SaveTag = 0x314E5553; // SUN1
        const int FooterSize = sizeof(uint) + sizeof(int) * 3 + sizeof(double);
        static readonly HttpClient Client = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };

        readonly DateOnly Date;
        readonly int SunriseSeconds;
        readonly int DaylightSeconds;
        readonly double HorizonHourAngle;

        public double Latitude { get; }
        public double Declination { get; }

        SolarDay(DateOnly date, double latitude, int sunriseSeconds, int daylightSeconds)
        {
            Date = date;
            Latitude = latitude;
            SunriseSeconds = sunriseSeconds;
            DaylightSeconds = daylightSeconds;
            Declination = SunMoonPos.SolarDeclination(Math.Tau * (date.DayOfYear - 1) / (DateTime.IsLeapYear(date.Year) ? 366 : 365));
            HorizonHourAngle = SunMoonPos.HorizonHourAngle(latitude, Declination);
        }

        public double HourAngle(float clockTime)
        {
            // Give both daylight and darkness a single arc, including when sunset falls after midnight.
            var sinceSunrise = (clockTime * SecondsPerDay - SunriseSeconds) % SecondsPerDay;
            if (sinceSunrise < 0)
                sinceSunrise += SecondsPerDay;
            if (sinceSunrise <= DaylightSeconds)
                return HorizonHourAngle * (2 * sinceSunrise / DaylightSeconds - 1);
            return HorizonHourAngle + (Math.Tau - 2 * HorizonHourAngle) *
                (sinceSunrise - DaylightSeconds) / (SecondsPerDay - DaylightSeconds);
        }

        public static SolarDay Resolve(UserSettings settings, WorldLocation start, ORTS.Common.CancellationToken cancellation)
        {
            var mode = (SolarTimeSource)settings.SolarTimeMode;
            if (mode != SolarTimeSource.TrainStart && mode != SolarTimeSource.CustomLocation)
                return null;

            if (!DateOnly.TryParseExact(settings.SolarDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                Trace.TraceWarning("Select a simulation date to use online sunrise and sunset times. Using route settings.");
                return null;
            }

            double latitude = 0;
            double longitude = 0;
            if (mode == SolarTimeSource.TrainStart)
            {
                if (new WorldLatLon().ConvertWTC(start.TileX, start.TileZ, start.Location, ref latitude, ref longitude) != 1)
                {
                    Trace.TraceWarning("The train's starting location could not be mapped. Using route sunrise and sunset settings.");
                    return null;
                }
                latitude = double.RadiansToDegrees(latitude);
                longitude = double.RadiansToDegrees(longitude);
            }
            else if (!double.TryParse(settings.SolarLatitude, NumberStyles.Float, CultureInfo.InvariantCulture, out latitude)
                || !double.TryParse(settings.SolarLongitude, NumberStyles.Float, CultureInfo.InvariantCulture, out longitude))
            {
                Trace.TraceWarning("The custom sunrise and sunset coordinates are invalid. Using route settings.");
                return null;
            }
            if (!(latitude >= -90 && latitude <= 90 && longitude >= -180 && longitude <= 180))
            {
                Trace.TraceWarning("The sunrise and sunset coordinates are out of range. Using route settings.");
                return null;
            }

            latitude = Math.Round(latitude, 5);
            longitude = Math.Round(longitude, 5);
            var cacheDirectory = Path.Combine(UserSettings.UserDataFolder, "SolarTimes");
            var cachePath = Path.Combine(cacheDirectory, FormattableString.Invariant($"{date:yyyy-MM-dd}_{latitude:F5}_{longitude:F5}.json"));
            try
            {
                if (File.Exists(cachePath) && Parse(File.ReadAllText(cachePath), date, latitude) is SolarDay cached)
                    return cached;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
            {
                Trace.TraceInformation($"Could not read the sunrise and sunset cache: {error.Message}");
            }

            try
            {
                var url = FormattableString.Invariant($"https://api.sunrise-sunset.org/v2?lat={latitude:F5}&lng={longitude:F5}&date={date:yyyy-MM-dd}");
                using var requestCancellation = new System.Threading.CancellationTokenSource();
                var request = Client.GetStringAsync(url, requestCancellation.Token);
                while (!request.IsCompleted && !cancellation.IsCancellationRequested)
                    Thread.Sleep(100);
                if (!request.IsCompleted)
                    requestCancellation.Cancel();
                var json = request.GetAwaiter().GetResult();
                var day = Parse(json, date, latitude);
                if (day == null)
                {
                    Trace.TraceInformation("No usable sunrise and sunset pair for the selected date and location. Using route settings.");
                    return null;
                }
                try
                {
                    Directory.CreateDirectory(cacheDirectory);
                    File.WriteAllText(cachePath, json);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    Trace.TraceInformation($"Could not cache sunrise and sunset times: {error.Message}");
                }
                Trace.TraceInformation($"Using sunrise and sunset times from Sunrise-Sunset.org for {date:yyyy-MM-dd}.");
                return day;
            }
            catch (Exception error) when (error is HttpRequestException or JsonException or OperationCanceledException)
            {
                Trace.TraceWarning($"Could not get online sunrise and sunset times. Using route settings. {error.Message}");
                return null;
            }
        }

        static SolarDay Parse(string json, DateOnly date, double latitude)
        {
            var response = JsonSerializer.Deserialize<Response>(json);
            if (response?.Sunrise is not DateTimeOffset sunrise || response.Sunset is not DateTimeOffset sunset
                || DateOnly.FromDateTime(sunrise.Date) != date)
                return null;

            // Use the API's local wall-clock times, which is how Open Rails reads the run clock.
            var duration = (sunset.DateTime - sunrise.DateTime).TotalSeconds;
            if (duration <= 0 || duration >= SecondsPerDay)
                return null;
            return Create(date, double.DegreesToRadians(latitude), (int)sunrise.TimeOfDay.TotalSeconds, (int)duration);
        }

        static SolarDay Create(DateOnly date, double latitude, int sunriseSeconds, int daylightSeconds)
        {
            if (!(latitude >= -Math.PI / 2 && latitude <= Math.PI / 2) || sunriseSeconds < 0 || sunriseSeconds >= SecondsPerDay
                || daylightSeconds <= 0 || daylightSeconds >= SecondsPerDay)
                return null;
            var day = new SolarDay(date, latitude, sunriseSeconds, daylightSeconds);
            return day.HorizonHourAngle > 0 && day.HorizonHourAngle < Math.PI ? day : null;
        }

        public void WriteToSave(BinaryWriter writer)
        {
            writer.Write(SaveTag);
            writer.Write(Date.DayNumber);
            writer.Write(Latitude);
            writer.Write(SunriseSeconds);
            writer.Write(DaylightSeconds);
        }

        public static SolarDay ReadFromSave(BinaryReader reader)
        {
            var stream = reader.BaseStream;
            var originalPosition = stream.Position;
            var markerPosition = stream.Length - FooterSize - sizeof(long);
            if (markerPosition < originalPosition)
                return null;
            try
            {
                stream.Position = markerPosition;
                if (reader.ReadInt64() != markerPosition || reader.ReadUInt32() != SaveTag)
                    return null;
                var dayNumber = reader.ReadInt32();
                if (dayNumber < 0 || dayNumber > DateOnly.MaxValue.DayNumber)
                    return null;
                return Create(DateOnly.FromDayNumber(dayNumber), reader.ReadDouble(), reader.ReadInt32(), reader.ReadInt32());
            }
            finally
            {
                stream.Position = originalPosition;
            }
        }

        sealed record Response(
            [property: JsonPropertyName("sunrise")] DateTimeOffset? Sunrise,
            [property: JsonPropertyName("sunset")] DateTimeOffset? Sunset);
    }
}
