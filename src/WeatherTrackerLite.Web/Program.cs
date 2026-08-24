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

var app = builder.Build();

app.Run();

public partial class Program;
