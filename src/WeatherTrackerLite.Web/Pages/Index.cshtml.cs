using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using WeatherTrackerLite.Web.Features.Weather.Application;
using WeatherTrackerLite.Web.Features.Weather.Domain;

namespace WeatherTrackerLite.Web.Pages;

public sealed class IndexModel(GetWeatherForCity getWeatherForCity) : PageModel
{
    [BindProperty]
    public string? City { get; set; }

    public WeatherQueryResult? Result { get; private set; }

    public string? OutcomeMessage { get; private set; }

    public async Task OnPostAsync(CancellationToken cancellationToken)
    {
        try
        {
            var outcome = await getWeatherForCity.ExecuteAsync(City, cancellationToken);
            ApplyOutcome(outcome);
        }
        catch (Exception)
        {
            OutcomeMessage = "Weather information is temporarily unavailable. Please try again later.";
        }
    }

    private void ApplyOutcome(WeatherQueryOutcome outcome)
    {
        switch (outcome)
        {
            case WeatherQueryOutcome.Success success:
                Result = success.Result;
                break;
            case WeatherQueryOutcome.NotFound:
                OutcomeMessage = "We could not find that city. Check the spelling or try a nearby city.";
                break;
            case WeatherQueryOutcome.TimedOut:
                OutcomeMessage = "The weather service took too long to respond. Please try again.";
                break;
            case WeatherQueryOutcome.InvalidRequest:
                OutcomeMessage = "Enter a city to see its weather.";
                break;
            case WeatherQueryOutcome.ProviderUnavailable:
            case WeatherQueryOutcome.InvalidProviderData:
            default:
                OutcomeMessage = "Weather information is temporarily unavailable. Please try again later.";
                break;
        }
    }
}
