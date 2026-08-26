using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace WeatherTrackerLite.Web.Features.Weather.Infrastructure;

public sealed class OpenMeteoConfigurationHealthCheck(IOptions<OpenMeteoOptions> options) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(options.Value.HasValidConfiguration()
            ? HealthCheckResult.Healthy("Local Open-Meteo configuration is valid.")
            : HealthCheckResult.Unhealthy("Local Open-Meteo configuration is invalid."));
}
