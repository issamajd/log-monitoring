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
 Windows app service ──▶ OpenTelemetry SDK ──▶ OTel Collector ──▶ Kafka ──┬─▶ Logs/Traces consumer ──▶ Elasticsearch
 (API, jobs, BP/DQ,      (traces, metrics,     (receive, batch,  (durable └─▶ Metrics consumer ─────▶ TimescaleDB
  SQL, errors,            structured logs)      route)            buffer)
  .NET process)

 Telegraf (on the host) ──▶ OTel Collector ──▶ Kafka ──▶ Metrics consumer ──▶ TimescaleDB   (host / server health)
 (CPU, RAM, disk, network,
  Windows service state)
```

Elasticsearch holds searchable events and traces, TimescaleDB holds metrics and time-series summaries, and Grafana serves dashboards and alerts on top of both.

**Kafka is part of Milestone 1.** The collector publishes each signal as human-readable OTLP JSON to its own topic (`telemetry-logs`, `telemetry-metrics`, `telemetry-traces`); dedicated consumers subscribe and write to the stores. The buffer decouples ingestion from storage: Elasticsearch or TimescaleDB can restart or fall behind without losing telemetry, and events can be replayed. Unparseable messages and per-document Elasticsearch rejections already route to a `dlq` topic. Milestone 1 runs a **single-broker** Kafka; advanced reliability — consumer scaling, DLQ replay tooling, backpressure views — matures in Milestone 2.

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

End-to-end checks for everything below are scripted in the [verification runbook](./verification-runbook).

| Component | What we have now | What needs to be done |
|---|---|---|
| OpenTelemetry in the app | **`rndfwcore` chokepoints are instrumented** on its `log-monitoring` branch (API, jobs, BP, DQ, SQL spans + metrics, OTLP log bridge, session/user attributes, method/system error kind with exception details) | Merge the branch; confirm the CI dispatch carries the hosts' OTel/Grpc/Protobuf DLLs (verified by hand on 2026-10-12); instrument the standalone `service/` hosts if in scope |
| .NET / process metrics | `AddRuntimeInstrumentation()` + `AddProcessInstrumentation()` enabled in the `server.service` host bootstrap | Nothing — keep Telegraf CLR counters removed |
| Host metrics | Collector `hostmetrics` receiver removed; containerized Telegraf removed; native Windows config at `deploy/telegraf-windows/` | Install Telegraf as a Windows service on each host; fill in the real FWCORE service names in `win_services` |
| Collector → Kafka routing | Collector publishes to per-signal Kafka topics; `telemetry-traces` is created with 12 partitions | Nothing for Milestone 1 |
| Kafka consumers | Logs, metrics **and traces** consumers build and run; OTLP protos vendored via `src/OtlpProto`; offsets commit only after successful storage flush; per-document Elasticsearch rejections and unparseable messages route to the `dlq` topic (transient 429/503 rejections are retried in-place first) | Replay tooling for the `dlq` topic (Milestone 2) |
| On-demand enrichment | **Built (opt-in) and live-verified (2026-08-03)** against a real SQL Server: schema-portable deploy scripts (`<<schema>>` + preflight check), EXECUTE-only login returning the 15 allowlisted fields, direct SELECT denied | Per-customer DBA setup; rotate the `mon_session_lookup` password; pin a static SQL port |
| Storage / search | Elasticsearch + TimescaleDB stand up via Docker Compose; **ES mappings/ILM finalized** in the `elasticsearch-init` templates (every LogRecord field mapped, flattened attributes, `strings_as_keywords` dynamic template, warm-phase ILM for logs and traces); **TimescaleDB schema complete** in `docker/timescaledb/init.sql` (hypertable on 1-hour chunks, compression after 1 day, 1min/5min/1hour/1day continuous aggregates with refresh and retention policies) | Verify the metric schema against real `win_*` and `.NET` metric shapes once Telegraf runs on a host |
| Dashboards & alerts | Five provisioned support-first dashboards (triage/error search, slow operations, SQL insight, host & service health, pipeline health) plus file-provisioned alert rules with the Milestone 1 thresholds; a Prometheus service scrapes the collector's self-telemetry for the pipeline-health panels | Verify panel queries against a live stack per the [verification runbook](./verification-runbook) (metric names flagged in panel descriptions); set real `ALERT_EMAIL_TO`/`ALERT_WEBHOOK_URL` per deployment |
| SystemTrace parser | Working pipe-delimited parser with unit tests | **Nothing** — kept in the repository unwired; SystemTrace files are a local backup and are not ingested |

::: info Architecture principle
Each component should be useful on its own: the OTel package emits good telemetry, the collector routes reliably, and dashboards still explain what is happening if one data source is temporarily missing.
:::
