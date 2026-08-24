namespace WeatherTrackerLite.Web.Features.Weather.Infrastructure;

public sealed class OpenMeteoOptions
{
    public const string SectionName = "OpenMeteo";

    public string GeocodingBaseUrl { get; init; } = string.Empty;

    public string ForecastBaseUrl { get; init; } = string.Empty;

    public int TimeoutSeconds { get; init; }

    public bool HasValidConfiguration() =>
        IsAbsoluteHttpUrl(GeocodingBaseUrl) &&
        IsAbsoluteHttpUrl(ForecastBaseUrl) &&
        TimeoutSeconds is > 0 and <= 120;

    private static bool IsAbsoluteHttpUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
}
