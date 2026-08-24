using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using WeatherTrackerLite.Web.Features.Weather.Domain;
using WeatherTrackerLite.Web.Features.Weather.Infrastructure;
using Xunit;

namespace WeatherTrackerLite.Web.Tests.Features.Weather.Infrastructure;

public sealed class OpenMeteoWeatherProviderTests
{
    [Fact]
    public async Task GetWeatherForCityAsync_maps_approved_fields_and_adds_attribution()
    {
        var handler = new QueueHttpMessageHandler(
            JsonResponse("""
                { "results": [{ "name": "Wellington", "country": "New Zealand", "latitude": -41.2865, "longitude": 174.7762, "timezone": "Pacific/Auckland" }] }
                """),
            JsonResponse("""
                { "utc_offset_seconds": 43200, "current": { "time": "2026-08-24T09:00", "temperature_2m": 12, "apparent_temperature": 10, "weather_code": 2, "wind_speed_10m": 20 }, "daily": { "time": ["2026-08-24", "2026-08-25", "2026-08-26"], "temperature_2m_min": [8, 7, 9], "temperature_2m_max": [13, 14, 15], "precipitation_probability_max": [20, 10, 30] } }
                """));
        var provider = CreateProvider(handler);

        var outcome = await provider.GetWeatherForCityAsync("Wellington");

        var success = Assert.IsType<WeatherQueryOutcome.Success>(outcome);
        Assert.Equal("Wellington", success.Result.Location.City);
        Assert.Equal(new DateTimeOffset(2026, 8, 24, 9, 0, 0, TimeSpan.FromHours(12)), success.Result.CurrentConditions.ObservedAtLocal);
        Assert.Equal("Partly cloudy", success.Result.CurrentConditions.Condition);
        Assert.Equal(3, success.Result.Forecast.Days.Count);
        Assert.Equal("Weather data by Open-Meteo.com (CC BY 4.0).", success.Result.Attribution);
        Assert.Equal(2, handler.RequestUris.Count);
        Assert.Contains("name=Wellington", handler.RequestUris[0].Query);
        Assert.Contains("current=temperature_2m,apparent_temperature,weather_code,wind_speed_10m", handler.RequestUris[1].Query);
        Assert.Contains("daily=temperature_2m_min,temperature_2m_max,precipitation_probability_max", handler.RequestUris[1].Query);
        Assert.Contains("timezone=Pacific%2FAuckland", handler.RequestUris[1].Query);
    }

    [Fact]
    public async Task GetWeatherForCityAsync_returns_not_found_for_no_usable_geocoding_match()
    {
        var provider = CreateProvider(new QueueHttpMessageHandler(JsonResponse("""
            { "results": [{ "name": "Wellington", "latitude": -41.2865, "longitude": 174.7762 }] }
            """)));

        var outcome = await provider.GetWeatherForCityAsync("Wellington");

        Assert.IsType<WeatherQueryOutcome.NotFound>(outcome);
    }

    [Fact]
    public async Task GetWeatherForCityAsync_returns_invalid_provider_data_for_missing_required_forecast_field()
    {
        var handler = new QueueHttpMessageHandler(
            JsonResponse("""
                { "results": [{ "name": "Wellington", "country": "New Zealand", "latitude": -41.2865, "longitude": 174.7762, "timezone": "Pacific/Auckland" }] }
                """),
            JsonResponse("""
                { "utc_offset_seconds": 43200, "current": { "time": "2026-08-24T09:00", "temperature_2m": 12, "apparent_temperature": 10, "weather_code": 2, "wind_speed_10m": 20 }, "daily": { "time": ["2026-08-24", "2026-08-25", "2026-08-26"], "temperature_2m_min": [8, 7, 9], "temperature_2m_max": [13, 14, 15] } }
                """));
        var provider = CreateProvider(handler);

        var outcome = await provider.GetWeatherForCityAsync("Wellington");

        Assert.IsType<WeatherQueryOutcome.InvalidProviderData>(outcome);
    }

    [Fact]
    public async Task GetWeatherForCityAsync_translates_unknown_weather_code_safely()
    {
        var handler = new QueueHttpMessageHandler(
            JsonResponse("""
                { "results": [{ "name": "Wellington", "country": "New Zealand", "latitude": -41.2865, "longitude": 174.7762, "timezone": "Pacific/Auckland" }] }
                """),
            JsonResponse("""
                { "utc_offset_seconds": 43200, "current": { "time": "2026-08-24T09:00", "temperature_2m": 12, "apparent_temperature": 10, "weather_code": 999, "wind_speed_10m": 20 }, "daily": { "time": ["2026-08-24", "2026-08-25", "2026-08-26"], "temperature_2m_min": [8, 7, 9], "temperature_2m_max": [13, 14, 15], "precipitation_probability_max": [20, 10, 30] } }
                """));
        var provider = CreateProvider(handler);

        var outcome = await provider.GetWeatherForCityAsync("Wellington");

        var success = Assert.IsType<WeatherQueryOutcome.Success>(outcome);
        Assert.Equal("Unknown weather condition", success.Result.CurrentConditions.Condition);
    }

    [Fact]
    public async Task GetWeatherForCityAsync_returns_provider_unavailable_for_unsuccessful_response()
    {
        var provider = CreateProvider(new QueueHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));

        var outcome = await provider.GetWeatherForCityAsync("Wellington");

        Assert.IsType<WeatherQueryOutcome.ProviderUnavailable>(outcome);
    }

    [Fact]
    public async Task GetWeatherForCityAsync_returns_timed_out_for_cancelled_request()
    {
        var provider = CreateProvider(new QueueHttpMessageHandler(new OperationCanceledException()));

        var outcome = await provider.GetWeatherForCityAsync("Wellington");

        Assert.IsType<WeatherQueryOutcome.TimedOut>(outcome);
    }

    [Fact]
    public async Task GetWeatherForCityAsync_returns_invalid_provider_data_for_malformed_json()
    {
        var provider = CreateProvider(new QueueHttpMessageHandler(JsonResponse("{ invalid json")));

        var outcome = await provider.GetWeatherForCityAsync("Wellington");

        Assert.IsType<WeatherQueryOutcome.InvalidProviderData>(outcome);
    }

    private static OpenMeteoWeatherProvider CreateProvider(QueueHttpMessageHandler handler) => new(
        new HttpClient(handler),
        Options.Create(new OpenMeteoOptions
        {
            GeocodingBaseUrl = "https://geocoding-api.open-meteo.com/",
            ForecastBaseUrl = "https://api.open-meteo.com/",
            TimeoutSeconds = 10
        }));

    private static HttpResponseMessage JsonResponse(string content) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(content, Encoding.UTF8, "application/json")
    };

    private sealed class QueueHttpMessageHandler(params object[] responses) : HttpMessageHandler
    {
        private readonly Queue<object> responses = new(responses);

        public List<Uri> RequestUris { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUris.Add(request.RequestUri!);
            var response = responses.Dequeue();
            return response switch
            {
                HttpResponseMessage message => Task.FromResult(message),
                Exception exception => Task.FromException<HttpResponseMessage>(exception),
                _ => throw new InvalidOperationException("Unsupported test response.")
            };
        }
    }
}
