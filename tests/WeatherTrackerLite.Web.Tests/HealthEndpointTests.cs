using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace WeatherTrackerLite.Web.Tests;

public sealed class HealthEndpointTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task Get_returns_success_for_configured_health_endpoint(string path)
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(path);

        response.EnsureSuccessStatusCode();
    }
}
