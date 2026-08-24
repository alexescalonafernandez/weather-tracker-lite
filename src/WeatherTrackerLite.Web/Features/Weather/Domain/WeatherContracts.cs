namespace WeatherTrackerLite.Web.Features.Weather.Domain;

public sealed record ResolvedLocation(
    string City,
    string CountryOrRegion,
    decimal Latitude,
    decimal Longitude,
    string TimeZone);

public sealed record CurrentConditions(
    DateTimeOffset ObservedAtLocal,
    decimal TemperatureCelsius,
    decimal ApparentTemperatureCelsius,
    decimal WindSpeedKilometresPerHour,
    string Condition);

public sealed record ForecastDay(
    DateOnly Date,
    decimal MinimumTemperatureCelsius,
    decimal MaximumTemperatureCelsius,
    decimal PrecipitationProbabilityPercent);

public sealed class ThreeDayForecast
{
    public ThreeDayForecast(IReadOnlyList<ForecastDay> days)
    {
        ArgumentNullException.ThrowIfNull(days);

        if (days.Count != 3)
        {
            throw new ArgumentException("A three-day forecast must contain exactly three days.", nameof(days));
        }

        Days = days;
    }

    public IReadOnlyList<ForecastDay> Days { get; }
}

public sealed record WeatherQueryResult(
    ResolvedLocation Location,
    CurrentConditions CurrentConditions,
    ThreeDayForecast Forecast,
    string Attribution);
