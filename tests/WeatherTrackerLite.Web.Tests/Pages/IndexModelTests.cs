using WeatherTrackerLite.Web.Features.Weather.Application;
using WeatherTrackerLite.Web.Features.Weather.Domain;
using WeatherTrackerLite.Web.Pages;
using Xunit;

namespace WeatherTrackerLite.Web.Tests.Pages;

public sealed class IndexModelTests
{
    [Fact]
    public async Task OnPostAsync_preserves_city_and_exposes_successful_weather_result()
    {
        var expectedResult = CreateWeatherQueryResult();
        var provider = new FakeWeatherProvider(new WeatherQueryOutcome.Success(expectedResult));
        var model = new IndexModel(new GetWeatherForCity(provider)) { City = "  Wellington  " };

        await model.OnPostAsync(CancellationToken.None);

        Assert.Equal("  Wellington  ", model.City);
        Assert.Same(expectedResult, model.Result);
        Assert.Null(model.OutcomeMessage);
        Assert.Equal("Wellington", provider.ReceivedCity);
        Assert.NotNull(model.Result);
        Assert.Equal(3, model.Result.Forecast.Days.Count);
    }

    [Theory]
    [InlineData(typeof(WeatherQueryOutcome.NotFound), "We could not find that city. Check the spelling or try a nearby city.")]
    [InlineData(typeof(WeatherQueryOutcome.ProviderUnavailable), "Weather information is temporarily unavailable. Please try again later.")]
    [InlineData(typeof(WeatherQueryOutcome.TimedOut), "The weather service took too long to respond. Please try again.")]
    [InlineData(typeof(WeatherQueryOutcome.InvalidProviderData), "Weather information is temporarily unavailable. Please try again later.")]
    [InlineData(typeof(WeatherQueryOutcome.InvalidRequest), "Enter a city to see its weather.")]
    public async Task OnPostAsync_renders_safe_actionable_message_for_each_failure_outcome(Type outcomeType, string expectedMessage)
    {
        var model = new IndexModel(new GetWeatherForCity(new FakeWeatherProvider(CreateOutcome(outcomeType)))) { City = "Wellington" };

        await model.OnPostAsync(CancellationToken.None);

        Assert.Equal("Wellington", model.City);
        Assert.Null(model.Result);
        Assert.Equal(expectedMessage, model.OutcomeMessage);
    }

    [Fact]
    public async Task OnPostAsync_hides_unexpected_exception_details()
    {
        var model = new IndexModel(new GetWeatherForCity(new ThrowingWeatherProvider())) { City = "Wellington" };

        await model.OnPostAsync(CancellationToken.None);

        Assert.Null(model.Result);
        Assert.Equal("Weather information is temporarily unavailable. Please try again later.", model.OutcomeMessage);
    }

    private static WeatherQueryOutcome CreateOutcome(Type outcomeType) =>
        outcomeType == typeof(WeatherQueryOutcome.NotFound) ? new WeatherQueryOutcome.NotFound() :
        outcomeType == typeof(WeatherQueryOutcome.ProviderUnavailable) ? new WeatherQueryOutcome.ProviderUnavailable() :
        outcomeType == typeof(WeatherQueryOutcome.TimedOut) ? new WeatherQueryOutcome.TimedOut() :
        outcomeType == typeof(WeatherQueryOutcome.InvalidProviderData) ? new WeatherQueryOutcome.InvalidProviderData() :
        new WeatherQueryOutcome.InvalidRequest();

    private static WeatherQueryResult CreateWeatherQueryResult() => new(
        new ResolvedLocation("Wellington", "New Zealand", -41.2865m, 174.7762m, "Pacific/Auckland"),
        new CurrentConditions(new DateTimeOffset(2026, 8, 24, 9, 0, 0, TimeSpan.FromHours(12)), 12m, 10m, 20m, "Cloudy"),
        new ThreeDayForecast(
        [
            new ForecastDay(new DateOnly(2026, 8, 24), 8m, 13m, 20m),
            new ForecastDay(new DateOnly(2026, 8, 25), 7m, 14m, 10m),
            new ForecastDay(new DateOnly(2026, 8, 26), 9m, 15m, 30m)
        ]),
        "Weather data by Open-Meteo.com (CC BY 4.0).");

    private sealed class FakeWeatherProvider(WeatherQueryOutcome outcome) : IWeatherProvider
    {
        public string? ReceivedCity { get; private set; }

        public Task<WeatherQueryOutcome> GetWeatherForCityAsync(string city, CancellationToken cancellationToken = default)
        {
            ReceivedCity = city;
            return Task.FromResult(outcome);
        }
    }

    private sealed class ThrowingWeatherProvider : IWeatherProvider
    {
        public Task<WeatherQueryOutcome> GetWeatherForCityAsync(string city, CancellationToken cancellationToken = default) =>
            Task.FromException<WeatherQueryOutcome>(new InvalidOperationException("Provider payload at https://example.test failed."));
    }
}
