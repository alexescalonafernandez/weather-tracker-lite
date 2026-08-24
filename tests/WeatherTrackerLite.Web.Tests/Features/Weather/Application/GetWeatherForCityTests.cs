using WeatherTrackerLite.Web.Features.Weather.Application;
using WeatherTrackerLite.Web.Features.Weather.Domain;
using Xunit;

namespace WeatherTrackerLite.Web.Tests.Features.Weather.Application;

public sealed class GetWeatherForCityTests
{
    [Fact]
    public async Task ExecuteAsync_returns_success_and_passes_normalized_city_to_provider()
    {
        var expectedResult = CreateWeatherQueryResult();
        var provider = new FakeWeatherProvider(new WeatherQueryOutcome.Success(expectedResult));
        var workflow = new GetWeatherForCity(provider);

        var outcome = await workflow.ExecuteAsync("  Wellington  ");

        var success = Assert.IsType<WeatherQueryOutcome.Success>(outcome);
        Assert.Same(expectedResult, success.Result);
        Assert.Equal("Wellington", provider.ReceivedCity);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("New\nYork")]
    public async Task ExecuteAsync_returns_invalid_request_without_calling_provider_for_invalid_city(string? city)
    {
        var provider = new FakeWeatherProvider(new WeatherQueryOutcome.NotFound());
        var workflow = new GetWeatherForCity(provider);

        var outcome = await workflow.ExecuteAsync(city);

        Assert.IsType<WeatherQueryOutcome.InvalidRequest>(outcome);
        Assert.Null(provider.ReceivedCity);
    }

    [Theory]
    [InlineData(typeof(WeatherQueryOutcome.NotFound))]
    [InlineData(typeof(WeatherQueryOutcome.ProviderUnavailable))]
    [InlineData(typeof(WeatherQueryOutcome.TimedOut))]
    [InlineData(typeof(WeatherQueryOutcome.InvalidProviderData))]
    public async Task ExecuteAsync_returns_provider_failure_outcome_unchanged(Type outcomeType)
    {
        var providerOutcome = CreateFailureOutcome(outcomeType);
        var provider = new FakeWeatherProvider(providerOutcome);
        var workflow = new GetWeatherForCity(provider);

        var outcome = await workflow.ExecuteAsync("Lisbon");

        Assert.Same(providerOutcome, outcome);
        Assert.Equal("Lisbon", provider.ReceivedCity);
    }

    private static WeatherQueryOutcome CreateFailureOutcome(Type outcomeType) =>
        outcomeType == typeof(WeatherQueryOutcome.NotFound) ? new WeatherQueryOutcome.NotFound() :
        outcomeType == typeof(WeatherQueryOutcome.ProviderUnavailable) ? new WeatherQueryOutcome.ProviderUnavailable() :
        outcomeType == typeof(WeatherQueryOutcome.TimedOut) ? new WeatherQueryOutcome.TimedOut() :
        new WeatherQueryOutcome.InvalidProviderData();

    private static WeatherQueryResult CreateWeatherQueryResult() => new(
        new ResolvedLocation("Wellington", "New Zealand", -41.2865m, 174.7762m, "Pacific/Auckland"),
        new CurrentConditions(
            new DateTimeOffset(2026, 8, 24, 9, 0, 0, TimeSpan.FromHours(12)),
            12m,
            10m,
            20m,
            "Cloudy"),
        new ThreeDayForecast(
        [
            new ForecastDay(new DateOnly(2026, 8, 24), 8m, 13m, 20m),
            new ForecastDay(new DateOnly(2026, 8, 25), 7m, 14m, 10m),
            new ForecastDay(new DateOnly(2026, 8, 26), 9m, 15m, 30m)
        ]),
        "Weather data provided by Open-Meteo.");

    private sealed class FakeWeatherProvider(WeatherQueryOutcome outcome) : IWeatherProvider
    {
        public string? ReceivedCity { get; private set; }

        public Task<WeatherQueryOutcome> GetWeatherForCityAsync(string city, CancellationToken cancellationToken = default)
        {
            ReceivedCity = city;
            return Task.FromResult(outcome);
        }
    }
}
