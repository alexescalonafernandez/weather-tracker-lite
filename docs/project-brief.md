# Weather Tracker Lite — Milestone 1 Brief

Build a small, containerized ASP.NET Core web application that lets a visitor look up the current weather and a short forecast for a city. This first milestone provides an auditable foundation for Azure, container, deployment, and operations learning without prematurely introducing stateful services or distributed components.

## Outcome

A visitor can enter a city, request an update, and see current conditions plus a short forecast. The application runs locally in Docker and is ready to be deployed as one containerized workload.

## Scope

| Area | Decision |
| --- | --- |
| Application | One ASP.NET Core web application and one deployable container |
| Weather data | Open-Meteo geocoding and forecast APIs |
| Interaction | Manual city search and manual refresh |
| Weather display | Current conditions and a short forecast |
| City data | Ephemeral; no saved favourites or user accounts |
| Initial Azure target | Azure Container Apps |

## Weather information set

Keep the first screen useful without turning it into a data dashboard.

| Section | Information |
| --- | --- |
| Location | Resolved city, country or region, and local observation time |
| Current conditions | Temperature, apparent temperature, and a readable weather condition |
| Wind | Wind speed |
| Short forecast | Three days with minimum and maximum temperature plus precipitation probability |

Humidity, pressure, UV index, charts, hourly detail, and severe-weather indicators are intentionally deferred until they support a specific user need.

## Non-goals

- User authentication or authorization
- Persistent city favourites or a database
- Automatic polling, webhooks, or event-driven updates
- Weather alerts or Azure Functions
- Kubernetes
- Multiple independently deployed application services
- A paid weather API or managed secret store

## Acceptance criteria

- [ ] A visitor can search for a valid city and see its resolved location.
- [ ] A visitor can manually request current weather and a short forecast.
- [ ] Invalid or unavailable city and weather-provider responses produce a clear user-facing outcome.
- [ ] The application can run locally as a Docker container.
- [ ] The external data source and its required attribution are documented.
- [ ] The codebase has a clear place to add health checks, structured logging, and deployment configuration in later milestones.

## Constraints

- Keep Azure and external-service costs low.
- Prefer independently verifiable increments.
- Use Azure services only when a feature creates a clear reason to use them.
- Keep all infrastructure reproducible and practical to destroy and recreate.
- Preserve the principle: “I can delegate it if I can audit it.”

## Planned evolution

| Later milestone | Triggering need | Learning opportunity |
| --- | --- | --- |
| Browser-local favourites | Returning visitor convenience | Client-side state without cloud persistence |
| Scheduled refresh or alerts | Timely weather notifications | Azure Functions, schedules, eventing, and operational monitoring |
| Account-backed favourites | Cross-device personalisation | Identity, persistent data, and authorization |
| Provider migration | A provider requiring credentials | Key Vault, managed identity, and environment configuration |
| AI capability | A useful weather insight or summary | AI-200-aligned data, AI integration, and observability |

## Next step

Choose the first user-facing weather information set and then design the smallest .NET application boundary around it.
