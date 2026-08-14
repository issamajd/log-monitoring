---
title: Verification runbook
---

# End-to-end verification runbook

Use this runbook to prove a deployment of the Milestone 1 stack actually works — every hop from OTLP ingestion through Kafka to Elasticsearch, TimescaleDB, and Grafana — **without needing the real application**. Each step gives the command to run and what success looks like. Work through the stages in order; each stage assumes the previous one passed.

Commands are written for **PowerShell on Windows** (`curl.exe`, `docker compose`). Commands wrapped in `docker exec` run *inside* the Linux containers and are shell-agnostic.

## Prerequisites

- **Docker Desktop** with Compose v2. The consumers build inside Docker (.NET 8 SDK image), so a host .NET SDK is only needed for tests or debugging a consumer outside Docker.
- Copy `.env.example` → `.env` (gitignored). The defaults work without it; you only need real values for session enrichment and alert notification targets.
- These host ports must be free:

| Port(s) | Service |
|---|---|
| 9092 / 9093 | Kafka (in-network / host listener) |
| 8080 | Kafka UI |
| 5432 | TimescaleDB |
| 9200 / 9300 | Elasticsearch |
| 4317 / 4318 | OTLP gRPC / HTTP (collector) |
| 8888 | Collector self-metrics |
| 13133 | Collector health endpoint |
| 56679 | Collector zPages (host port deliberately moved off the Windows NAT reserved range 55634–55933) |
| 9090 | Prometheus |
| 3000 | Grafana |
| 5601 | Kibana |

::: info Consumers are not in the solution file
`LogMonitoring.sln` holds only Instrumentation, SampleApp, SystemTraceParser, OtlpProto, and tests — the consumers are compiled transitively as project references of `OtlpJsonParserTests`, so a solution build does catch breakage in them. Build and run the consumers via Compose, or individually with `dotnet run --project src/Consumers/LogsConsumer` — but read [gotcha: Kafka from the host is port 9093](#kafka-from-the-host-is-port-9093-not-9092) first.
:::

## Stage 1 — bring the stack up

```powershell
docker compose up -d --build
docker compose ps --all
```

**Success looks like:** `kafka`, `timescaledb`, `elasticsearch`, `grafana`, and `otel-collector` report **healthy** — but only the first four healthchecks prove liveness; the collector's only runs `otelcol-contrib validate --config` (config validity — real liveness is checked in Stage 2). `kafka-init` and `elasticsearch-init` are one-shot containers that show **`Exited (0)` — that is normal, not a failure**. The `--all` flag matters: plain `docker compose ps` hides exited containers, so you cannot see the init exit codes without it. `up -d` returns while the init containers are still running (`elasticsearch-init` sleeps 10 s before it even starts), so if either still shows `Up`, wait and re-run `docker compose ps --all` until both have exited before continuing — Stage 2's topics and index templates do not exist until then. A non-zero exit means init failed: read `docker logs elasticsearch-init` / `docker logs kafka-init`. The consumers, Prometheus, Kafka UI, and Kibana have no healthcheck.

## Stage 2 — per-service health

**Kafka topics** (created by `kafka-init`):

```powershell
docker exec kafka kafka-topics --describe --bootstrap-server localhost:9092
```

Expect `telemetry-logs`, `telemetry-metrics`, `telemetry-traces` with `PartitionCount: 12` each, plus `alerts` and `dlq` with `PartitionCount: 3` — `--describe` (not `--list`) is what proves the partition counts, which `--list` cannot show.

**Elasticsearch** cluster and init artifacts:

```powershell
curl.exe -s http://localhost:9200/_cluster/health
curl.exe -s "http://localhost:9200/_cat/indices/logs,traces?v"
curl.exe -s "http://localhost:9200/_cat/aliases/logs,traces?v"
curl.exe -s http://localhost:9200/_ilm/policy/logs-policy
```

Expect cluster status `green` or `yellow` (yellow is normal on a single node), and the ILM policy JSON. On a fresh volume `_cat/indices` shows `logs-000001` and `traces-000001`; a long-running volume also has later generations from ILM rollover. The `_cat/aliases` output is the check that actually matters for writes: **exactly one** index per alias must show `is_write_index: true` — that is where the consumers write and what ILM rolls over.

**TimescaleDB** schema from `docker/timescaledb/init.sql`:

```powershell
docker exec timescaledb psql -U monitoring -d metrics -c "\dt metrics.*"
docker exec timescaledb psql -U monitoring -d metrics -c "\dv metrics.*"
```

Expect exactly one table, the `metrics.raw_metrics` hypertable — the continuous aggregates (`metrics_1min` … `metrics_1day`) are **views** (their backing tables live in `_timescaledb_internal`), so they show up under `\dv`, not `\dt`.

**Collector** — real liveness, then self-metrics:

```powershell
curl.exe -s http://localhost:13133/health
curl.exe -s http://localhost:8888/metrics | Select-Object -First 10
```

Expect a JSON status from the health extension and Prometheus-format text from `:8888`.

**Prometheus** scraping the collector (job `otel-collector`, target `otel-collector:8888`):

```powershell
curl.exe -s "http://localhost:9090/api/v1/targets" | Select-String '"health":"up"'
```

**Grafana**:

```powershell
curl.exe -s http://localhost:3000/api/health
```

Expect `"database": "ok"`. Login is `admin` / `admin123` unless `GRAFANA_USER` / `GRAFANA_PASSWORD` are set in `.env`.

**Consumers** alive:

```powershell
docker logs logs-consumer --tail 20
docker logs traces-consumer --tail 20
docker logs metrics-consumer --tail 20
```

Expect startup/subscription logging and no crash loops.

## Stage 3 — inject synthetic OTLP telemetry

The collector accepts OTLP over **gRPC on 4317** and **HTTP on 4318**. For hand-testing use HTTP: `POST http://localhost:4318/v1/logs` (also `/v1/traces`, `/v1/metrics`) with `Content-Type: application/json` and a standard OTLP/JSON body.

```powershell
$now = "$([DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds())000000"
$body = @"
{
  "resourceLogs": [{
    "resource": { "attributes": [
      { "key": "service.name", "value": { "stringValue": "verify-runbook" } },
      { "key": "host.name",    "value": { "stringValue": "verify-host" } }
    ]},
    "scopeLogs": [{ "logRecords": [{
      "timeUnixNano": "$now",
      "observedTimeUnixNano": "$now",
      "severityNumber": 17,
      "body": { "stringValue": "synthetic verification error event" },
      "attributes": [
        { "key": "session.id", "value": { "stringValue": "11111111-2222-3333-4444-555555555555" } },
        { "key": "user.id",    "value": { "stringValue": "verify-user" } }
      ]
    }]}]
  }]
}
"@
Invoke-RestMethod -Method Post -Uri http://localhost:4318/v1/logs -ContentType 'application/json' -Body $body
```

**Success looks like:** an empty `{}` response (HTTP 200) — the collector accepted the payload.

Post a synthetic **span** the same way (`traceId` must be 32 hex chars, `spanId` 16):

```powershell
$end = "$([DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds())000000"
$traceBody = @"
{
  "resourceSpans": [{
    "resource": { "attributes": [
      { "key": "service.name", "value": { "stringValue": "verify-runbook" } },
      { "key": "host.name",    "value": { "stringValue": "verify-host" } }
    ]},
    "scopeSpans": [{ "spans": [{
      "traceId": "0123456789abcdef0123456789abcdef",
      "spanId": "0123456789abcdef",
      "name": "verify-span",
      "kind": 2,
      "startTimeUnixNano": "$now",
      "endTimeUnixNano": "$end",
      "attributes": [
        { "key": "session.id", "value": { "stringValue": "11111111-2222-3333-4444-555555555555" } }
      ]
    }]}]
  }]
}
"@
Invoke-RestMethod -Method Post -Uri http://localhost:4318/v1/traces -ContentType 'application/json' -Body $traceBody
```

And a synthetic **gauge**:

```powershell
$metricBody = @"
{
  "resourceMetrics": [{
    "resource": { "attributes": [
      { "key": "service.name", "value": { "stringValue": "verify-runbook" } },
      { "key": "host.name",    "value": { "stringValue": "verify-host" } }
    ]},
    "scopeMetrics": [{ "metrics": [{
      "name": "verify.gauge",
      "gauge": { "dataPoints": [{ "timeUnixNano": "$now", "asDouble": 42 }] }
    }]}]
  }]
}
"@
Invoke-RestMethod -Method Post -Uri http://localhost:4318/v1/metrics -ContentType 'application/json' -Body $metricBody
```

Each returns `{}` on success. All three signals are now in flight, so every hop in Stage 4 has something to find.

::: warning Attribute key spelling is load-bearing
`OtlpLogsParser` promotes only OTel-convention keys to top-level Elasticsearch fields: `session.id` (or PascalCase `SessionId`) → `session_id`, and likewise `user.id`, `request.id`, `correlation.id`, and `exception.type/message/stacktrace`. A synthetic attribute keyed `session_id` (underscore) lands only inside the flattened `attributes` object and is **invisible to dashboard filters** on the top-level `session_id` field.
:::

::: warning Don't say "health check" in your test body
The collector's `filter/logs` processor drops log records whose **body** matches `health.*check`, `readiness.*probe`, or `liveness.*probe`. A synthetic log with "health check" in the body silently disappears.
:::

## Stage 4 — verify each hop

**Hop 1 — Kafka.** The collector published OTLP JSON to the per-signal topic:

```powershell
docker exec kafka kafka-console-consumer --bootstrap-server localhost:9092 --topic telemetry-logs --from-beginning --max-messages 1 --timeout-ms 15000
```

Expect one OTLP JSON message. Repeat with `telemetry-traces` / `telemetry-metrics`, or browse Kafka UI at `http://localhost:8080`.

**Hop 2 — Elasticsearch.** The logs consumer indexed into the `logs` alias, and the `session.id` attribute was promoted to a top-level field:

```powershell
curl.exe -s "http://localhost:9200/logs/_search?q=service_name:verify-runbook&pretty"
curl.exe -s "http://localhost:9200/logs/_search?q=session_id:11111111-2222-3333-4444-555555555555&pretty"
curl.exe -s "http://localhost:9200/traces/_search?q=service_name:verify-runbook&pretty"
```

Expect `hits.total.value` ≥ 1 on each. Allow a few seconds for the consumer batch and index refresh.

**Hop 3 — TimescaleDB.** The metrics consumer wrote to `metrics.raw_metrics`:

```powershell
docker exec timescaledb psql -U monitoring -d metrics -c "SELECT time, metric_name, value, service_name FROM metrics.raw_metrics ORDER BY time DESC LIMIT 10;"
```

Expect rows including your Stage 3 `verify.gauge` with `service_name` `verify-runbook`.

**Hop 4 — Prometheus.** Collector self-telemetry is queryable:

```powershell
curl.exe -s "http://localhost:9090/api/v1/query?query=otelcol_exporter_sent_log_records"
```

Expect a non-empty `result` array. `otelcol_exporter_sent_log_records`, `otelcol_exporter_sent_metric_points`, `otelcol_exporter_sent_spans`, `otelcol_exporter_send_failed_*`, and `otelcol_process_uptime` are the names the pipeline dashboard and the `collector-export-failures` alert query. If the query comes back empty, check the names actually exposed by your collector build with `curl.exe -s http://localhost:8888/metrics`.

## Stage 5 — dashboards

Open Grafana at `http://localhost:3000`, folder **Log Monitoring**. Five dashboards are file-provisioned from `docker/grafana/provisioning/dashboards/json`:

- Triage & Error Search
- Slow Operations
- SQL Insight & Risky Changes
- Host & Windows Service Health
- Pipeline & Stack Health

(plus **Session Lookup (App DB)** when enrichment is enabled — see Stage 7).

**Success looks like:** the synthetic `verify-runbook` events from Stage 3 appear in Triage & Error Search within the selected time range, and Pipeline & Stack Health shows the collector exporting. Check under *Connections → Data sources* that all four provisioned datasources exist (uids `timescaledb`, `es-logs` on the `logs` alias, `es-traces` on the `traces` alias, `prometheus-collector`), and under *Alerting → Alert rules* that the file-provisioned rules from `docker/grafana/provisioning/alerting/rules.yaml` loaded.

## Stage 6 — dead-letter queue

Any Kafka message the **logs or traces** consumer cannot read — anything that is not valid OTLP JSON — is published to the `dlq` topic as a `parse_failure` record with the raw payload base64-encoded. Per-document Elasticsearch rejections also route there, after transient 429/503 rejections are retried in place. The **metrics consumer has no DLQ producer** — it logs `Failed to parse metrics message, skipping...` and drops the message, so do not run this test against `telemetry-metrics`.

```powershell
$marker = "this-is-not-otlp-$(Get-Random)"
$markerB64 = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($marker))
$marker | docker exec -i kafka kafka-console-producer --bootstrap-server localhost:9092 --topic telemetry-logs
docker exec kafka kafka-console-consumer --bootstrap-server localhost:9092 --topic dlq --from-beginning --timeout-ms 15000 | Select-String $markerB64
docker logs logs-consumer --tail 10
```

The random marker matters on a long-running volume: `--from-beginning` replays **every** retained DLQ record (including old Elasticsearch rejections), so filtering for your marker's base64 is what proves *this* injection arrived rather than matching some earlier record.

**Success looks like:** `Select-String` prints one JSON record with `"error_type": "parse_failure"` and your marker's base64 under `"raw_payload_base64"`, and the consumer log shows `Failed to parse logs message, skipping...`. The pipeline keeps processing — the bad message is skipped, not fatal.

## Stage 7 — session enrichment (optional, needs a reachable SQL Server)

The full procedure is in `deploy/session-enrichment/README.md`: the DBA runs `00-preflight-check.sql`, then `02-create-lookup-proc.sql`, then `01-create-readonly-login.sql`. The preflight discovers the application's real schema — expect it **not** to be `dbo` (for example, `master` on the rnds4assets DEV database).

Operator side, set in `.env`: `SESSION_ENRICHMENT=enabled`, the four `SESSION_ENRICHMENT_MSSQL_*` values, and `GF_SECURITY_SECRET_KEY`. Then:

```powershell
docker compose up -d --force-recreate grafana
docker compose exec grafana cat /etc/grafana/provisioning/datasources/session-enrichment.yaml
```

**Success looks like:** the file shows the `mssql` datasource block (uid `mssql-sessions`), *not* the disabled `deleteDatasources` stub. Then copy a real `session_id` from Triage & Error Search, open its **Session context (app DB)** link, and confirm the Session Lookup dashboard resolves it.

To disable: set `SESSION_ENRICHMENT=disabled` and `docker compose up -d --force-recreate grafana` (this actively deletes the datasource), blank the MSSQL variables, and have the DBA revoke the SQL login. The `--force-recreate` is required whenever the enrichment flag flips on a running stack — the bind mounts change and plain `up -d` does not remount them.

## Resetting the stores to adopt a mapping change

Elasticsearch applies an index template only when an index is **created**. Documents already indexed under an older template keep their old mapping forever — a field that used to be `text` will not answer a bare keyword aggregation no matter how the template changes. There is no in-place migration and no reindex step in this project: the sanctioned way to adopt a mapping change is to drop the data and start clean.

This is destructive and telemetry is reproducible, so it is the right trade for a pre-production stack. **Do not run this on a deployment whose telemetry is still needed.**

```powershell
docker compose down
docker volume rm log-monitoring_elasticsearch-data
docker compose up -d --build
```

Drop `log-monitoring_kafka-kraft-data`, `log-monitoring_timescaledb-data`, and `log-monitoring_otel-checkpoint` in the same way if you want the buffer, metrics, and collector checkpoint cleared too. Prefix the volume names with your Compose project name if it is not `log-monitoring` — confirm with `docker volume ls`.

To wipe only the telemetry while keeping the volume and the ILM policies, delete the indices directly and let `elasticsearch-init` rebuild them on the next `up`:

```powershell
curl.exe -X DELETE "http://localhost:9200/logs-*,traces-*"
docker compose up -d --force-recreate elasticsearch-init
```

**Success looks like:** `_cat/indices` shows only `logs-000001` and `traces-000001`, and `_cat/aliases` shows exactly one `is_write_index: true` per alias. Re-run Stage 3 to repopulate.

## Gotchas

### `$` in unquoted `.env` values

Compose interpolates `$VAR` inside unquoted values and silently mangles passwords. Single-quote the value, escape as `$$`, or generate `$`-free secrets (documented in `.env.example` and the enrichment README).

### Enrichment placeholder files must exist

The two enrichment mount targets — `docker/grafana/provisioning/datasources/session-enrichment.yaml` and `docker/grafana/provisioning/dashboards/json/session-lookup.json` — must exist as committed placeholder files: a file bind-mount inside the read-only provisioning mount can only cover an existing file, never create one. Deleting them breaks Grafana startup in both modes. When flipping enrichment on a running stack, use `docker compose up -d --force-recreate grafana` so the changed mount takes effect.

### `host.name` missing from rndfwcore telemetry

The app does not yet emit a `host.name` resource attribute, so its documents have no `host_name` field. Dashboards tolerate that with the Lucene filter `(host_name:$host OR (NOT _exists_:host_name))` — the inner parentheses are load-bearing; `a OR NOT b` without them matches nothing. The proper fix is app-side, on the `log-monitoring` branch.

### Kafka from the host is port 9093, not 9092

`PLAINTEXT://kafka:9092` is advertised as `kafka:9092` and only resolves inside the Docker network; host clients must bootstrap `localhost:9093`. The consumer `appsettings.json` defaults (`localhost:9092`) therefore do **not** work for host-run debugging — override in PowerShell with `$env:Kafka__BootstrapServers = 'localhost:9093'` before `dotnet run --project src/Consumers/LogsConsumer`. In-container commands (`docker exec kafka ...`) use `localhost:9092` correctly.

### Collector healthcheck is not liveness

The compose healthcheck only runs `otelcol-contrib validate --config`. Real liveness is `curl.exe http://localhost:13133/health`.

### Init containers are idempotent by design

`elasticsearch-init` bootstraps `logs-000001` / `traces-000001` only when the `logs` / `traces` alias is absent, so re-runs against an existing volume skip the PUT instead of firing it and swallowing the error. This matters after the first ILM rollover: the write alias then points at a later generation and `-000001` may have been deleted outright, so blindly re-creating it would claim `is_write_index` for a second index. Every call in the script uses `--fail`, so a genuine init error is loud rather than silently tolerated. `kafka-init` creates topics with `--if-not-exists` for the same reason.

There is no schema-migration path for existing indices: mapping changes apply to newly created indices only. To adopt a mapping change, wipe and start clean per [resetting the stores](#resetting-the-stores-to-adopt-a-mapping-change).

### Telegraf is not part of the Docker stack

Host metrics come from a native Windows Telegraf install per `deploy/telegraf-windows/README.md` — run `telegraf.exe --test` first (perf-counter names are locale-sensitive). It outputs OTLP to `localhost:4317`.

### Alert contact points are placeholders

Notifications go nowhere until `ALERT_EMAIL_TO` / `ALERT_WEBHOOK_URL` are set; they are consumed via `$__env{}` in `docker/grafana/provisioning/alerting/contact-points.yaml`. Note the enrichment datasource file uses plain `$VAR` interpolation instead — the two syntaxes are not interchangeable.

## What cannot be verified locally

- **`win_*` host metric names** — Telegraf has never run against this stack. Confirm the actual metric names on the first real Windows host install.
- **`sql_danger_flags` and job-span document shapes** — the app has not emitted these yet; the dashboard panels that reference them carry their own metric-name caveats in their descriptions.
- **Exact `otelcol_*` metric spellings at runtime** — the names in Stage 4 are the ones referenced by the pipeline dashboard and alerts for collector-contrib 0.91.0; confirm against `curl.exe -s http://localhost:8888/metrics` on your running collector.
- **Synthetic `/v1/metrics` gauge names vs. dashboard expectations** — dashboard panels state which `metric_name` values they query; a synthetic gauge proves the pipeline hop, not the panel query.
