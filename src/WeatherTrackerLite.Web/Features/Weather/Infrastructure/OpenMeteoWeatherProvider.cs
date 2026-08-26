using System.Globalization;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using WeatherTrackerLite.Web.Features.Weather.Application;
using WeatherTrackerLite.Web.Features.Weather.Domain;

namespace WeatherTrackerLite.Web.Features.Weather.Infrastructure;

public sealed class OpenMeteoWeatherProvider(
    HttpClient httpClient,
    IOptions<OpenMeteoOptions> options,
    ILogger<OpenMeteoWeatherProvider> logger) : IWeatherProvider
{
    private const string Attribution = "Weather data by Open-Meteo.com (CC BY 4.0).";
    private readonly OpenMeteoOptions options = options.Value;

    public async Task<WeatherQueryOutcome> GetWeatherForCityAsync(string city, CancellationToken cancellationToken = default)
    {
        try
        {
            var geocodingResponse = await SendAsync<GeocodingResponse>(BuildGeocodingUri(city), "geocoding", cancellationToken);
            if (geocodingResponse is null)
            {
                return new WeatherQueryOutcome.ProviderUnavailable();
            }

            var location = TryMapLocation(geocodingResponse.Results);
            if (location is null)
            {
                return new WeatherQueryOutcome.NotFound();
            }

            var forecastResponse = await SendAsync<ForecastResponse>(BuildForecastUri(location), "forecast", cancellationToken);
            if (forecastResponse is null)
            {
                return new WeatherQueryOutcome.ProviderUnavailable();
            }

            return TryMapResult(location, forecastResponse, out var result)
                ? new WeatherQueryOutcome.Success(result)
                : new WeatherQueryOutcome.InvalidProviderData();
        }
        catch (HttpRequestException)
        {
            return new WeatherQueryOutcome.ProviderUnavailable();
        }
        catch (OperationCanceledException)
        {
            return new WeatherQueryOutcome.TimedOut();
        }
        catch (JsonException)
        {
            return new WeatherQueryOutcome.InvalidProviderData();
        }
    }

    private async Task<T?> SendAsync<T>(Uri requestUri, string operation, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        HttpResponseMessage? response = null;

        try
        {
            response = await httpClient.GetAsync(requestUri, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                LogDependencyOutcome(operation, "HttpFailure", response.StatusCode, stopwatch.Elapsed.TotalMilliseconds);
                return default;
            }

            var payload = await response.Content.ReadFromJsonAsync<T>(cancellationToken: cancellationToken);
            LogDependencyOutcome(operation, "Success", response.StatusCode, stopwatch.Elapsed.TotalMilliseconds);
            return payload;
        }
        catch (HttpRequestException)
        {
            LogDependencyOutcome(operation, "TransportFailure", response?.StatusCode, stopwatch.Elapsed.TotalMilliseconds);
            throw;
        }
        catch (OperationCanceledException)
        {
            LogDependencyOutcome(operation, "TimedOut", response?.StatusCode, stopwatch.Elapsed.TotalMilliseconds);
            throw;
        }
        catch (JsonException)
        {
            LogDependencyOutcome(operation, "InvalidPayload", response?.StatusCode, stopwatch.Elapsed.TotalMilliseconds);
            throw;
        }
        finally
        {
            response?.Dispose();
        }
    }

    private void LogDependencyOutcome(string operation, string outcome, System.Net.HttpStatusCode? statusCode, double durationMilliseconds)
    {
        logger.LogInformation(
            "Open-Meteo dependency call completed with outcome {DependencyOutcome} for operation {ProviderOperation}, status code {StatusCode}, duration {DurationMs}ms",
            outcome,
            operation,
            statusCode is null ? null : (int)statusCode,
            durationMilliseconds);
    }

    private Uri BuildGeocodingUri(string city) => new(
        new Uri(options.GeocodingBaseUrl),
        $"v1/search?name={Uri.EscapeDataString(city)}&count=10&language=en&format=json");

    private Uri BuildForecastUri(ResolvedLocation location) => new(
        new Uri(options.ForecastBaseUrl),
        "v1/forecast?" +
        $"latitude={location.Latitude.ToString(CultureInfo.InvariantCulture)}&" +
        $"longitude={location.Longitude.ToString(CultureInfo.InvariantCulture)}&" +
        $"timezone={Uri.EscapeDataString(location.TimeZone)}&" +
        "current=temperature_2m,apparent_temperature,weather_code,wind_speed_10m&" +
        "daily=temperature_2m_min,temperature_2m_max,precipitation_probability_max&forecast_days=3");

    private static ResolvedLocation? TryMapLocation(IReadOnlyList<GeocodingResult>? results)
    {
        var match = results?.FirstOrDefault(IsUsableLocation);
        if (match is null)
        {
            return null;
        }

        return new ResolvedLocation(
            match.Name!,
            match.Country ?? match.Admin1!,
            match.Latitude!.Value,
            match.Longitude!.Value,
            match.TimeZone!);
    }

    private static bool IsUsableLocation(GeocodingResult location) =>
        !string.IsNullOrWhiteSpace(location.Name) &&
        !string.IsNullOrWhiteSpace(location.Country ?? location.Admin1) &&
        !string.IsNullOrWhiteSpace(location.TimeZone) &&
        location.Latitude is >= -90m and <= 90m &&
        location.Longitude is >= -180m and <= 180m;

    private static bool TryMapResult(ResolvedLocation location, ForecastResponse response, out WeatherQueryResult result)
    {
        result = null!;

        if (response.Current is not { Time: { } time, TemperatureCelsius: { } temperature, ApparentTemperatureCelsius: { } apparentTemperature, WeatherCode: { } weatherCode, WindSpeedKilometresPerHour: { } windSpeed } ||
            response.UtcOffsetSeconds is not { } utcOffsetSeconds ||
            !TryParseLocalTime(time, utcOffsetSeconds, out var observedAtLocal) ||
            !TryMapForecast(response.Daily, out var forecast))
        {
            return false;
        }

        result = new WeatherQueryResult(
            location,
            new CurrentConditions(observedAtLocal, temperature, apparentTemperature, windSpeed, TranslateWeatherCode(weatherCode)),
            forecast,
            Attribution);
        return true;
    }

    private static bool TryParseLocalTime(string value, int utcOffsetSeconds, out DateTimeOffset observedAtLocal)
    {
        observedAtLocal = default;
        if (utcOffsetSeconds is < -50_400 or > 50_400 ||
            !DateTime.TryParseExact(value, ["yyyy-MM-dd'T'HH:mm", "yyyy-MM-dd'T'HH:mm:ss"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var localTime))
        {
            return false;
        }

        observedAtLocal = new DateTimeOffset(DateTime.SpecifyKind(localTime, DateTimeKind.Unspecified), TimeSpan.FromSeconds(utcOffsetSeconds));
        return true;
    }

    private static bool TryMapForecast(DailyForecast? daily, out ThreeDayForecast forecast)
    {
        forecast = null!;
        if (daily?.Dates is not { Count: 3 } dates ||
            daily.MinimumTemperaturesCelsius is not { Count: 3 } minimumTemperatures ||
            daily.MaximumTemperaturesCelsius is not { Count: 3 } maximumTemperatures ||
            daily.PrecipitationProbabilitiesPercent is not { Count: 3 } precipitationProbabilities)
        {
            return false;
        }

        var days = new List<ForecastDay>(3);
        for (var index = 0; index < 3; index++)
        {
            if (!DateOnly.TryParseExact(dates[index], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ||
                minimumTemperatures[index] is not { } minimumTemperature ||
                maximumTemperatures[index] is not { } maximumTemperature ||
                precipitationProbabilities[index] is not { } precipitationProbability ||
                minimumTemperature > maximumTemperature ||
                precipitationProbability is < 0m or > 100m)
            {
                return false;
            }

            days.Add(new ForecastDay(date, minimumTemperature, maximumTemperature, precipitationProbability));
        }

        forecast = new ThreeDayForecast(days);
        return true;
    }

    private static string TranslateWeatherCode(int code) => code switch
    {
        0 => "Clear sky",
        1 => "Mainly clear",
        2 => "Partly cloudy",
        3 => "Overcast",
        45 => "Fog",
        48 => "Depositing rime fog",
        51 => "Light drizzle",
        53 => "Moderate drizzle",
        55 => "Dense drizzle",
        56 => "Light freezing drizzle",
        57 => "Dense freezing drizzle",
        61 => "Slight rain",
        63 => "Moderate rain",
        65 => "Heavy rain",
        66 => "Light freezing rain",
        67 => "Heavy freezing rain",
        71 => "Slight snow fall",
        73 => "Moderate snow fall",
        75 => "Heavy snow fall",
        77 => "Snow grains",
        80 => "Slight rain showers",
        81 => "Moderate rain showers",
        82 => "Violent rain showers",
        85 => "Slight snow showers",
        86 => "Heavy snow showers",
        95 => "Thunderstorm",
        96 => "Thunderstorm with slight hail",
        99 => "Thunderstorm with heavy hail",
        _ => "Unknown weather condition"
    };

    private sealed record GeocodingResponse([property: JsonPropertyName("results")] IReadOnlyList<GeocodingResult>? Results);

    private sealed record GeocodingResult(
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("country")] string? Country,
        [property: JsonPropertyName("admin1")] string? Admin1,
        [property: JsonPropertyName("latitude")] decimal? Latitude,
        [property: JsonPropertyName("longitude")] decimal? Longitude,
        [property: JsonPropertyName("timezone")] string? TimeZone);

    private sealed record ForecastResponse(
        [property: JsonPropertyName("utc_offset_seconds")] int? UtcOffsetSeconds,
        [property: JsonPropertyName("current")] CurrentForecast? Current,
        [property: JsonPropertyName("daily")] DailyForecast? Daily);

    private sealed record CurrentForecast(
        [property: JsonPropertyName("time")] string? Time,
        [property: JsonPropertyName("temperature_2m")] decimal? TemperatureCelsius,
        [property: JsonPropertyName("apparent_temperature")] decimal? ApparentTemperatureCelsius,
        [property: JsonPropertyName("weather_code")] int? WeatherCode,
        [property: JsonPropertyName("wind_speed_10m")] decimal? WindSpeedKilometresPerHour);

    private sealed record DailyForecast(
        [property: JsonPropertyName("time")] IReadOnlyList<string?>? Dates,
        [property: JsonPropertyName("temperature_2m_min")] IReadOnlyList<decimal?>? MinimumTemperaturesCelsius,
        [property: JsonPropertyName("temperature_2m_max")] IReadOnlyList<decimal?>? MaximumTemperaturesCelsius,
        [property: JsonPropertyName("precipitation_probability_max")] IReadOnlyList<decimal?>? PrecipitationProbabilitiesPercent);
}
