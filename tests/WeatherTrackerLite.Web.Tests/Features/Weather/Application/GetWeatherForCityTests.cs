using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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
        var workflow = CreateWorkflow(provider);

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
        var workflow = CreateWorkflow(provider);

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
        var workflow = CreateWorkflow(provider);

        var outcome = await workflow.ExecuteAsync("Lisbon");

        Assert.Same(providerOutcome, outcome);
        Assert.Equal("Lisbon", provider.ReceivedCity);
    }

    [Fact]
    public async Task ExecuteAsync_logs_the_outcome_classification_without_the_city()
    {
        var logger = new CapturingLogger<GetWeatherForCity>();
        var workflow = new GetWeatherForCity(new FakeWeatherProvider(new WeatherQueryOutcome.NotFound()), logger);

        await workflow.ExecuteAsync("Wellington");

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Equal("Weather query completed with outcome classification {OutcomeClassification}", entry.Template);
        Assert.Equal("NotFound", entry.Properties["OutcomeClassification"]);
        Assert.DoesNotContain("Wellington", entry.Properties.Values.OfType<string>());
    }

    private static WeatherQueryOutcome CreateFailureOutcome(Type outcomeType) =>
        outcomeType == typeof(WeatherQueryOutcome.NotFound) ? new WeatherQueryOutcome.NotFound() :
        outcomeType == typeof(WeatherQueryOutcome.ProviderUnavailable) ? new WeatherQueryOutcome.ProviderUnavailable() :
        outcomeType == typeof(WeatherQueryOutcome.TimedOut) ? new WeatherQueryOutcome.TimedOut() :
        new WeatherQueryOutcome.InvalidProviderData();

    private static GetWeatherForCity CreateWorkflow(IWeatherProvider provider) =>
        new(provider, NullLogger<GetWeatherForCity>.Instance);

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

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (state is not IReadOnlyList<KeyValuePair<string, object?>> structuredState)
            {
                throw new InvalidOperationException("Structured log state was expected.");
            }

            var properties = structuredState
                .Where(property => property.Key != "{OriginalFormat}")
                .ToDictionary(property => property.Key, property => property.Value);
            var template = structuredState
                .Single(property => property.Key == "{OriginalFormat}").Value?.ToString();

            Entries.Add(new LogEntry(logLevel, template, properties));
        }
    }

    private sealed record LogEntry(LogLevel Level, string? Template, IReadOnlyDictionary<string, object?> Properties);
}
