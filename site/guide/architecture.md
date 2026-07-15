---
title: Architecture
---

# Dual-source telemetry pipeline

The system should not choose between existing logs and OpenTelemetry. It uses **both**: logs give immediate value for current deployments, and OpenTelemetry gives cleaner structured traces and metrics going forward.

## Milestone 1 flow

Two source lanes feed a shared collector and split into the right store for each signal type.

```
 OpenTelemetry lane
 ───────────────────
 Windows app service ──▶ OpenTelemetry SDK ──▶ OTel Collector ──▶ TimescaleDB
 (API, jobs, BP/DQ,      (traces, metrics,     (receive, batch,   (metrics,
  SQL, errors)            structured logs)      enrich, route)     summaries)

 Log lane
 ────────
 SystemTrace log files ──▶ Log parser agent ──▶ Elasticsearch ──▶ Grafana
 (large pipe-delimited     (tail, parse,        (searchable logs,  (dashboards,
  logs from the app)        enrich sessions)     SQL text, timelines) alerts)
```

Kafka is intentionally a **later** pipeline stage. It stays part of the target architecture, but Milestone 1 can prove parsing, enrichment, search, dashboards, and alerts first.

## SystemTrace parser

The sample log format is pipe-delimited and includes structured values that should be extracted into fields rather than treated as plain text:

- Timestamp with timezone.
- Log type such as Trace or Performance.
- Component / category and event name.
- Service / module / class / method context.
- `SessionID`, `ClientRequestTranID`, `OperationID`, and thread ID.
- Total elapsed and last-log elapsed.
- Message and data payload.
- SQL ID, transaction ID, execution time, rows affected, result summary, connection string.
- BP / DQ names and method names.

## OpenTelemetry instrumentation

Instrumentation should be added across the app, not only around HTTP calls:

- **API request spans** — endpoint, method, status, duration, user/session context.
- **Windows service job spans** — job name, run ID, status, duration.
- **BP spans** — BP name, method, session, operation, result.
- **DQ spans** — query object name, method, operation type.
- **SQL spans** — SQL text (masked at the app level), execution time, rows affected, danger flags.
- **Metrics** — request counts, error counts, operation duration, queue/job runs, ingestion health.

## Field model

| Field group | Examples | Purpose |
|---|---|---|
| Identity | `customer_id`, `site_id`, `environment`, `server_name`, `service_name` | Filter dashboards and searches by deployment. |
| Correlation | `session_id`, `operation_id`, `client_request_tran_id`, `trace_id`, `span_id` | Connect logs, traces, API calls, BP/DQ calls, and SQL events. |
| Application | `component`, `event_name`, `bp_name`, `bp_method`, `dq_name`, `dq_method` | Find where a failure or slowdown happened in the app stack. |
| SQL | `sql_operation`, `sql_text`, `tables`, `execution_ms`, `rows_affected`, `danger_flags` | Search slow and risky SQL. |
| Outcome | `severity`, `status`, `error_type`, `message`, `duration_ms` | Drive triage, dashboards, and alerts. |

::: info Architecture principle
Each component should be useful on its own: the parser produces validated events, the OTel package emits good telemetry, the collector routes reliably, and dashboards still explain what is happening if one data source is temporarily missing.
:::
