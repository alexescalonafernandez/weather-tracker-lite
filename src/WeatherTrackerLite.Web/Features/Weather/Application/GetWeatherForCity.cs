using WeatherTrackerLite.Web.Features.Weather.Domain;

namespace WeatherTrackerLite.Web.Features.Weather.Application;

public sealed class GetWeatherForCity(IWeatherProvider weatherProvider)
{
    private const int MaximumCityLength = 200;

    public Task<WeatherQueryOutcome> ExecuteAsync(string? city, CancellationToken cancellationToken = default)
    {
        var normalizedCity = city?.Trim();

        if (string.IsNullOrWhiteSpace(normalizedCity) ||
            normalizedCity.Length > MaximumCityLength ||
            normalizedCity.Any(char.IsControl))
        {
            return Task.FromResult<WeatherQueryOutcome>(new WeatherQueryOutcome.InvalidRequest());
        }

        return weatherProvider.GetWeatherForCityAsync(normalizedCity, cancellationToken);
    }
}
