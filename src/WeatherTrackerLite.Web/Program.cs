using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using WeatherTrackerLite.Web.Features.Weather.Application;
using WeatherTrackerLite.Web.Features.Weather.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddOptions<OpenMeteoOptions>()
    .Bind(builder.Configuration.GetSection(OpenMeteoOptions.SectionName))
    .Validate(options => options.HasValidConfiguration(), "Open-Meteo endpoints and timeout must be valid.")
    .ValidateOnStart();

builder.Services.AddHttpClient<IWeatherProvider, OpenMeteoWeatherProvider>((serviceProvider, client) =>
{
    var options = serviceProvider.GetRequiredService<IOptions<OpenMeteoOptions>>().Value;
    client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
});
builder.Services.AddScoped<GetWeatherForCity>();
builder.Services.AddRazorPages();
builder.Services.AddHealthChecks()
    .AddCheck<OpenMeteoConfigurationHealthCheck>("openmeteo_configuration", tags: ["ready"]);

var app = builder.Build();

app.MapRazorPages();
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = healthCheck => healthCheck.Tags.Contains("ready") });
app.Run();

public partial class Program;
