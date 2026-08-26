using WeatherTrackerLite.Web.Features.Weather.Domain;

namespace WeatherTrackerLite.Web.Features.Weather.Application;

public sealed class GetWeatherForCity(IWeatherProvider weatherProvider, ILogger<GetWeatherForCity> logger)
{
    private const int MaximumCityLength = 200;

    public async Task<WeatherQueryOutcome> ExecuteAsync(string? city, CancellationToken cancellationToken = default)
    {
        var normalizedCity = city?.Trim();

        if (string.IsNullOrWhiteSpace(normalizedCity) ||
            normalizedCity.Length > MaximumCityLength ||
            normalizedCity.Any(char.IsControl))
        {
            var invalidRequest = new WeatherQueryOutcome.InvalidRequest();
            LogOutcome(invalidRequest);
            return invalidRequest;
        }

        var outcome = await weatherProvider.GetWeatherForCityAsync(normalizedCity, cancellationToken);
        LogOutcome(outcome);
        return outcome;
    }

    private void LogOutcome(WeatherQueryOutcome outcome)
    {
        var classification = outcome.GetType().Name;
        if (outcome is WeatherQueryOutcome.ProviderUnavailable or WeatherQueryOutcome.TimedOut or WeatherQueryOutcome.InvalidProviderData)
        {
            logger.LogWarning("Weather query completed with outcome classification {OutcomeClassification}", classification);
            return;
        }

        logger.LogInformation("Weather query completed with outcome classification {OutcomeClassification}", classification);
    }
}
