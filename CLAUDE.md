# Log Monitoring

Support-first observability for on-prem Windows deployments. The goal is that a support engineer can investigate a customer issue **without opening massive raw log files**.

## The plan is the source of truth

The VitePress site under [`site/guide/`](site/guide/) is authoritative. Read it before making implementation decisions — especially [`architecture.md`](site/guide/architecture.md), whose *"Current state vs. what is left"* table is the live status of every component.

```
npm install && npm run docs:dev   # http://localhost:5173
```

## Context you cannot get from this repo

- **The application being monitored is `rndfwcore`, at `D:\GitProjects\rndfwcore`.** It is a large .NET 8 enterprise framework (server / service / engine / web / logic). Its chokepoints are instrumented on its `log-monitoring` branch (not yet merged to `dev`); the pipeline has been verified end-to-end against it.
- `rndfwcore` writes its `SystemTrace_*.log` files from `server/server.logging/sinks/FileSink.cs`.

## Decisions that are easy to accidentally undo

- **`src/SystemTraceParser/` is parked on purpose.** It works and has tests, but it is deliberately unwired. `SystemTrace_*.log` files are a local backup that the pipeline does **not** ingest. Do not wire it up — that decision was made explicitly.
- **Kafka is in Milestone 1**, not deferred. `OTel Collector → Kafka → consumers → Elasticsearch/TimescaleDB` is the target pipeline, so the existing compose stack and `src/Consumers/` are the base to build on, not a prototype to strip.
- **One source per signal.** .NET runtime/process metrics come from OTel `AddRuntimeInstrumentation()`/`AddProcessInstrumentation()`, never Telegraf CLR counters. Host metrics come from Telegraf, never the collector's `hostmetrics` receiver.
- **Enrichment happens in the app.** Safe context (session, operation, user) is attached as OTel attributes at emit time. The read-only SQL Server connection is only an *optional on-demand lookup* at investigation time — it is not a per-event enrichment step, and it is not SQL Server performance monitoring.
- **Redaction happens in the application layer**, before telemetry reaches this pipeline. The pipeline stores what it receives.

## Known gaps in the current code

- Telegraf must still be **installed on each Windows host** (config + install steps in `deploy/telegraf-windows/`); the collector `hostmetrics` receiver and the containerized Telegraf are already removed.
- `rndfwcore` instrumentation lives on its `log-monitoring` branch and is not merged to `dev` yet; deployment must ensure the OpenTelemetry NuGet DLLs land next to the host executables (`Private=False` HintPath references do not copy them).
- Dashboards, alerts, datasources, and the DLQ path were verified against the live local stack on 2026-07-24 (provisioning loads clean, parse failures reach `dlq`, `.NET` metric names confirmed). Two things remain unverifiable locally: the `win_*` host metric names (Telegraf has never run against this stack — confirm on the first real host install) and `sql_danger_flags`/job-span shapes (no such documents emitted yet).
- `rndfwcore` does not emit a `host.name` resource attribute, so its telemetry has no `host_name` field; the dashboards' Lucene filters use `(host_name:$host OR (NOT _exists_:host_name))` to tolerate that. The proper fix is app-side, in the instrumentation on the `log-monitoring` branch. (Lucene gotcha: `a OR NOT b` without the inner parentheses matches nothing.)
- Alert notifications use placeholder defaults; set `ALERT_EMAIL_TO` / `ALERT_WEBHOOK_URL` (consumed via `$__env{}` in `docker/grafana/provisioning/alerting/contact-points.yaml`) per deployment.
- The on-demand enrichment lookup has not been built yet.
