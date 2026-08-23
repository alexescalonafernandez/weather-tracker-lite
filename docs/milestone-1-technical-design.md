# Milestone 1 Technical Design: City Weather Lookup

Milestone 1 delivers one containerized ASP.NET Core web application in which a visitor enters a city and receives its resolved location, current conditions, and a three-day forecast. The design deliberately keeps the application deployable and auditable as one workload while isolating the external weather API behind a narrow boundary.

## Quick path

1. The visitor submits a city name from the city input and result presentation feature.
2. The presentation layer invokes the `GetWeatherForCity` application workflow.
3. The workflow asks `IWeatherProvider` for a weather query outcome and renders either the weather result or a safe, actionable outcome.

## Scope and boundary

| Area | Design decision |
| --- | --- |
| Deployable unit | One ASP.NET Core web application in one container. |
| Organization | Feature-oriented folders and namespaces; the city weather lookup feature owns its presentation, workflow, and contracts. |
| External integration | One `IWeatherProvider` boundary, implemented by an Open-Meteo HTTP adapter. |
| Interaction | Manual city search and manual refresh only. City data is ephemeral. |
| Weather display | Resolved location and local observation time; current temperature, apparent temperature, readable condition, and wind speed; three forecast days with minimum/maximum temperature and precipitation probability. |
| Provider acknowledgment | The UI presents the attribution supplied by the provider adapter wherever weather results are shown. |

## Component responsibilities

### City input and result presentation

The presentation layer accepts a city string, performs only interaction-level validation such as required/trimmed input, and sends valid requests to `GetWeatherForCity`. It does not call Open-Meteo, interpret provider payloads, or translate weather codes.

It renders one of two states:

- **Weather result:** location, local observation time, current conditions, three-day forecast, and attribution.
- **Safe outcome:** a concise, non-technical explanation with a next action; no provider details, URLs, exception text, or stack traces are exposed.

### `GetWeatherForCity` workflow

`GetWeatherForCity` is the application entry point for a city request. It normalizes the input required by the use case, calls `IWeatherProvider`, and returns the provider's domain-shaped query outcome unchanged except for application-level input handling. It is intentionally free of HTTP, JSON, Open-Meteo query-string, and weather-code knowledge.

The workflow is the seam for future authorization, request metrics, or policy only when a real requirement appears; none are included in this milestone.

### Domain contracts

The feature contracts communicate the information the application needs, rather than mirroring an upstream schema:

| Contract | Meaning |
| --- | --- |
| `ResolvedLocation` | Display-ready city, country or region, geographic coordinates, and the location timezone used for the weather query. |
| `CurrentConditions` | Local observation time, temperature, apparent temperature, wind speed, and readable weather condition. |
| `ForecastDay` | Local date, minimum temperature, maximum temperature, and precipitation probability. |
| `ThreeDayForecast` | Exactly the three display forecast days. |
| `WeatherQueryResult` | Resolved location, current conditions, three-day forecast, and required provider attribution. |
| `WeatherQueryOutcome` | A discriminated success-or-safe-failure result returned to the workflow and presentation layer. |

The model uses provider-independent terms. Open-Meteo DTOs remain inside the adapter so an upstream response shape cannot leak into presentation or application code.

### `IWeatherProvider` boundary

`IWeatherProvider` exposes one operation: retrieve a `WeatherQueryOutcome` for a city query. It is an application-facing boundary, not a general provider marketplace. Milestone 1 has exactly one implementation: the Open-Meteo adapter.

The abstraction exists to separate the city-weather workflow from HTTP concerns and to make the workflow independently testable. Adding multiple providers, routing, fallback, or selection policy is explicitly out of scope.

### Open-Meteo HTTP adapter

The Open-Meteo adapter owns all provider-specific behavior:

1. Call Open-Meteo geocoding to resolve the submitted city.
2. Select the resolved location or return `NotFound` when no usable location is returned.
3. Request required current and daily forecast fields for that location, using the resolved location's timezone so observation times and forecast dates are local to the city.
4. Map HTTP DTOs to the domain contracts.
5. Translate Open-Meteo weather codes to the application's readable weather-condition vocabulary.
6. Attach required Open-Meteo attribution to successful results.
7. Map provider and transport failures to safe query outcomes.

The adapter uses the application's configured HTTP client and platform defaults. It does not introduce custom retry, circuit-breaker, or fallback behavior in this milestone.

## User-safe outcomes

| Outcome | Trigger | Visitor-facing message and action | Internal treatment |
| --- | --- | --- | --- |
| `Success` | A location is resolved and required weather data is valid. | Show weather result and attribution. | Record successful provider dependency telemetry. |
| `NotFound` | Geocoding returns no usable match. | “We could not find that city. Check the spelling or try a nearby city.” | Log a structured, non-sensitive not-found event. |
| `ProviderUnavailable` | Open-Meteo returns a service or dependency failure. | “Weather information is temporarily unavailable. Please try again later.” | Log the mapped provider status and dependency failure without exposing it to the visitor. |
| `TimedOut` | The HTTP operation exceeds the configured/default client timeout or is cancelled by an upstream timeout path. | “The weather service took too long to respond. Please try again.” | Log timeout classification and duration. |
| `InvalidProviderData` | A successful response is missing, malformed, or internally inconsistent for required fields. | “Weather information is temporarily unavailable. Please try again later.” | Log validation failure with safe diagnostic context; do not render partial or guessed weather data. |
| `InvalidRequest` | The city input is blank after normalization or otherwise fails workflow input rules. | “Enter a city to see its weather.” | Treat as a normal validation outcome, not a provider failure. |

Unexpected application failures use the same generic user-safe unavailable message. They are logged with exception details only in server-side telemetry.

## Request and data flow

```text
Visitor
  -> City input/result presentation
  -> GetWeatherForCity
  -> IWeatherProvider
  -> Open-Meteo adapter
       -> Geocoding API
       -> Forecast API (resolved coordinates + resolved timezone)
       -> DTO mapping + weather-code translation + attribution
  <- WeatherQueryOutcome
  <- Result or safe outcome
```

The geocoding result is the authority for the coordinates, display location, and timezone of a request. The forecast request must not use the server/container timezone because that would mislabel dates and observation time for cities in other zones.

## Operational seams

| Concern | Milestone 1 seam | Intended behavior |
| --- | --- | --- |
| Structured logging | Request boundary, workflow outcome, and adapter dependency calls | Include outcome classification, duration, provider HTTP status where available, and a correlation identifier. Never log raw exception details to the visitor. |
| Trace and correlation | Incoming request trace context propagated to workflow and outgoing HTTP dependency calls | Make one city lookup traceable across presentation, workflow, geocoding, and forecast calls. Generate/use a correlation identifier when no trace context exists. |
| Health checks | ASP.NET Core health-check registration point | Separate lightweight application liveness from readiness checks. Readiness initially verifies local application configuration only; it does not make Open-Meteo a blocking health dependency. |
| Configuration validation | Strongly typed Open-Meteo and application options validated at startup | Fail fast for missing or invalid endpoint base addresses, required path/settings, or invalid timeout configuration. Keep configuration names and sources outside feature code. |

These seams identify where later operational work belongs without selecting Azure-specific telemetry exporters or infrastructure resources.

## Verification checklist

- [ ] A valid city produces a resolved location, local observation time, current conditions, three forecast days, and attribution.
- [ ] A city with no geocoding match produces `NotFound` and no provider implementation detail reaches the visitor.
- [ ] Provider failures, timeouts, and invalid payloads map to distinct internal classifications and user-safe outcomes.
- [ ] The workflow can be tested with a fake `IWeatherProvider` without HTTP.
- [ ] The Open-Meteo adapter can be tested with representative geocoding and forecast DTO payloads, including unknown weather codes and missing required fields.
- [ ] Health checks, structured logging, trace correlation, and startup configuration validation have explicit registration seams.

## Deferred decisions

The following are intentionally not designed or implemented in Milestone 1:

- Persistence, accounts, and favourites.
- Caching, scheduled work, polling, retries/circuit breakers beyond platform defaults, background services, and provider fallbacks.
- Multiple-provider abstraction, provider selection, or migration policy.
- Secret management.
- Azure-specific telemetry exporters.
- Infrastructure as code and any deployment-resource choice.

## Next recommended step

Implement the city-weather feature as independently verifiable slices: domain contracts and outcomes, `GetWeatherForCity` with a fake provider, Open-Meteo adapter mapping/error handling, then presentation rendering and container execution.
