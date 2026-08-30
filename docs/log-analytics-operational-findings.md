# Log Analytics Operational Findings

This portfolio evidence records aggregate observations from a seven-day Azure Portal Log Analytics window. The observed weather-query outcomes were successful, while geocoding was the slower of the two observed external dependency operations. These findings describe this small window only; they are not a stable performance baseline.

## Executive summary

| Observation | Finding | Operational interpretation |
|---|---:|---|
| Weather-query outcomes | 3 total; all `Success` | Every observed weather query completed successfully during the window. |
| Geocoding dependency | 3 successful calls; mean 798.5 ms; P95 811.5 ms | Geocoding was the slower observed dependency and is the first operation to investigate if lookup latency becomes a concern. |
| Forecast dependency | 3 successful calls; mean 102.1 ms; P95 111.3 ms | Forecast completed successfully and was materially faster than geocoding in this window. |

## Interpretation and limits

The observed successful outcomes provide limited evidence that the weather-query path and both dependency operations completed successfully during the selected window. The comparison identifies geocoding, rather than forecast retrieval, as the slower observed dependency.

However, each dependency measure contains only three calls. A three-call sample cannot characterize normal latency, variability, tail behavior, or a service-level expectation. The reported mean and P95 are descriptive observations for this window, not a stable baseline or a performance guarantee. Collect a larger, representative sample across different traffic periods before setting thresholds or drawing trend conclusions.

## Operational follow-up

This record complements the [Log Analytics operations runbook](log-analytics-operations-runbook.md). Use the runbook's bounded outcome and dependency-latency queries to repeat the observation, compare both operations in the same window, and investigate failures or slower responses without changing Azure resources.

## Privacy-safe evidence practice

This document records only aggregate counts, outcomes, and latency statistics. It intentionally excludes raw logs, city input, request URLs, FQDNs, workspace or account identifiers, credentials, and timestamps. Future portfolio evidence should remain time-bounded and aggregate-only; any screenshots should be redacted before sharing.

## Next investigation questions

1. Does a larger sample, collected across representative traffic periods, continue to show geocoding as the slower dependency?
2. Are geocoding and forecast latency distributions stable, or do they vary with traffic, cold starts, or external-provider conditions?
3. Do slower dependency calls correlate with weather-query failures, timeouts, or provider-error classifications?
4. What sample size and observation period are sufficient to establish a defensible operational baseline and investigation threshold?
