---
title: Architecture
---

# OpenTelemetry-first telemetry pipeline

All application telemetry comes from **OpenTelemetry instrumentation in the app code**: structured logs, traces, and metrics, emitted through one pipeline into purpose-built stores.

::: info SystemTrace files: backup only
The application keeps writing `SystemTrace_*.log` files as a **local backup**, but the monitoring system does **not** ingest them — they are not parsed, not fed to Elasticsearch, and not part of the pipeline. The `SystemTraceParser` project stays in the repository (unwired) in case that decision is revisited.
:::

## Pipeline

```
 Windows app service ──▶ OpenTelemetry SDK ──▶ OTel Collector ──┬─▶ TimescaleDB   (metrics)
 (API, jobs, BP/DQ,      (traces, metrics,     (receive, batch,  └─▶ Elasticsearch (logs + traces)
  SQL, errors,            structured logs)      enrich, route)
  .NET process)

 Telegraf (on the host) ──▶ OTel Collector ──▶ TimescaleDB      (host / server health)
 (CPU, RAM, disk, network,
  Windows service state)
```

Elasticsearch holds searchable events and traces, TimescaleDB holds metrics and time-series summaries, and Grafana serves dashboards and alerts on top of both.

Kafka stays a **later** pipeline stage (Milestone 2). Milestone 1 routes from the collector straight to the stores and can prove instrumentation, enrichment, search, dashboards, and alerts without it.

## One source per concern — no double collection

Every signal has exactly **one** owner. This removes the redundancy that exists in the current prototype.

| Concern | Single source | Explicitly removed |
|---|---|---|
| App / business signals — API, jobs, BP, DQ, SQL, errors, durations | OpenTelemetry SDK in the app | — |
| **.NET runtime / process health** — GC, heap, thread pool, exceptions, process CPU/memory | OTel `AddRuntimeInstrumentation()` + `AddProcessInstrumentation()` | Telegraf `.NET CLR` `win_perf_counters` |
| **Host / OS / infrastructure** — CPU, RAM, disk space, disk I/O, network, Windows service state | **Telegraf, running natively on the Windows host** | OTel Collector `hostmetrics` receiver |
| Searchable log events | OTLP structured logs | SystemTrace file ingestion |

::: info Why these choices
- **.NET metrics via OTel**, not Telegraf: runtime metrics ride the same pipeline as traces and app metrics, correlated by resource attributes, tagged to *this* service, with no separate Windows-only counter agent. A GC pause then lines up with the slow operation it caused.
- **Host metrics via Telegraf on the host**, not the collector's `hostmetrics` receiver: a containerized collector only measures its own Linux VM, not the real Windows app server. Native Telegraf with `win_perf_counters` + `win_services` sees the true host and the FWCORE service.
:::

## OpenTelemetry instrumentation

Instrumentation should be added across the app, not only around HTTP calls:

- **API request spans** — endpoint, method, status, duration, user/session context.
- **Windows service job spans** — job name, run ID, status, duration.
- **BP spans** — BP name, method, session, operation, result.
- **DQ spans** — query object name, method, operation type.
- **SQL spans** — SQL text (masked at the app level), execution time, rows affected, danger flags.
- **Metrics** — request counts, error counts, operation duration, queue/job runs, ingestion health.
- **.NET runtime / process metrics** — GC, heap, thread pool, exceptions, process CPU/memory (the single source for .NET health).

## Enrichment: at the source, in the app

The app already holds the support context at the moment an event happens (`UserName`, `SessionGuid`, operation), so it attaches that context **directly as OTel attributes** before telemetry leaves the process — no lookup, no cache, no extra component.

| Context | How it is enriched |
|---|---|
| Cheap, safe, always useful — `session_id`, `operation_id`, `client_request_tran_id`, username, environment | The app emits it directly as span/log/resource attributes. |
| Sensitive or heavy — customer/entity details, business context | **Optional on-demand lookup** at investigation time via a read-only SQL Server connection, resolved only for the sessions support actually examines and **not stored per-event**. |

This split keeps telemetry lean (the correlation key travels with every event; heavy context does not), and keeps personal data out of monitoring storage by default. The on-demand lookup is read-only, cacheable, and can be disabled per customer — see [Data & security](/guide/data-security). It is a **lookup** use of SQL Server, not SQL Server performance monitoring, which remains a non-goal.

## Field model

| Field group | Examples | Purpose |
|---|---|---|
| Identity | `customer_id`, `site_id`, `environment`, `server_name`, `service_name` | Filter dashboards and searches by deployment. |
| Correlation | `session_id`, `operation_id`, `client_request_tran_id`, `trace_id`, `span_id` | Connect logs, traces, API calls, BP/DQ calls, and SQL events. |
| Application | `component`, `event_name`, `bp_name`, `bp_method`, `dq_name`, `dq_method` | Find where a failure or slowdown happened in the app stack. |
| SQL | `sql_operation`, `sql_text`, `tables`, `execution_ms`, `rows_affected`, `danger_flags` | Search slow and risky SQL. |
| Outcome | `severity`, `status`, `error_type`, `message`, `duration_ms` | Drive triage, dashboards, and alerts. |

## Current state vs. what is left

| Component | What we have now | What needs to be done |
|---|---|---|
| OpenTelemetry in the app | Instrumentation helper library exists; **`rndfwcore` is not instrumented** | Add OTel to `rndfwcore` chokepoints (API, jobs, BP, DQ, SQL, errors) with session/user attributes — this is the first workstream; nothing flows until it lands |
| .NET / process metrics | Runtime + process instrumentation available in the helper library | Enable alongside app instrumentation; **do not** enable Telegraf CLR counters |
| Host metrics | Telegraf config exists but runs in a container with `win_perf_counters` commented out | Run Telegraf **on the Windows host**; enable `win_perf_counters` + `win_services`; **remove the collector `hostmetrics` receiver** |
| Collector routing | Collector runs; routes through Kafka (prototype) | Route straight to Elasticsearch / TimescaleDB; Kafka returns in Milestone 2 |
| On-demand enrichment | Not built | Optional read-only SQL Server lookup keyed on `SessionID`, resolved at investigation time |
| Storage / search | Elasticsearch + TimescaleDB stand up via Docker Compose | Finalize index mappings, retention/ILM, and metric schemas |
| Dashboards & alerts | Not built | Build the support-first Grafana dashboards and the basic alert rules |
| SystemTrace parser | Working pipe-delimited parser with unit tests | **Nothing** — kept in the repository unwired; SystemTrace files are a local backup and are not ingested |

::: info Architecture principle
Each component should be useful on its own: the OTel package emits good telemetry, the collector routes reliably, and dashboards still explain what is happening if one data source is temporarily missing.
:::
