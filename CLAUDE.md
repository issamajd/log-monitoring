# Log Monitoring

Support-first observability for on-prem Windows deployments. The goal is that a support engineer can investigate a customer issue **without opening massive raw log files**.

## The plan is the source of truth

The VitePress site under [`site/guide/`](site/guide/) is authoritative. Read it before making implementation decisions — especially [`architecture.md`](site/guide/architecture.md), whose *"Current state vs. what is left"* table is the live status of every component.

```
npm install && npm run docs:dev   # http://localhost:5173
```

## Context you cannot get from this repo

- **The application being monitored is `rndfwcore`, at `D:\GitProjects\rndfwcore`.** It is a large .NET 8 enterprise framework (server / service / engine / web / logic). It is **not instrumented** — it currently emits no OpenTelemetry at all. Instrumenting its chokepoints is workstream #1; nothing flows through the pipeline until that lands.
- `rndfwcore` writes its `SystemTrace_*.log` files from `server/server.logging/sinks/FileSink.cs`.

## Decisions that are easy to accidentally undo

- **`src/SystemTraceParser/` is parked on purpose.** It works and has tests, but it is deliberately unwired. `SystemTrace_*.log` files are a local backup that the pipeline does **not** ingest. Do not wire it up — that decision was made explicitly.
- **Kafka is in Milestone 1**, not deferred. `OTel Collector → Kafka → consumers → Elasticsearch/TimescaleDB` is the target pipeline, so the existing compose stack and `src/Consumers/` are the base to build on, not a prototype to strip.
- **One source per signal.** .NET runtime/process metrics come from OTel `AddRuntimeInstrumentation()`/`AddProcessInstrumentation()`, never Telegraf CLR counters. Host metrics come from Telegraf, never the collector's `hostmetrics` receiver.
- **Enrichment happens in the app.** Safe context (session, operation, user) is attached as OTel attributes at emit time. The read-only SQL Server connection is only an *optional on-demand lookup* at investigation time — it is not a per-event enrichment step, and it is not SQL Server performance monitoring.
- **Redaction happens in the application layer**, before telemetry reaches this pipeline. The pipeline stores what it receives.

## Known gaps in the current code

- Host metrics are collected twice (Telegraf *and* the collector's `hostmetrics` receiver), and both run in Linux containers, so neither sees the real Windows host. Telegraf needs to run natively on the host with `win_perf_counters` + `win_services`.
- There is no traces consumer — spans have a Kafka topic but no path into Elasticsearch.
- No Grafana dashboards, alert rules, or enrichment lookup have been built yet.
