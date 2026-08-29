# Azure Portal Logs Operations Runbook

Use Azure Portal **Logs** to investigate the deployed Weather Tracker Lite Container App without changing Azure resources. Start small: select the deployed Log Analytics workspace, set a narrow time range, discover the actual schema, then run only the applicable bounded query.

## Quick path

1. In the Azure portal, open the deployed Log Analytics workspace and select **Logs**. Confirm the query scope is that workspace, not a subscription-wide scope.
2. Set the portal time range to **Last 30 minutes** for an incident or **Last 24 hours** for a portfolio summary. Narrow it before increasing result limits.
3. Run schema discovery. The confirmed tables are `ContainerAppConsoleLogs_CL` and `ContainerAppSystemLogs_CL`; their deployed columns must still be verified.
4. Copy a query below, replacing the candidate column names only when discovery confirms them. Keep `take` limits and time bounds.

## Schema discovery and query rules

Run this first. It is bounded and does not expose log values.

```kusto
union isfuzzy=true
    (ContainerAppConsoleLogs_CL | getschema),
    (ContainerAppSystemLogs_CL | getschema)
| project TableName, ColumnName, ColumnType
| order by TableName asc, ColumnName asc
```

Then inspect a small sample only when necessary:

```kusto
ContainerAppConsoleLogs_CL
| take 10
```

The query templates use common *candidate* names such as `TimeGenerated`, `Log_s`, `ContainerAppRevisionName_s`, and `ContainerAppReplicaName_s`. They are **not guaranteed deployed columns**. Before use, adjust each template to the names and types returned by `getschema`. If no timestamp column is available, retain a small `take` limit and state that the result is not time-filtered; do not remove the limit.

## Application log inventory

**Precondition:** discovery confirms the timestamp and message columns used below. Replace candidate names as needed.

```kusto
let startTime = ago(30m);
ContainerAppConsoleLogs_CL
| where TimeGenerated >= startTime
| extend Message = tostring(Log_s)
| project TimeGenerated, Message
| order by TimeGenerated desc
| take 100
```

Use this inventory first to confirm that application logs are arriving and to identify the exact rendered text for later filters. Do not export raw message text into portfolio evidence.

## Weather outcome classifications

The application emits `Weather query completed with outcome classification {OutcomeClassification}`. Expected classifications from the current implementation are `Success`, `NotFound`, `ProviderUnavailable`, `TimedOut`, `InvalidProviderData`, and `InvalidRequest`.

**Precondition:** confirm the console timestamp and message columns.

```kusto
let startTime = ago(24h);
ContainerAppConsoleLogs_CL
| where TimeGenerated >= startTime
| extend Message = tostring(Log_s)
| where Message has "Weather query completed with outcome classification"
| extend OutcomeClassification = extract(@"classification ([A-Za-z]+)", 1, Message)
| summarize Events = count() by OutcomeClassification
| order by Events desc
| take 20
```

If structured logging fields are materialized as separate columns, group by the discovered field instead of parsing `Log_s`. Do not add city input to the projection or evidence.

## Open-Meteo dependency outcomes and latency

The application logs dependency operation, outcome, HTTP status when available, and duration. It calls the `geocoding` and `forecast` operations. This query deliberately extracts only these operational values from the message when structured fields are unavailable.

**Precondition:** confirm the console timestamp and message columns; use discovered structured fields in preference to parsing.

```kusto
let startTime = ago(24h);
ContainerAppConsoleLogs_CL
| where TimeGenerated >= startTime
| extend Message = tostring(Log_s)
| where Message has "Open-Meteo dependency call completed"
| extend DependencyOutcome = extract(@"outcome ([A-Za-z]+)", 1, Message),
         ProviderOperation = extract(@"operation ([A-Za-z]+)", 1, Message),
         DurationMs = todouble(extract(@"duration ([0-9.]+)ms", 1, Message))
| summarize Calls = count(),
            AverageDurationMs = round(avg(DurationMs), 1),
            P95DurationMs = round(percentile(DurationMs, 95), 1)
    by ProviderOperation, DependencyOutcome
| order by Calls desc
| take 20
```

If the duration or status fields are absent, report only the outcomes observed. Do not infer latency or HTTP status from missing data.

## Revision, startup, liveness, and readiness events

Probe and lifecycle messages are platform-generated, so their exact message and column shape must be discovered from the system table. First inventory a narrow window, then replace the broad terms with the exact observed wording.

```kusto
let startTime = ago(2h);
ContainerAppSystemLogs_CL
| where TimeGenerated >= startTime
| extend Message = tostring(Log_s)
| where Message has_any ("revision", "startup", "liveness", "readiness", "probe")
| project TimeGenerated, Message
| order by TimeGenerated desc
| take 100
```

If discovery shows separate event, reason, revision, or replica columns, project those columns instead of `Log_s`. The deployed design maps startup and liveness to `/health/live` and readiness to `/health/ready`; readiness validates local Open-Meteo configuration, not live Open-Meteo availability.

## Scale and revision lifecycle

Use the same confirmed system-table columns to look for replica creation, termination, scaling, activation, deactivation, and revision state messages.

```kusto
let startTime = ago(24h);
ContainerAppSystemLogs_CL
| where TimeGenerated >= startTime
| extend Message = tostring(Log_s)
| where Message has_any ("scale", "replica", "revision", "activation", "deactivation", "restart")
| project TimeGenerated, Message
| order by TimeGenerated desc
| take 200
```

The approved scale range is zero through one replica. A quiet period can therefore be expected when the app scales to zero; absence of console logs alone is not a failure.

## Operational troubleshooting flow

1. **No expected application response:** inspect system events for the active revision, startup, liveness, readiness, and restart messages. Verify the observed message shape before filtering further.
2. **Revision does not serve traffic:** check startup and liveness first, then readiness. Do not treat an Open-Meteo outage as a readiness failure because the current readiness check validates configuration only.
3. **Weather lookup fails:** run the outcome classification query, then the Open-Meteo outcome query. Distinguish `NotFound` and `InvalidRequest` from `ProviderUnavailable`, `TimedOut`, or `InvalidProviderData`.
4. **Slow weather lookup:** use latency only when the duration field is present and parseable; compare `geocoding` and `forecast` outcomes in the same bounded time window.
5. **No logs:** confirm workspace scope and time range, then query both confirmed tables with schema discovery and a ten-row sample. Allow for ingestion delay and scale-to-zero before declaring an outage.

## Safe evidence capture

Record aggregate, time-bounded findings rather than raw events. A portfolio-safe statement includes the UTC window, scope described generically, query purpose, counts or percentages, and a conclusion—for example: “During a 24-hour window, dependency outcomes were predominantly successful; no provider timeout events were observed.”

Do **not** include raw request URLs, city input, generated FQDNs, IP addresses, workspace IDs, subscription or tenant IDs, operator identities, tokens, cookies, authorization headers, connection strings, or credentials. The application sends city text to Open-Meteo in request URLs; avoid displaying or exporting raw console lines that could contain it. Use screenshots only after redacting query text, scope identifiers, and result values that could identify a person, account, or request.

## Expected gaps and limitations

- Log ingestion is not instantaneous; a recent request can be absent temporarily.
- Table schemas and platform message text can vary by diagnostic configuration and service evolution. Discovery is authoritative for the deployed workspace.
- Current application logging supports weather outcome classifications and Open-Meteo outcomes/latency, but it does not guarantee structured fields are materialized as columns.
- There is no Application Insights deployment, distributed tracing, client telemetry, or persistent weather-request history.
- A scale-to-zero interval can produce no application logs until traffic activates a replica.
- Health endpoints were acceptance-tested, but platform probe events are not guaranteed to appear in a particular column or wording.

## Azure CLI limitation and Portal workaround

The locally observed Azure CLI Log Analytics query path currently fails with `PathNotFoundError`. Do not retry it as proof that logs are absent and do not change CLI, Azure, RBAC, or diagnostic configuration as part of this runbook. Use the Azure Portal **Logs** experience against the deployed workspace, follow schema discovery, and capture only the bounded, redacted findings described above.

## Review checklist

- [ ] Portal scope is the deployed Log Analytics workspace.
- [ ] Time range and `take` limits are narrow enough for the question.
- [ ] Schema discovery ran before candidate column names were used.
- [ ] Findings are aggregated and exclude city input, credentials, and account identifiers.
- [ ] Any unavailable field or missing log is recorded as a limitation, not an inferred result.
