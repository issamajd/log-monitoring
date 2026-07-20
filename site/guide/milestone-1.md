---
title: Milestone 1 — Support Triage Foundation
---

# Milestone 1: Support Triage Foundation

Milestone 1 should prove that support can investigate real customer issues **without opening massive raw log files**. All telemetry comes from OpenTelemetry instrumentation in the application code.

**Primary outcome:** fast support investigation · **Includes** basic alerts and a single-broker **Kafka** buffer.

## Goals

- Search errors by time window, session, operation, BP, DQ, SQL, and component.
- Find slow API, BP, DQ, SQL, and background service operations.
- Flag risky SQL / data-change patterns.
- Show Windows service and server health beside application signals.
- Run on the app server **or** a separate monitoring server.
- Support online and offline installation modes.

## Non-goals

- No AI/ML anomaly detection yet.
- No clustered / high-availability deployment yet.
- No custom alert manager yet.
- No deep SQL Server performance monitoring yet.
- No multi-customer central SaaS assumption.
- No SQL-parameter or secret redaction inside the pipeline — that is handled at the application level.
- No `SystemTrace_*.log` ingestion — the app keeps writing the files as a local backup, but the pipeline does not parse or index them.

## Scope

| Area | Milestone 1 decision |
|---|---|
| Telemetry sources | OpenTelemetry emitted from app code, plus Telegraf on the host for server health. `SystemTrace_*.log` files remain a local backup and are not ingested. |
| Instrumentation layers | API entry points, Windows service jobs, business process calls, data query calls, SQL execution, errors, and key timings. |
| Pipeline | OTel Collector publishes to per-signal Kafka topics (single broker); consumers write to the stores. Buys durability and replay from day one. |
| Storage / search | Elasticsearch for searchable logs/events; TimescaleDB for metrics and time-series summaries; Grafana for dashboards and alerts. |
| Retention | 14–30 days searchable in Elasticsearch, with older raw files archived separately if needed. |
| SQL handling | Store the SQL text and extract operation / table / duration / rows fields. Telemetry arrives already masked from the app. |
| Enrichment | Read-only SQL Server connector resolves `SessionID` to safe support context when configured. |
| Notifications | Grafana alert state, email, and optional webhook support for Teams, Slack, or custom integrations. |
| Forwarding | Local-first. Optional forwarding only when needed and approved. |

## Workstreams

1. Add OpenTelemetry instrumentation package/adapters to the app, including session/user context attributes.
2. Configure OTel Collector, Kafka, Elasticsearch, TimescaleDB, and Grafana through Docker Compose.
3. Adapt the Kafka consumers to the final schemas (logs, metrics) and add traces handling.
4. Set up Telegraf on the Windows host for server-health metrics and service state.
5. Create the optional on-demand session-enrichment lookup.
6. Create support-first dashboards and search views.
7. Add basic alerts and notification channels.
8. Package online and offline installation flows.

## Default thresholds

Thresholds must be configurable per customer, module, endpoint, BP, DQ, SQL table, job, and environment.

- **API / BP warning:** over 3 seconds.
- **API / BP critical:** over 10 seconds.
- **SQL warning:** over 1 second.
- **SQL critical:** over 5 seconds.
- **Risky data-change alerts** use SQL operation, table, `WHERE` detection, and rows affected.

## Success criteria

- Support can answer *"what failed around this time?"* in under 5 minutes.
- Support can search by `SessionID`, `OperationID`, BP/DQ name, SQL operation, component, and time window.
- Slow operations are visible without reading raw log files.
- Risky SQL / data-change events are flagged with clear reasons.
- Monitoring-stack health is visible, including ingestion stopped and disk almost full.
- Installation works on the same app server and on a separate monitoring server.
