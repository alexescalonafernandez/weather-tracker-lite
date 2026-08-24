namespace WeatherTrackerLite.Web.Features.Weather.Domain;

public abstract record WeatherQueryOutcome
{
    private WeatherQueryOutcome()
    {
    }

    public sealed record Success(WeatherQueryResult Result) : WeatherQueryOutcome;

    public sealed record NotFound : WeatherQueryOutcome;

    public sealed record ProviderUnavailable : WeatherQueryOutcome;

    public sealed record TimedOut : WeatherQueryOutcome;

    public sealed record InvalidProviderData : WeatherQueryOutcome;

    public sealed record InvalidRequest : WeatherQueryOutcome;
}
