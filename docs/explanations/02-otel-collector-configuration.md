# OpenTelemetry Collector Configuration Explained

## Overview

**OpenTelemetry Collector** is the central aggregation and routing layer for telemetry data.

**File**: `docker/otel-collector/otel-collector-config.yaml`

**What it does**:
- Receives telemetry from .NET applications (OTLP protocol)
- Scrapes metrics from Telegraf (Prometheus format)
- Processes and enriches telemetry
- Exports everything to Kafka for persistence

---

## Architecture Position

```
.NET Apps (OTLP) ──┐
                   ├──→ OTel Collector ──→ Kafka ──→ Consumers ──→ Storage
Telegraf (Prom) ───┘
```

---

## Configuration Structure

```yaml
receivers:      # How to receive telemetry (input)
processors:     # How to transform telemetry (middleware)
exporters:      # Where to send telemetry (output)
extensions:     # Additional features (health checks, debugging)
service:        # Wire everything together (pipelines)
```

---

## 1. Receivers (Lines 4-39)

### OTLP Receiver (Lines 6-11)

```yaml
otlp:
  protocols:
    grpc:
      endpoint: 0.0.0.0:4317
    http:
      endpoint: 0.0.0.0:4318
```

**Purpose**: Receive telemetry from instrumented applications using OpenTelemetry Protocol.

**What is OTLP?**
- **O**pen**T**elemetry **P**rotocol
- Standard protocol for sending traces, metrics, and logs
- Binary format (efficient)
- Supports both gRPC (port 4317) and HTTP (port 4318)

**From .NET Application**:
```csharp
// In your .NET app
services.AddOpenTelemetry()
    .WithTracing(builder => builder
        .AddOtlpExporter(options => {
            options.Endpoint = new Uri("http://otel-collector:4317");
            options.Protocol = OtlpExportProtocol.Grpc;
        })
    )
    .WithMetrics(builder => builder
        .AddOtlpExporter(options => {
            options.Endpoint = new Uri("http://otel-collector:4317");
        })
    );
```

**Ports**:
- **4317**: gRPC (binary, faster, preferred)
- **4318**: HTTP (REST-like, easier debugging)

**Binding to 0.0.0.0**: Accept connections from any network interface.

---

### Prometheus Receiver (Lines 14-24)

```yaml
prometheus:
  config:
    scrape_configs:
      - job_name: 'telegraf'
        scrape_interval: 10s
        static_configs:
          - targets: ['telegraf:9273']
        metric_relabel_configs:
          - source_labels: [__name__]
            regex: 'go_.*'
            action: drop
```

**Purpose**: Scrape metrics from Prometheus-compatible endpoints (Telegraf).

**How it works**:
1. Every 10 seconds, OTel Collector makes HTTP GET to `http://telegraf:9273/metrics`
2. Telegraf returns metrics in Prometheus text format
3. OTel Collector parses and converts to OTLP format

**Why scrape Telegraf?**
- Telegraf doesn't speak OTLP natively
- Telegraf exposes Prometheus format
- OTel Collector can consume Prometheus format

**metric_relabel_configs**:
```yaml
- source_labels: [__name__]
  regex: 'go_.*'
  action: drop
```
**Purpose**: Drop metrics starting with `go_` (Go runtime metrics from Telegraf itself).

**Why drop?**: Reduces noise, we only care about system metrics, not Telegraf's own metrics.

---

### HostMetrics Receiver (Lines 27-39)

```yaml
hostmetrics:
  collection_interval: 30s
  scrapers:
    cpu:
      metrics:
        system.cpu.utilization:
          enabled: true
    memory:
      metrics:
        system.memory.utilization:
          enabled: true
    disk:
    network:
```

**Purpose**: Collect metrics from the host machine where OTel Collector is running.

**What it collects**:
- **CPU utilization**: Percentage CPU usage
- **Memory utilization**: Percentage memory usage
- **Disk**: Disk I/O stats
- **Network**: Network I/O stats

**Why have this?**: Monitors the OTel Collector's own host (for self-monitoring).

**Difference from Telegraf**:
- **Telegraf**: Monitors application servers
- **HostMetrics**: Monitors OTel Collector's server

---

## 2. Processors (Lines 41-91)

### Batch Processor (Lines 43-46)

```yaml
batch:
  timeout: 1s
  send_batch_size: 1024
  send_batch_max_size: 2048
```

**Purpose**: Group multiple telemetry items into batches before exporting (reduces overhead).

**Settings**:
- **timeout = 1s**: Send batch after 1 second, even if not full
- **send_batch_size = 1024**: Ideal batch size (send when reaching 1024 items)
- **send_batch_max_size = 2048**: Hard limit (split into multiple batches if exceeded)

**Example**:
```
Without batching:
Item 1 → Kafka
Item 2 → Kafka
Item 3 → Kafka
... (1000 network calls)

With batching:
Items 1-1024 → Kafka (1 network call)
Items 1025-2048 → Kafka (1 network call)
```

**Benefits**:
- ✅ Reduces network overhead (fewer connections)
- ✅ Better throughput (bulk writes)
- ✅ Lower CPU usage

---

### Resource Processor (Lines 49-56)

```yaml
resource:
  attributes:
    - key: deployment.environment
      value: ${DEPLOYMENT_ENV:-development}
      action: upsert
    - key: service.namespace
      value: ${SERVICE_NAMESPACE:-log-monitoring}
      action: upsert
```

**Purpose**: Add/modify resource attributes (metadata attached to all telemetry).

**What is a resource attribute?**
- Describes **where** the telemetry came from
- Examples: environment, region, cluster, namespace

**action: upsert**:
- **Insert** if not present
- **Update** if already present

**Environment variables**:
```yaml
${DEPLOYMENT_ENV:-development}
```
**Meaning**: Use `DEPLOYMENT_ENV` from environment, or `development` as default.

**Usage in docker-compose**:
```yaml
environment:
  DEPLOYMENT_ENV: production
  SERVICE_NAMESPACE: customer-xyz
```

**Result**: All telemetry gets tagged with:
```json
{
  "resource": {
    "deployment.environment": "production",
    "service.namespace": "customer-xyz"
  }
}
```

**Why useful**: Query/filter by environment in Grafana.

---

### Memory Limiter (Lines 59-62)

```yaml
memory_limiter:
  check_interval: 1s
  limit_mib: 512
  spike_limit_mib: 128
```

**Purpose**: Prevent OTel Collector from using too much memory (OOM protection).

**Settings**:
- **check_interval = 1s**: Check memory usage every second
- **limit_mib = 512**: Soft limit (512 MB)
- **spike_limit_mib = 128**: Extra allowance for sudden spikes (512 + 128 = 640 MB max)

**What happens when limit exceeded?**
1. Stop accepting new data
2. Apply backpressure (slow down receivers)
3. Log warning
4. Start dropping data if backpressure fails

**Why 512 MB?**
- Assumes OTel Collector has ~1 GB RAM available
- Leaves headroom for other processes

**Tuning for high throughput**:
```yaml
memory_limiter:
  limit_mib: 2048  # For 4 GB RAM systems
  spike_limit_mib: 512
```

---

### Filter Processor (Logs) (Lines 65-72)

```yaml
filter/logs:
  logs:
    exclude:
      match_type: regexp
      bodies:
        - "health.*check"
        - "readiness.*probe"
        - "liveness.*probe"
```

**Purpose**: Drop noisy logs that aren't useful.

**How it works**:
1. Check log body (message content)
2. If matches regex pattern → Drop the log
3. Otherwise → Keep the log

**Patterns**:
- `health.*check`: Matches "health check", "health_check", etc.
- `readiness.*probe`: Kubernetes readiness probes
- `liveness.*probe`: Kubernetes liveness probes

**Example**:
```
Log: "GET /health - 200 OK"  → DROPPED ✅
Log: "GET /api/users - 500 Error" → KEPT ✅
```

**Why drop health checks?**
- Generated every few seconds
- Not useful for debugging
- Waste storage space

---

### Attributes Processor (Lines 75-91)

```yaml
attributes/logs:
  actions:
    - key: log.pipeline.source
      value: otel-collector
      action: upsert

attributes/metrics:
  actions:
    - key: metrics.pipeline.source
      value: otel-collector
      action: upsert

attributes/traces:
  actions:
    - key: traces.pipeline.source
      value: otel-collector
      action: upsert
```

**Purpose**: Add a tag indicating data passed through OTel Collector.

**Why useful?**
- Troubleshooting (did data go through OTel Collector or direct?)
- Debugging pipeline issues
- Auditing data flow

**Example metric**:
```json
{
  "metric_name": "cpu.usage",
  "value": 75.5,
  "attributes": {
    "metrics.pipeline.source": "otel-collector"
  }
}
```

---

## 3. Exporters (Lines 93-137)

### Kafka Exporters (Lines 95-125)

#### Metrics to Kafka (Lines 95-103)

```yaml
kafka/metrics:
  brokers:
    - kafka:9092
  topic: telemetry-metrics
  encoding: otlp_proto
  producer:
    max_message_bytes: 1000000
    required_acks: 1
    compression: snappy
```

**Settings**:
- **brokers**: Kafka server address
- **topic**: Kafka topic name
- **encoding = otlp_proto**: Binary OTLP format (efficient)
- **max_message_bytes = 1 MB**: Max message size
- **required_acks = 1**: Wait for leader acknowledgment (not all replicas)
- **compression = snappy**: Fast compression algorithm

**Why snappy compression?**
- Fast (low CPU overhead)
- Good compression ratio (2-3x)
- Better than gzip for real-time data

---

#### Logs to Kafka (Lines 106-114)

```yaml
kafka/logs:
  brokers:
    - kafka:9092
  topic: telemetry-logs
  encoding: otlp_proto
  producer:
    max_message_bytes: 1000000
    required_acks: 1
    compression: snappy
```

Same settings as metrics, different topic.

---

#### Traces to Kafka (Lines 117-125)

```yaml
kafka/traces:
  brokers:
    - kafka:9092
  topic: telemetry-traces
  encoding: otlp_proto
  producer:
    max_message_bytes: 1000000
    required_acks: 1
    compression: snappy
```

Same settings, separate topic for traces.

---

### Debug Exporter (Lines 128-131)

```yaml
debug:
  verbosity: basic
  sampling_initial: 5
  sampling_thereafter: 200
```

**Purpose**: Log telemetry to console for debugging.

**Settings**:
- **verbosity = basic**: Show summary, not full content
- **sampling_initial = 5**: Log first 5 items fully
- **sampling_thereafter = 200**: Then log every 200th item

**Example output**:
```
2026-01-13T10:30:00Z info Metrics Exporter {"#metrics": 1024}
2026-01-13T10:30:10Z info Logs Exporter {"#logs": 256}
```

**⚠️ Production**: Set `verbosity: none` or remove this exporter (performance).

---

### Prometheus Exporter (Lines 134-136)

```yaml
prometheus:
  endpoint: 0.0.0.0:8889
  namespace: otelcol
```

**Purpose**: Expose OTel Collector's **own metrics** (self-monitoring).

**Access**: `http://otel-collector:8889/metrics`

**Metrics exposed**:
- `otelcol_receiver_accepted_spans`: Spans received
- `otelcol_receiver_refused_spans`: Spans rejected
- `otelcol_exporter_sent_spans`: Spans exported
- `otelcol_processor_batch_batch_send_size`: Batch sizes
- `otelcol_processor_memory_limiter_mem_usage`: Memory usage

**Why useful**: Monitor OTel Collector's health and performance.

---

## 4. Extensions (Lines 138-150)

### Health Check (Lines 140-142)

```yaml
health_check:
  endpoint: 0.0.0.0:13133
  path: /health
```

**Purpose**: HTTP endpoint to check if OTel Collector is healthy.

**Access**: `http://otel-collector:13133/health`

**Response**:
```json
{"status": "Server available"}
```

**Use in docker-compose**:
```yaml
healthcheck:
  test: ["CMD", "wget", "--spider", "-q", "http://localhost:13133/health"]
```

---

### pprof (Lines 145-146)

```yaml
pprof:
  endpoint: 0.0.0.0:1777
```

**Purpose**: Go profiling endpoint (CPU, memory profiling).

**Access**: `http://otel-collector:1777/debug/pprof/`

**Use cases**:
- Debug memory leaks
- Profile CPU usage
- Analyze goroutine stacks

**⚠️ Production**: Disable or restrict access (security risk).

---

### zPages (Lines 149-150)

```yaml
zpages:
  endpoint: 0.0.0.0:55679
```

**Purpose**: Web UI for debugging OTel Collector internals.

**Access**: `http://otel-collector:55679/debug/tracez`

**Pages available**:
- `/debug/tracez`: View traces in memory
- `/debug/pipelinez`: View pipeline stats
- `/debug/servicez`: View extensions/services
- `/debug/extensionz`: View extensions status

**⚠️ Production**: Disable (exposes internal data).

---

## 5. Service (Pipelines) (Lines 152-181)

### Service Structure

```yaml
service:
  extensions: [health_check, pprof, zpages]

  pipelines:
    metrics:
      receivers: [...]
      processors: [...]
      exporters: [...]

    logs:
      receivers: [...]
      processors: [...]
      exporters: [...]

    traces:
      receivers: [...]
      processors: [...]
      exporters: [...]
```

**Purpose**: Wire receivers → processors → exporters into pipelines.

---

### Metrics Pipeline (Lines 157-160)

```yaml
metrics:
  receivers: [otlp, prometheus, hostmetrics]
  processors: [memory_limiter, batch, resource, attributes/metrics]
  exporters: [kafka/metrics, debug]
```

**Data flow**:
```
OTLP (from .NET apps) ──┐
Prometheus (Telegraf) ──┼──→ Memory Limiter
HostMetrics ────────────┘          ↓
                              Batch Processor
                                   ↓
                            Resource Processor (add env tags)
                                   ↓
                            Attributes Processor (add pipeline tag)
                                   ↓
                        ┌──────────┴─────────┐
                        ↓                    ↓
                    Kafka Topic      Debug Logger
                 (telemetry-metrics)
```

---

### Logs Pipeline (Lines 163-166)

```yaml
logs:
  receivers: [otlp]
  processors: [memory_limiter, filter/logs, batch, resource, attributes/logs]
  exporters: [kafka/logs, debug]
```

**Data flow**:
```
OTLP (from .NET apps) ──→ Memory Limiter
                               ↓
                         Filter (drop health checks)
                               ↓
                         Batch Processor
                               ↓
                         Resource Processor (add env tags)
                               ↓
                         Attributes Processor (add pipeline tag)
                               ↓
                        ┌──────┴─────┐
                        ↓            ↓
                    Kafka Topic   Debug Logger
                  (telemetry-logs)
```

---

### Traces Pipeline (Lines 169-172)

```yaml
traces:
  receivers: [otlp]
  processors: [memory_limiter, batch, resource, attributes/traces]
  exporters: [kafka/traces, debug]
```

**Data flow**:
```
OTLP (from .NET apps) ──→ Memory Limiter
                               ↓
                         Batch Processor
                               ↓
                         Resource Processor (add env tags)
                               ↓
                         Attributes Processor (add pipeline tag)
                               ↓
                        ┌──────┴──────┐
                        ↓             ↓
                    Kafka Topic    Debug Logger
                 (telemetry-traces)
```

---

### Telemetry (Self-Monitoring) (Lines 174-180)

```yaml
telemetry:
  logs:
    level: info
    encoding: json
  metrics:
    level: detailed
    address: 0.0.0.0:8888
```

**Purpose**: Configure OTel Collector's own telemetry (logging and metrics about itself).

**Difference from Prometheus exporter**:
- **Prometheus exporter (port 8889)**: Exposes metrics in Prometheus format (for scraping)
- **Telemetry metrics (port 8888)**: Internal metrics endpoint (for OTel Collector's own monitoring)

**logs.level**:
- `none`: No logs
- `error`: Only errors
- `warn`: Warnings and errors
- `info`: General info (recommended)
- `debug`: Verbose (for troubleshooting)

**metrics.level**:
- `none`: No metrics
- `basic`: Minimal metrics
- `normal`: Standard metrics
- `detailed`: All metrics (recommended for monitoring)

**Access internal metrics**: `http://otel-collector:8888/metrics`

---

## Complete Data Flow Example

### Scenario: .NET App Sends a Log

**1. .NET Application**:
```csharp
_logger.LogError("Database connection failed");
```

**2. OpenTelemetry SDK** (in .NET app):
```
Converts to OTLP format:
{
  "timestamp": "2026-01-13T10:30:00Z",
  "severity": "ERROR",
  "body": "Database connection failed",
  "resource": {
    "service.name": "api-service",
    "service.version": "1.2.3"
  }
}

Sends via gRPC to http://otel-collector:4317
```

**3. OTel Collector Receives** (OTLP receiver):
```
Accepts on port 4317
Passes to logs pipeline
```

**4. Processors Run**:
```
Memory Limiter: Check memory usage → OK
Filter: "Database connection failed" matches "health.*check"? → NO (keep)
Batch: Add to batch (wait for 1024 logs or 1 second)
Resource: Add {"deployment.environment": "production"}
Attributes: Add {"log.pipeline.source": "otel-collector"}
```

**5. Exporters Run**:
```
Kafka Exporter:
  - Serialize to OTLP binary format
  - Compress with snappy
  - Send to topic: telemetry-logs
  - Wait for leader ACK

Debug Exporter:
  - Log: "Exported 1024 logs to Kafka"
```

**6. Kafka**:
```
Stores in telemetry-logs topic, partition 7 (hash-based)
```

**7. Consumer Service**:
```
Reads from Kafka
Parses OTLP format
Inserts into Elasticsearch
```

---

## Performance Tuning

### High Throughput (100k+ events/sec)

```yaml
batch:
  timeout: 500ms
  send_batch_size: 2048
  send_batch_max_size: 4096

memory_limiter:
  limit_mib: 2048
  spike_limit_mib: 512

kafka/metrics:
  producer:
    max_message_bytes: 5000000  # 5 MB
    compression: lz4  # Faster than snappy
```

### Low Latency (< 1 second end-to-end)

```yaml
batch:
  timeout: 100ms
  send_batch_size: 256

kafka/metrics:
  producer:
    required_acks: 0  # Don't wait for ACK (fire and forget)
    compression: none  # Skip compression overhead
```

---

## Troubleshooting

### Check OTel Collector Logs

```bash
docker logs otel-collector
```

### Test Configuration

```bash
docker exec otel-collector /otelcol --config=/etc/otel-collector-config.yaml --dry-run
```

### View Self-Monitoring Metrics

```bash
curl http://localhost:8889/metrics | grep otelcol_receiver
```

**Key metrics**:
```
otelcol_receiver_accepted_spans 12500
otelcol_receiver_refused_spans 0  # Should be 0!
otelcol_exporter_queue_size 256
otelcol_processor_batch_batch_send_size_sum 1024
```

### Health Check

```bash
curl http://localhost:13133/health
```

### View Traces in Memory

```
http://localhost:55679/debug/tracez
```

---

## Summary

| Component | Purpose | Ports |
|-----------|---------|-------|
| **OTLP Receiver** | Accept telemetry from apps | 4317 (gRPC), 4318 (HTTP) |
| **Prometheus Receiver** | Scrape Telegraf metrics | N/A (outbound to 9273) |
| **HostMetrics Receiver** | Self-monitoring | N/A |
| **Processors** | Transform/filter telemetry | N/A |
| **Kafka Exporters** | Send to Kafka | N/A (outbound to 9092) |
| **Health Check** | Container health | 13133 |
| **Prometheus Exporter** | Self-metrics | 8889 |
| **zPages** | Debug UI | 55679 |

**Key Features**:
- ✅ Multi-protocol support (OTLP, Prometheus)
- ✅ Batching for efficiency
- ✅ Memory limits (no OOM)
- ✅ Filter noisy logs
- ✅ Enrich with environment tags
- ✅ Export to Kafka (scalable)
- ✅ Self-monitoring built-in

**Next Steps**:
1. Tune batch sizes for your throughput
2. Adjust memory limits based on available RAM
3. Add custom processors if needed
4. Monitor self-metrics in Grafana
