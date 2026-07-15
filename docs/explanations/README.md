# Configuration Explanations

This directory contains detailed explanations of all configuration files and concepts in the log monitoring system.

> Current planning source of truth: the VitePress docs site under [`../../site/`](../../site/). These explanation files describe existing prototype components and implementation concepts; they do not override the current Milestone 1 plan.

---

## Files

### 1. [Telegraf Configuration](./01-telegraf-configuration.md)
**File**: `docker/telegraf/telegraf.conf`

Explains:
- What Telegraf is and how it collects system metrics
- Input plugins (CPU, memory, disk, network, Docker)
- Processor and aggregator plugins
- Prometheus output format
- Windows vs Linux configuration
- Performance tuning

**Key topics**:
- System metrics collection
- .NET CLR metrics (Windows)
- Docker container metrics
- Cross-platform compatibility

---

### 2. [OpenTelemetry Collector Configuration](./02-otel-collector-configuration.md)
**File**: `docker/otel-collector/otel-collector-config.yaml`

Explains:
- OTLP receiver (gRPC + HTTP)
- Prometheus receiver (scraping Telegraf)
- Processors (batch, resource, memory limiter, filters)
- Kafka exporters (metrics, logs, traces)
- Extensions (health check, pprof, zPages)
- Service pipelines (how everything connects)

**Key topics**:
- OTLP protocol vs Telegraf
- Batching and memory limits
- Filtering noisy logs
- Self-monitoring

---

### 3. [Kafka Setup](./03-kafka-setup.md)
**File**: `docker/kafka/docker-compose.kafka.yml`

Explains:
- ZooKeeper (cluster coordinator)
- Kafka broker configuration
- Listener configuration (PLAINTEXT vs PLAINTEXT_HOST)
- Replication and fault tolerance
- Data retention and compression
- Topic creation (telemetry-metrics, telemetry-logs, telemetry-traces)
- Kafka UI (web management)

**Key topics**:
- How listeners work (kafka:9092 vs localhost:9093)
- Why ZooKeeper is needed
- Retention policies (7 days)
- Performance tuning (snappy compression, 2 MB messages)

---

### 4. [Kafka Partitions Deep Dive](./04-kafka-partitions-deep-dive.md)

Explains:
- What partitions are physically (directories on disk)
- How messages are distributed (key-based vs round-robin)
- Parallelism (up to 12 consumers for 12 partitions)
- Consumer groups and partition assignment
- Ordering guarantees (within partition vs across partitions)
- Rebalancing when consumers join/leave
- Performance calculations (why 12 partitions?)

**Key topics**:
- Physical storage structure
- Hash-based partitioning with keys
- Consumer parallelism limits
- Multi-broker distribution
- Choosing partition count

---

### 5. [TimescaleDB Setup](./05-timescaledb-setup.md)
**File**: `docker/timescaledb/docker-compose.timescaledb.yml`

Explains:
- What TimescaleDB is (PostgreSQL + time-series)
- Environment variables (username, password, database)
- PostgreSQL tuning parameters:
  - Memory settings (shared_buffers, effective_cache_size)
  - Connection limits (max_connections)
  - WAL settings (wal_buffers, max_wal_size)
  - Parallel query settings
  - SSD optimizations (random_page_cost, effective_io_concurrency)
- Health checks
- Volume management

**Key topics**:
- Memory tuning for different system sizes
- SSD vs spinning disk configuration
- Checkpoint and WAL tuning
- Connection pooling
- Performance benchmarks

---

### 6. [TimescaleDB Schema](./06-timescaledb-schema.md)
**File**: `docker/timescaledb/init.sql`

Explains:
- Main metrics hypertable (time-partitioned table)
- Continuous aggregates (1-min, 5-min, 1-hour, 1-day)
- Refresh policies (automatic background jobs)
- Retention policies (7 days to 3 years)
- Compression (90% space savings)
- Anomaly detection tables (ML integration)
- Forecast tables (predictive monitoring)
- Helper functions (get_metric_stats, is_anomalous)
- Views for Grafana

**Key topics**:
- Hypertables and chunks
- Time-bucket aggregations
- Continuous aggregates vs regular views
- Compression segmentby strategy
- Retention ladder (raw → aggregates)
- JSONB labels for flexibility

---

### 7. [Elasticsearch Setup](./07-elasticsearch-setup.md)
**File**: `docker/elasticsearch/docker-compose.elasticsearch.yml`

Explains:
- What Elasticsearch is (distributed search and analytics)
- Elasticsearch service configuration
- JVM memory tuning (heap size)
- ILM (Index Lifecycle Management) policy:
  - Hot phase (active indices, daily rollover)
  - Warm phase (shrink shards, forcemerge)
  - Cold phase (low priority)
  - Delete phase (30-day retention)
- Index templates and mappings
- Field types (keyword, text, date, object)
- Multi-field mappings (text + keyword)
- Kibana web UI

**Key topics**:
- ILM lifecycle (hot → warm → cold → delete)
- Dynamic templates (strings as keywords)
- Index rollover (daily or 50 GB)
- Shard optimization (shrink and forcemerge)
- Transaction log durability (async)
- Security disabled (development mode)
- 30-day retention policy

---

## Architecture Overview

```
┌─────────────────────────────────────────────────────────────────────┐
│                           Data Sources                              │
├─────────────────────────────────────────────────────────────────────┤
│                                                                      │
│  .NET Apps (OTLP)          Telegraf (System Metrics)                │
│         │                            │                               │
│         │ gRPC/HTTP                  │ Prometheus format             │
│         │ :4317/:4318                │ :9273                         │
│         └────────────┬───────────────┘                               │
│                      │                                               │
│                      ▼                                               │
│           ┌──────────────────────┐                                  │
│           │ OpenTelemetry         │                                  │
│           │ Collector             │                                  │
│           │ (Receive, Process,    │                                  │
│           │  Filter, Batch)       │                                  │
│           └──────────┬───────────┘                                  │
│                      │                                               │
│                      │ OTLP Binary (Snappy compressed)               │
│                      │                                               │
│                      ▼                                               │
│           ┌──────────────────────┐                                  │
│           │ Kafka                 │                                  │
│           │ (Message Buffer)      │                                  │
│           │                       │                                  │
│           │ Topics:               │                                  │
│           │ - telemetry-metrics   │ (12 partitions)                 │
│           │ - telemetry-logs      │ (12 partitions)                 │
│           │ - telemetry-traces    │ (6 partitions)                  │
│           └──────────┬───────────┘                                  │
│                      │                                               │
│                      │                                               │
│         ┌────────────┼────────────┐                                 │
│         │            │            │                                  │
│         ▼            ▼            ▼                                  │
│   ┌─────────┐  ┌─────────┐  ┌─────────┐                           │
│   │ Metrics │  │  Logs   │  │ Traces  │                            │
│   │Consumer │  │Consumer │  │Consumer │                            │
│   └────┬────┘  └────┬────┘  └────┬────┘                           │
│        │            │            │                                  │
│        │            │            │                                  │
│        ▼            ▼            ▼                                  │
│  ┌──────────────────────────────────────┐                          │
│  │         Storage Layer                 │                          │
│  ├───────────────┬──────────────────────┤                          │
│  │ TimescaleDB   │ Elasticsearch        │                          │
│  │ (Metrics)     │ (Logs)               │                          │
│  │               │                      │                          │
│  │ - Hypertables │ - Index templates    │                          │
│  │ - Compression │ - ILM policies       │                          │
│  │ - Aggregates  │ - Search             │                          │
│  └───────────────┴──────────────────────┘                          │
│                      │                                               │
│                      ▼                                               │
│           ┌──────────────────────┐                                  │
│           │ Grafana               │                                  │
│           │ (Visualization)       │                                  │
│           └──────────────────────┘                                  │
└─────────────────────────────────────────────────────────────────────┘
```

---

## Quick Reference

### Port Reference

| Service | Port(s) | Purpose |
|---------|---------|---------|
| ZooKeeper | 2181 | Kafka coordination |
| Kafka | 9092 (internal), 9093 (external) | Message broker |
| Kafka UI | 8080 | Web management interface |
| OTel Collector | 4317 (gRPC), 4318 (HTTP) | OTLP receivers |
| OTel Collector | 8889 | Prometheus self-metrics |
| OTel Collector | 13133 | Health check |
| OTel Collector | 55679 | zPages debug UI |
| Telegraf | 9273 | Prometheus metrics export |
| TimescaleDB | 5432 | PostgreSQL database |
| Elasticsearch | 9200 | HTTP API (REST) |
| Elasticsearch | 9300 | Transport (inter-node) |
| Kibana | 5601 | Web UI |

---

### Data Flow Summary

1. **Collection**:
   - .NET apps → OTLP → OTel Collector (port 4317/4318)
   - Telegraf → Prometheus format → OTel Collector (scrape from 9273)

2. **Processing**:
   - OTel Collector: Filter, batch, enrich
   - Export to Kafka topics (compressed with snappy)

3. **Buffering**:
   - Kafka: 7-day retention, 12 partitions (metrics/logs), 6 partitions (traces)

4. **Storage**:
   - Metrics Consumer → TimescaleDB (hypertables, compression, aggregates)
   - Logs Consumer → Elasticsearch (indexes, ILM)

5. **Visualization**:
   - Grafana → Query TimescaleDB/Elasticsearch
   - Dashboards, alerts, ML insights

---

## Configuration Files Map

```
log-monitoring/
├── docker/
│   ├── telegraf/
│   │   └── telegraf.conf                    # → Explanation 01
│   │
│   ├── otel-collector/
│   │   └── otel-collector-config.yaml       # → Explanation 02
│   │
│   ├── kafka/
│   │   └── docker-compose.kafka.yml         # → Explanation 03
│   │                                          # → Explanation 04 (concepts)
│   │
│   ├── timescaledb/
│   │   ├── docker-compose.timescaledb.yml   # → Explanation 05
│   │   └── init.sql                          # → Explanation 06
│   │
│   └── elasticsearch/
│       └── docker-compose.elasticsearch.yml  # → Explanation 07
│
└── docs/
    └── explanations/
        ├── 01-telegraf-configuration.md
        ├── 02-otel-collector-configuration.md
        ├── 03-kafka-setup.md
        ├── 04-kafka-partitions-deep-dive.md
        ├── 05-timescaledb-setup.md
        ├── 06-timescaledb-schema.md
        ├── 07-elasticsearch-setup.md
        └── README.md (this file)
```

---

## Key Concepts

### 1. OTLP vs Telegraf

**OTLP (OpenTelemetry Protocol)**:
- Standard protocol for telemetry
- Application-level (traces, metrics, logs from code)
- Binary format (efficient)
- gRPC or HTTP

**Telegraf**:
- Agent for collecting system metrics
- OS-level (CPU, memory, disk, network)
- Prometheus output format
- Scrape-based

**Use both**: OTLP for app telemetry, Telegraf for system metrics.

---

### 2. Kafka Partitions

**Key insight**: Partitions enable parallel processing.

**Formula**:
```
Max parallel consumers = Number of partitions

12 partitions → Max 12 consumers (12x throughput)
```

**Message routing**:
- With key: `hash(key) % partitions`
- Without key: Round-robin

**Ordering**:
- ✅ Guaranteed within partition
- ❌ Not guaranteed across partitions

---

### 3. TimescaleDB Hypertables

**What**: Time-series partitioned tables (automatic chunks).

**Chunk size**: 1 hour (configurable).

**Benefits**:
- Fast inserts (parallel to multiple chunks)
- Fast deletes (drop entire chunk)
- Compression (90% space savings)
- Continuous aggregates (pre-computed rollups)

**Query**: Use like normal PostgreSQL table (transparent partitioning).

---

### 4. Continuous Aggregates

**Problem**: Aggregating millions of rows is slow.

**Solution**: Pre-compute aggregates automatically.

**Granularities**:
- 1-minute: For last hour (high detail)
- 5-minute: For last day (medium detail)
- 1-hour: For last week (daily views)
- 1-day: For long-term trends (monthly/yearly)

**Refresh**: Background jobs update aggregates automatically.

---

### 5. Retention Ladder

**Strategy**: Keep high-resolution data short-term, aggregates long-term.

**Ladder**:
```
Raw metrics:      7 days   (full resolution)
1-min aggregates: 30 days  (detailed)
5-min aggregates: 90 days  (medium)
1-hour aggregates: 1 year  (daily)
1-day aggregates:  3 years (trends)
```

**Storage savings**: 99.99% reduction with aggregates!

---

## Performance Targets

| Metric | Target | Component |
|--------|--------|-----------|
| Log ingestion | 50k logs/sec | Kafka (12 partitions) |
| Metric ingestion | 100k metrics/sec | Kafka (12 partitions) |
| End-to-end latency | < 5 seconds (p95) | Full pipeline |
| Query latency | < 500ms (simple), < 3s (complex) | TimescaleDB |
| Alert delivery | < 30 seconds | Alert manager |
| Dashboard load | < 3 seconds | Grafana |
| Compression ratio | 10:1 (90% savings) | TimescaleDB |

---

## Tuning Guidelines

### For Higher Throughput (100k+ events/sec)

**OTel Collector**:
```yaml
batch:
  send_batch_size: 2048
  timeout: 500ms

memory_limiter:
  limit_mib: 2048
```

**Kafka**:
```yaml
KAFKA_NUM_IO_THREADS: 16
KAFKA_NUM_NETWORK_THREADS: 6
```

**TimescaleDB**:
```yaml
shared_buffers=2GB
max_connections=300
```

---

### For Lower Latency (< 1s end-to-end)

**OTel Collector**:
```yaml
batch:
  send_batch_size: 256
  timeout: 100ms

kafka:
  producer:
    required_acks: 0  # Fire and forget
```

---

### For Memory-Constrained Systems

**OTel Collector**:
```yaml
memory_limiter:
  limit_mib: 256
  spike_limit_mib: 64
```

**TimescaleDB**:
```yaml
shared_buffers=256MB
effective_cache_size=768MB
max_connections=100
```

---

## Troubleshooting Guide

### Check Component Health

```bash
# Telegraf
curl http://localhost:9273/metrics

# OTel Collector
curl http://localhost:13133/health

# Kafka
docker exec kafka kafka-topics --list --bootstrap-server kafka:9092

# TimescaleDB
docker exec timescaledb pg_isready -U monitoring -d metrics
```

---

### Check Data Flow

```bash
# OTel Collector self-metrics
curl http://localhost:8889/metrics | grep otelcol_receiver

# Kafka topics
docker exec kafka kafka-topics --describe --topic telemetry-metrics --bootstrap-server kafka:9092

# Consumer lag
docker exec kafka kafka-consumer-groups --bootstrap-server kafka:9092 --group metrics-consumer --describe

# TimescaleDB row counts
docker exec timescaledb psql -U monitoring -d metrics -c "SELECT count(*) FROM metrics.raw_metrics;"
```

---

## Additional Resources

- [Current Plan](../../site/guide/overview.md) - support-first milestone plan
- [Milestone 1](../../site/guide/milestone-1.md) - first usable release scope
- [OpenTelemetry Docs](https://opentelemetry.io/docs/)
- [Kafka Documentation](https://kafka.apache.org/documentation/)
- [TimescaleDB Docs](https://docs.timescale.com/)
- [Telegraf Documentation](https://docs.influxdata.com/telegraf/)

---

## Contributing

When adding new configurations:
1. Add the config file to appropriate `docker/` subdirectory
2. Create detailed explanation in `docs/explanations/`
3. Update this README with link and summary
4. Add to architecture diagram if needed
5. Update troubleshooting guide if applicable
