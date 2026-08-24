using WeatherTrackerLite.Web.Features.Weather.Domain;

namespace WeatherTrackerLite.Web.Features.Weather.Application;

public interface IWeatherProvider
{
    Task<WeatherQueryOutcome> GetWeatherForCityAsync(string city, CancellationToken cancellationToken = default);
}
