---
title: Support workflows
---

# Support-first investigation workflow

The first dashboards should help support triage customer issues quickly. Support usually starts with a **time window**, then narrows by session, operation, user context, BP/DQ, SQL, component, or business object.

## Default triage path

1. Select customer / site / environment.
2. Select the reported time window.
3. Check the error, slow-operation, risky-SQL, service, and ingestion summary panels.
4. Filter by `SessionID`, `OperationID`, BP/DQ name, SQL operation/table, endpoint, job, component, or business identifier.
5. Open the session or operation timeline.
6. Export/share the relevant filtered events if escalation is needed.

From **Triage & Error Search** or **SQL Insight**, open a `session_id` cell's **Session context (app DB)** link to reach **Session Lookup**. This opt-in lookup is read-only and runs only when opened; its result is not stored in monitoring.

## Dashboard set

- **Triage Overview** — errors, slow operations, risky SQL, service state, ingestion health.
- **Error Explorer** — grouped by component, event, BP/DQ, SQL, session, and operation.
- **Slow Operations** — API/BP/DQ/SQL timings with configurable thresholds.
- **SQL Risk** — updates/deletes, high rows affected, missing-`WHERE` detection, slow SQL.
- **Session Timeline** — ordered events for one `SessionID` or `OperationID`.
- **Server Health** — CPU, RAM, disk, network, Windows service/process state.

## Search filters

- Time range.
- Customer / site / environment / server.
- Severity / log type / event.
- `SessionID`, `OperationID`, `ClientRequestTranID`.
- API endpoint, job name, component, class, method.
- BP name/method, DQ name/method.
- SQL operation, table/entity, rows affected, duration band.
- User context from read-only enrichment when available.

## Alerting in Milestone 1

Milestone 1 includes dashboards/search plus basic alerts. Alerts should stay high-signal and configurable.

| Alert | Default trigger | Why support cares |
|---|---|---|
| Service down | Windows service/process unavailable or app health check failing. | Confirms a customer outage quickly. |
| Ingestion stopped | No new logs/telemetry for the expected interval. | Prevents false confidence when the pipeline is blind. |
| Disk almost full | Disk crosses warning/critical thresholds. | Protects the app server and monitoring stack. |
| High error rate | Error count or rate exceeds a configured threshold. | Highlights active failure periods. |
| Slow operation | API/BP/DQ duration crosses a module threshold. | Finds performance issues before support reads raw logs. |
| Slow SQL | SQL execution duration crosses a configured threshold. | Identifies database-driven slowdowns. |
| Risky SQL / data change | Risk flags such as high rows affected, update/delete patterns, or missing `WHERE`. | Surfaces risky data changes quickly. |

## Notification channels

- Grafana alert state is always visible in dashboards.
- Email is the default on-prem notification channel.
- Webhook support is optional for Teams, Slack, or customer-specific integrations.
- Each customer can enable or disable notification channels.
- Critical monitoring-system alerts stay visible even when outbound channels are not configured.
