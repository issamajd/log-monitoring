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
- The `log-monitoring` branch is checked out in a worktree at `D:\GitProjects\rndfwcore-log-monitoring`; the main checkout stays on `dev`. A fresh worktree needs `BUILD/RUNTIME/dlls/thirdparty/` copied from the main checkout (it is gitignored). There is no repo-wide solution: build the single-project `.sln` files in `BUILD/modulesSolutions.config.yml` order. Projects reference each other through `Private=False` HintPath DLLs, so NuGet packages added to libraries never reach host bin folders; hosts (server.service, server.console) need their own PackageReferences. server.configuration, server.highavailability and server.console fail locally for unrelated reasons.
- The pipeline was verified against **rnds4assets** (SQL Server; connection details in its `BUILD\SetUserVars.bat`, company VPN required). Its tables live in schema `master`, not `dbo`; `LOCALTIME` does not exist despite `ColumnNames.cs`. The named instance uses a dynamic port: resolve it through SQL Browser (UDP 1434) and pin a static port before relying on it. `CHECK_POLICY = ON` silently rejects weak passwords in `CREATE LOGIN`. Agents can't run `sqlcmd` with credentials; give the user a `! sqlcmd ...` line. Pick a current session with `SELECT TOP 1 SESSIONID FROM master.CLIENTSESSIONLOG ORDER BY CREATEDDATE DESC`.

## Decisions that are easy to accidentally undo

- **`src/SystemTraceParser/` is parked on purpose.** It works and has tests, but it is deliberately unwired. `SystemTrace_*.log` files are a local backup that the pipeline does **not** ingest. Do not wire it up — that decision was made explicitly.
- **Kafka is in Milestone 1**, not deferred. `OTel Collector → Kafka → consumers → Elasticsearch/TimescaleDB` is the target pipeline, so the existing compose stack and `src/Consumers/` are the base to build on, not a prototype to strip.
- **One source per signal.** .NET runtime/process metrics come from OTel `AddRuntimeInstrumentation()`/`AddProcessInstrumentation()`, never Telegraf CLR counters. Host metrics come from Telegraf, never the collector's `hostmetrics` receiver.
- **Enrichment happens in the app.** Safe context (session, operation, user) is attached as OTel attributes at emit time. The read-only SQL Server connection is only an *optional on-demand lookup* at investigation time — it is not a per-event enrichment step, and it is not SQL Server performance monitoring.
- **Redaction happens in the application layer**, before telemetry reaches this pipeline. The pipeline stores what it receives.
- **No backward-compatibility shims until there is a stable version.** Compat was dropped on purpose on 2026-08-04 (volumes wiped, Protobuf fallback and `.keyword` sub-fields removed).
- **`elasticsearch-init` creates `logs-000001`/`traces-000001` only when the alias is absent, with `--fail` on every call.** Don't go back to swallowing the 400: after an ILM rollover, re-creating `-000001` would claim `is_write_index` for a second index and writes would stop. `kafka-init`'s `--if-not-exists` is plain restart idempotency; keep it.
- **Never point anything at an ILM-managed index name.** ILM `shrink` renamed indices and hid all warm data from pattern-based datasources; shrink is removed and Grafana queries the aliases.

## Known gaps in the current code

- Telegraf must still be **installed on each Windows host** (config + install steps in `deploy/telegraf-windows/`); the collector `hostmetrics` receiver and the containerized Telegraf are already removed.
- `rndfwcore` instrumentation lives on its `log-monitoring` branch and is not merged to `dev` yet; deployment must ensure the OpenTelemetry NuGet DLLs land next to the host executables (`Private=False` HintPath references do not copy them).
- Dashboards, alerts, datasources, and the DLQ path were verified against the live local stack on 2026-07-24 (provisioning loads clean, parse failures reach `dlq`, `.NET` metric names confirmed). Two things remain unverifiable locally: the `win_*` host metric names (Telegraf has never run against this stack — confirm on the first real host install) and `sql_danger_flags`/job-span shapes (no such documents emitted yet).
- `rndfwcore` does not emit a `host.name` resource attribute, so its telemetry has no `host_name` field; the dashboards' Lucene filters use `(host_name:$host OR (NOT _exists_:host_name))` to tolerate that. The proper fix is app-side, in the instrumentation on the `log-monitoring` branch. (Lucene gotcha: `a OR NOT b` without the inner parentheses matches nothing.)
- Alert notifications use placeholder defaults; set `ALERT_EMAIL_TO` / `ALERT_WEBHOOK_URL` (consumed via `$__env{}` in `docker/grafana/provisioning/alerting/contact-points.yaml`) per deployment.
- The on-demand enrichment lookup is built as an opt-in Grafana MSSQL datasource plus an application-database stored procedure, disabled by default. It was live-verified against a real SQL Server on 2026-08-03 and the deploy scripts are schema-portable (`<<schema>>` placeholder + preflight check), but it still needs per-customer DBA setup, a rotated `mon_session_lookup` password, and a pinned static SQL port. The procedure requires FW >= 2.2.0.0 columns.
- All three consumers use `AutoOffsetReset.Latest` with manual commits, so a consumer group with no committed offset starts at the end and silently drops whatever is already buffered in Kafka (and a group reaped after `offsets.retention.minutes` loses everything buffered while it was away). That contradicts the no-loss buffer goal; switching to `Earliest` is the first Milestone 2 item, paired with a deliberate starting offset because a fresh group would otherwise replay the whole retention window.
- Consumers parse **OTLP JSON only**. The legacy Protobuf fallback was removed once pre-migration records expired; `src/OtlpProto` still vendors the official protos because the generated C# types are what the JSON parser deserializes into. The `OpenTelemetry.Proto` NuGet package does not exist, and `IF.APM.OpenTelemetry.Proto` is a suspected typosquat: never add it. The `.gitignore`'s blanket `logs/` rule hides any vendored path containing a `logs/` folder, so new vendored trees need negation rules.
- Elasticsearch index mappings and ILM are finalized in the `elasticsearch-init` templates in `docker-compose.yml`. Grafana queries the `logs`/`traces` **aliases**, not `logs-*`/`traces-*` index patterns, so ILM can rename indices freely. Strings map to plain keywords via a `strings_as_keywords` dynamic template — dashboard queries use bare field names, not `.keyword` sub-fields. Mapping changes apply only to newly created indices and there is no reindex path: adopting one means wiping the ES volume. Upgrading an existing deployment *without* wiping leaves aggregation panels erroring (`Fielddata is disabled` on the old `text` fields) for the whole retention window, because the alias spans both generations.
- **OTLP/JSON encodes `trace_id`/`span_id` as hex strings, but Protobuf JSON decodes `bytes` as base64.** Google.Protobuf's `JsonParser` follows the Protobuf rule, so IDs arrive mangled — silently, because hex is valid base64 and the lengths are multiples of 4. `src/OtlpProto/OtlpJsonIds.cs` repairs this; always route trace/span IDs through it rather than calling `Convert.ToHexString` on the raw bytes. Tests that build payloads with `JsonFormatter` will **not** catch a regression here, because that emits base64 — the guard test hand-writes collector-style hex JSON. Pipeline checks must assert on identifier values and lengths, not hit counts.

## Gotchas

- Histogram `le` and summary `quantile` labels must be formatted with the invariant culture; culture-sensitive `ToString` yields `"2,5"` on comma-decimal hosts and silently stops matching dashboards.
- Synthetic OTLP test payloads must use the attribute key `session.id`, not `session_id`; the parser promotes only OTel-convention names to top-level fields.
- `OtlpJsonParserTests` must stay in `LogMonitoring.sln`; without it, solution builds never compile the consumers.
- A file bind-mount inside a read-only directory mount can't create its own mountpoint, so the placeholder files under `docker/grafana/provisioning/` are required. Use `--force-recreate` after adding mounts.
- Grafana's table panel only shows `noValue` for a zero-column result, so the session-lookup procedure returns an explanatory row when a session is not found.
- Compose interpolates `$` inside unquoted `.env` values and silently mangles passwords.
