# Kafka Docker Compose Setup Explained

## Overview

**Kafka** is a distributed streaming platform that acts as a message buffer between data producers and consumers.

**File**: `docker/kafka/docker-compose.kafka.yml`

**What it does**:
- Buffers telemetry data (metrics, logs, traces)
- Decouples producers (OTel Collector) from consumers (processing services)
- Enables horizontal scaling
- Provides durability and replay capability

---

## Architecture Position

```
OTel Collector → Kafka Topics → Consumers → Storage
                 (telemetry-metrics)
                 (telemetry-logs)
                 (telemetry-traces)
```

---

## Services Overview

The compose file defines 4 services:

1. **ZooKeeper**: Cluster coordinator
2. **Kafka**: Message broker
3. **Kafka-Init**: Topic creator (runs once)
4. **Kafka-UI**: Web management interface

---

## 1. ZooKeeper Service (Lines 7-29)

```yaml
zookeeper:
  image: confluentinc/cp-zookeeper:7.5.0
  container_name: zookeeper
  hostname: zookeeper
  ports:
    - "2181:2181"
  environment:
    ZOOKEEPER_CLIENT_PORT: 2181
    ZOOKEEPER_TICK_TIME: 2000
    ZOOKEEPER_INIT_LIMIT: 5
    ZOOKEEPER_SYNC_LIMIT: 2
    ZOOKEEPER_MAX_CLIENT_CNXNS: 60
```

### What is ZooKeeper?

**Purpose**: Distributed coordination service for Kafka.

**Responsibilities**:
- Track which brokers are alive
- Store cluster metadata (topics, partitions, replicas)
- Elect partition leaders
- Track consumer group offsets (older Kafka versions)

**Analogy**: ZooKeeper is the **air traffic controller** for Kafka brokers.

---

### Configuration Parameters

#### ZOOKEEPER_CLIENT_PORT: 2181

**Purpose**: Port for client connections (Kafka brokers connect here).

**Standard port**: 2181 (convention)

---

#### ZOOKEEPER_TICK_TIME: 2000

**Purpose**: Basic time unit in milliseconds (heartbeat interval).

**Value**: 2000 = 2 seconds

**What it does**: ZooKeeper nodes send heartbeats every 2 seconds to prove they're alive.

---

#### ZOOKEEPER_INIT_LIMIT: 5

**Purpose**: Timeout for initial connection/sync between ZooKeeper nodes.

**Value**: 5 ticks = 5 × 2 seconds = 10 seconds

**Use case**: When a ZooKeeper node starts, it has 10 seconds to sync with the cluster.

---

#### ZOOKEEPER_SYNC_LIMIT: 2

**Purpose**: Timeout for ongoing sync between nodes after initial connection.

**Value**: 2 ticks = 2 × 2 seconds = 4 seconds

**Use case**: If a follower node doesn't respond within 4 seconds, it's considered dead.

---

#### ZOOKEEPER_MAX_CLIENT_CNXNS: 60

**Purpose**: Maximum concurrent connections from a single client IP.

**Value**: 60 connections

**Why limit?**: Prevents a single client from monopolizing ZooKeeper.

---

### Volumes

```yaml
volumes:
  - zookeeper-data:/var/lib/zookeeper/data
  - zookeeper-logs:/var/lib/zookeeper/log
```

**zookeeper-data**: Cluster state and metadata
**zookeeper-logs**: Transaction logs (for recovery)

**Why external volumes?**: Data persists even if container is recreated.

---

### Health Check

```yaml
healthcheck:
  test: ["CMD", "nc", "-z", "localhost", "2181"]
  interval: 10s
  timeout: 5s
  retries: 5
```

**What it does**: Uses `nc` (netcat) to check if port 2181 is listening.

**Success**: Port is open → ZooKeeper is ready
**Failure**: 5 failed checks in a row → Container marked unhealthy

---

## 2. Kafka Service (Lines 31-77)

```yaml
kafka:
  image: confluentinc/cp-kafka:7.5.0
  container_name: kafka
  hostname: kafka
  depends_on:
    zookeeper:
      condition: service_healthy
  ports:
    - "9092:9092"
    - "9093:9093"
  environment:
    KAFKA_BROKER_ID: 1
    KAFKA_ZOOKEEPER_CONNECT: zookeeper:2181
    ...
```

### Dependency Management

```yaml
depends_on:
  zookeeper:
    condition: service_healthy
```

**Purpose**: Kafka waits for ZooKeeper to be **healthy** before starting.

**Without this**: Kafka would fail to start (can't connect to ZooKeeper).

---

### Broker Configuration

#### KAFKA_BROKER_ID: 1

**Purpose**: Unique identifier for this broker.

**In a cluster**:
```
Broker 1: KAFKA_BROKER_ID = 1
Broker 2: KAFKA_BROKER_ID = 2
Broker 3: KAFKA_BROKER_ID = 3
```

**Current setup**: Single broker (ID = 1)

---

#### KAFKA_ZOOKEEPER_CONNECT: zookeeper:2181

**Purpose**: Tell Kafka where to find ZooKeeper.

**Format**: `hostname:port`

---

### Listener Configuration (IMPORTANT!)

```yaml
KAFKA_LISTENER_SECURITY_PROTOCOL_MAP: PLAINTEXT:PLAINTEXT,PLAINTEXT_HOST:PLAINTEXT
KAFKA_ADVERTISED_LISTENERS: PLAINTEXT://kafka:9092,PLAINTEXT_HOST://localhost:9093
KAFKA_INTER_BROKER_LISTENER_NAME: PLAINTEXT
```

#### KAFKA_LISTENER_SECURITY_PROTOCOL_MAP

**Purpose**: Map listener names to security protocols.

**Format**: `LISTENER_NAME:SECURITY_PROTOCOL`

**In this config**:
- `PLAINTEXT` → `PLAINTEXT` (no encryption)
- `PLAINTEXT_HOST` → `PLAINTEXT` (no encryption)

**Available protocols**:
- `PLAINTEXT`: No encryption
- `SSL`: TLS encryption
- `SASL_PLAINTEXT`: Authentication without encryption
- `SASL_SSL`: Authentication + encryption (production)

---

#### KAFKA_ADVERTISED_LISTENERS

**Purpose**: Addresses that clients use to connect to Kafka.

**Format**: `LISTENER_NAME://HOST:PORT`

**In this config**:
```yaml
PLAINTEXT://kafka:9092,PLAINTEXT_HOST://localhost:9093
```

**Two listeners**:

1. **PLAINTEXT://kafka:9092**
   - **For**: Docker containers (OTel Collector, consumers)
   - **Hostname**: `kafka` (Docker DNS)
   - **Port**: 9092

2. **PLAINTEXT_HOST://localhost:9093**
   - **For**: Host machine (your laptop, testing)
   - **Hostname**: `localhost`
   - **Port**: 9093

**Why two listeners?**

**Problem with single listener**:
```yaml
# If we only had: localhost:9092
KAFKA_ADVERTISED_LISTENERS: PLAINTEXT://localhost:9092
```

**From Docker container**:
```
Container tries: localhost:9092
Result: Connects to itself, NOT Kafka! ❌
```

**From host machine**:
```
Host tries: localhost:9092
Result: Works! ✅
```

**Solution**: Two listeners with different hostnames.

---

#### KAFKA_INTER_BROKER_LISTENER_NAME: PLAINTEXT

**Purpose**: Which listener brokers use to talk to each other.

**Value**: `PLAINTEXT` → Use `kafka:9092`

**In multi-broker cluster**:
```
Broker 1 connects to Broker 2 via: kafka-2:9092
Broker 2 connects to Broker 3 via: kafka-3:9092
```

---

### Replication Configuration

```yaml
KAFKA_OFFSETS_TOPIC_REPLICATION_FACTOR: 1
KAFKA_TRANSACTION_STATE_LOG_MIN_ISR: 1
KAFKA_TRANSACTION_STATE_LOG_REPLICATION_FACTOR: 1
KAFKA_DEFAULT_REPLICATION_FACTOR: 1
KAFKA_MIN_INSYNC_REPLICAS: 1
```

**All set to `1`**: Because this is a **single-broker setup**.

**What is replication?**

In a cluster, each partition has:
- **1 leader**: Handles reads and writes
- **N-1 followers**: Replicate data from leader

**Example with replication factor 3**:
```
Partition 0:
  Leader: Broker 1
  Followers: Broker 2, Broker 3

If Broker 1 dies:
  Broker 2 becomes new leader ✅
```

**In production (3 brokers)**:
```yaml
KAFKA_DEFAULT_REPLICATION_FACTOR: 2
KAFKA_MIN_INSYNC_REPLICAS: 2
```

**Meaning**: Each partition has 2 replicas, and writes succeed only after 2 brokers acknowledge.

---

### Data Retention

```yaml
KAFKA_LOG_RETENTION_HOURS: 168         # 7 days
KAFKA_LOG_RETENTION_BYTES: 10737418240 # 10 GB
KAFKA_LOG_SEGMENT_BYTES: 1073741824    # 1 GB
KAFKA_LOG_CLEANUP_POLICY: delete
```

#### KAFKA_LOG_RETENTION_HOURS: 168

**Purpose**: Delete messages older than 7 days.

**Why 7 days?**
- Allows reprocessing recent data
- Matches TimescaleDB raw retention (7 days)
- Balances storage vs recovery capability

---

#### KAFKA_LOG_RETENTION_BYTES: 10 GB

**Purpose**: Delete oldest messages when topic exceeds 10 GB.

**Why both time AND size limits?**

**Use whichever triggers first**:
```
If low traffic: Delete after 7 days
If high traffic: Delete after 10 GB (might be < 7 days)
```

---

#### KAFKA_LOG_SEGMENT_BYTES: 1 GB

**Purpose**: Each log segment file is 1 GB.

**What is a segment?**

Kafka stores data in segment files:
```
topic-partition-0/
├── 00000000000000000000.log (1 GB - full)
├── 00000000000001000000.log (1 GB - full)
├── 00000000000002000000.log (500 MB - active)
```

**Why 1 GB?**
- Optimal for performance (not too many files)
- Efficient deletion (drop entire segment)
- Reasonable compaction size

---

#### KAFKA_LOG_CLEANUP_POLICY: delete

**Purpose**: How to clean up old data.

**Options**:
- **delete**: Remove old messages (time/size based)
- **compact**: Keep only latest value per key
- **delete,compact**: Both strategies

**For telemetry**: `delete` (time-series data doesn't need compaction)

---

### Performance Tuning

```yaml
KAFKA_COMPRESSION_TYPE: snappy
KAFKA_MESSAGE_MAX_BYTES: 2097152       # 2 MB
KAFKA_NUM_IO_THREADS: 8
KAFKA_NUM_NETWORK_THREADS: 3
KAFKA_SOCKET_SEND_BUFFER_BYTES: 102400    # 100 KB
KAFKA_SOCKET_RECEIVE_BUFFER_BYTES: 102400 # 100 KB
```

#### KAFKA_COMPRESSION_TYPE: snappy

**Purpose**: Compress messages before storing.

**Why snappy?**
- Fast compression (low CPU overhead)
- Good compression ratio (2-3x)
- Better than gzip for real-time data

**Alternatives**:
- `gzip`: Higher compression, slower
- `lz4`: Fastest, lower compression
- `zstd`: Best compression, higher CPU

---

#### KAFKA_MESSAGE_MAX_BYTES: 2 MB

**Purpose**: Maximum size of a single message.

**Why 2 MB?**
- Handles large trace spans
- Handles batched metrics from OTel Collector
- Not too large (avoids network issues)

**If exceeded**: Producer gets error, must split message.

---

#### KAFKA_NUM_IO_THREADS: 8

**Purpose**: Number of threads for disk I/O.

**Why 8?**
- Assumes modern SSD (high parallelism)
- Assumes 4-8 CPU cores

**Effect**: More threads = higher disk throughput

---

#### KAFKA_NUM_NETWORK_THREADS: 3

**Purpose**: Number of threads handling network requests.

**Why 3?**
- Sufficient for moderate load
- Each thread handles multiple connections

**Rule of thumb**: `# of cores / 2`

---

#### KAFKA_SOCKET_SEND_BUFFER_BYTES: 100 KB

**Purpose**: OS-level socket send buffer size.

**Why 100 KB?**
- Balance between throughput and latency
- Too small: Many system calls
- Too large: Increased latency

---

### Volumes

```yaml
volumes:
  - kafka-data:/var/lib/kafka/data
```

**Contents**:
```
/var/lib/kafka/data/
├── telemetry-metrics-0/
│   ├── 00000000000000000000.log
│   └── 00000000000000000000.index
├── telemetry-metrics-1/
│   └── ...
└── ...
```

**Why external volume**: Data survives container restarts.

---

### Health Check

```yaml
healthcheck:
  test: ["CMD", "kafka-broker-api-versions", "--bootstrap-server", "localhost:9092"]
  interval: 10s
  timeout: 10s
  retries: 5
```

**What it does**: Queries Kafka's API versions (verifies Kafka is responding).

**Success**: Kafka returns API versions → Healthy ✅
**Failure**: No response or error → Unhealthy ❌

**Why this command?**
- ✅ Actually tests Kafka APIs (not just port open)
- ✅ Lightweight (fast query)
- ✅ Confirms broker is ready to accept requests

---

## 3. Kafka-Init Service (Lines 79-146)

```yaml
kafka-init:
  image: confluentinc/cp-kafka:7.5.0
  container_name: kafka-init
  depends_on:
    kafka:
      condition: service_healthy
  entrypoint: ["/bin/bash", "-c"]
  command: |
    "
    echo 'Waiting for Kafka to be ready...'
    sleep 10

    echo 'Creating telemetry topics...'

    # Create topics...
    "
```

**Purpose**: One-time initialization to create Kafka topics.

**Lifecycle**:
1. Waits for Kafka to be healthy
2. Creates topics
3. Exits (doesn't keep running)

---

### Topic: telemetry-metrics (Lines 94-101)

```bash
kafka-topics --create --if-not-exists \
  --bootstrap-server kafka:9092 \
  --topic telemetry-metrics \
  --partitions 12 \
  --replication-factor 1 \
  --config retention.ms=604800000 \
  --config compression.type=snappy \
  --config cleanup.policy=delete
```

**Settings**:
- **topic**: `telemetry-metrics`
- **partitions**: 12 (high parallelism for throughput)
- **replication-factor**: 1 (single broker)
- **retention.ms**: 604800000 = 7 days
- **compression.type**: snappy
- **cleanup.policy**: delete

**Why 12 partitions?**

**Throughput calculation**:
```
Target: 100k metrics/sec
Single partition max: ~10 MB/sec
Average metric size: 1 KB

Required bandwidth: 100k × 1 KB = 100 MB/sec
Partitions needed: 100 MB/sec ÷ 10 MB/sec = 10

12 partitions = 20% headroom ✅
```

**Parallelism benefit**:
```
1 partition  → Max 1 consumer
12 partitions → Max 12 consumers (12x throughput!)
```

---

### Topic: telemetry-logs (Lines 104-111)

```bash
kafka-topics --create --if-not-exists \
  --bootstrap-server kafka:9092 \
  --topic telemetry-logs \
  --partitions 12 \
  --replication-factor 1 \
  --config retention.ms=604800000 \
  --config compression.type=snappy \
  --config cleanup.policy=delete
```

**Same settings as metrics** (also high volume).

**Why 12 partitions?**
- Target: 50k logs/sec
- Similar reasoning to metrics

---

### Topic: telemetry-traces (Lines 114-121)

```bash
kafka-topics --create --if-not-exists \
  --bootstrap-server kafka:9092 \
  --topic telemetry-traces \
  --partitions 6 \
  --replication-factor 1 \
  --config retention.ms=259200000 \
  --config compression.type=snappy \
  --config cleanup.policy=delete
```

**Differences**:
- **partitions**: 6 (lower volume than metrics/logs)
- **retention.ms**: 259200000 = 3 days (shorter retention)

**Why 3 days retention?**
- Traces are large (detailed spans)
- Less frequently queried after a few days
- Save storage space

---

### Topic: alerts (Lines 124-129)

```bash
kafka-topics --create --if-not-exists \
  --bootstrap-server kafka:9092 \
  --topic alerts \
  --partitions 3 \
  --replication-factor 1 \
  --config retention.ms=604800000
```

**Purpose**: Queue for alert processing.

**Why 3 partitions?**
- Alerts are low volume
- 3 partitions allows 3 parallel alert processors

---

### Topic: dlq (Dead Letter Queue) (Lines 132-137)

```bash
kafka-topics --create --if-not-exists \
  --bootstrap-server kafka:9092 \
  --topic dlq \
  --partitions 3 \
  --replication-factor 1 \
  --config retention.ms=1209600000
```

**Purpose**: Store messages that failed processing.

**retention.ms**: 1209600000 = 14 days (longer retention for investigation)

**Use case**:
```
Consumer reads from telemetry-logs
Parsing fails (malformed JSON)
Send to DLQ for manual review
```

---

## 4. Kafka UI Service (Lines 147-162)

```yaml
kafka-ui:
  image: provectuslabs/kafka-ui:latest
  container_name: kafka-ui
  depends_on:
    kafka:
      condition: service_healthy
  ports:
    - "8080:8080"
  environment:
    KAFKA_CLUSTERS_0_NAME: log-monitoring
    KAFKA_CLUSTERS_0_BOOTSTRAPSERVERS: kafka:9092
    KAFKA_CLUSTERS_0_ZOOKEEPER: zookeeper:2181
    DYNAMIC_CONFIG_ENABLED: "true"
```

**Purpose**: Web-based Kafka management UI.

**Access**: `http://localhost:8080`

**Features**:
- View topics, partitions, messages
- Monitor consumer lag
- Browse messages in real-time
- View broker status
- Create/delete topics (with DYNAMIC_CONFIG_ENABLED)

**Use cases**:
- Debug message flow
- Monitor consumer progress
- Inspect message contents
- Check partition distribution

---

## Networks and Volumes

### Network

```yaml
networks:
  monitoring-net:
    external: true
```

**external: true**: Network must be created before starting services.

**Create manually**:
```bash
docker network create monitoring-net
```

**Why external?**: Shared across multiple compose files (Kafka, TimescaleDB, OTel Collector).

---

### Volumes

```yaml
volumes:
  kafka-data:
    external: true
  zookeeper-data:
    external: true
  zookeeper-logs:
    external: true
```

**external: true**: Volumes must be created manually.

**Create manually**:
```bash
docker volume create kafka-data
docker volume create zookeeper-data
docker volume create zookeeper-logs
```

**Why external?**: Data persists even after `docker-compose down`.

---

## Startup Sequence

```
1. ZooKeeper starts
   ↓ (waits for health check)
2. Kafka starts
   ↓ (waits for health check)
3. Kafka-Init creates topics (exits)
4. Kafka-UI starts
```

**Critical**: Health checks ensure proper startup order.

---

## Production vs Development

| Setting | Development (Current) | Production (3-Broker Cluster) |
|---------|----------------------|------------------------------|
| Brokers | 1 | 3 |
| ZooKeeper nodes | 1 | 3 or 5 |
| Replication factor | 1 | 2 or 3 |
| Min in-sync replicas | 1 | 2 |
| Listener security | PLAINTEXT | SASL_SSL |
| Authentication | None | SASL/SCRAM |
| Encryption | None | TLS |

**For production**: Create separate `docker-compose.kafka-cluster.yml` with 3 brokers.

---

## Troubleshooting

### Check Kafka Logs

```bash
docker logs kafka
```

### List Topics

```bash
docker exec kafka kafka-topics --list --bootstrap-server kafka:9092
```

### Describe Topic

```bash
docker exec kafka kafka-topics --describe --topic telemetry-metrics --bootstrap-server kafka:9092
```

**Output**:
```
Topic: telemetry-metrics
PartitionCount: 12
ReplicationFactor: 1
Partition: 0    Leader: 1    Replicas: 1    Isr: 1
Partition: 1    Leader: 1    Replicas: 1    Isr: 1
...
```

### Produce Test Message

```bash
docker exec -it kafka kafka-console-producer --bootstrap-server kafka:9092 --topic telemetry-metrics
> {"metric": "test", "value": 123}
> (Ctrl+C to exit)
```

### Consume Messages

```bash
docker exec kafka kafka-console-consumer \
  --bootstrap-server kafka:9092 \
  --topic telemetry-metrics \
  --from-beginning \
  --max-messages 10
```

### Check Consumer Lag

```bash
docker exec kafka kafka-consumer-groups \
  --bootstrap-server kafka:9092 \
  --group metrics-consumer \
  --describe
```

---

## Summary

**ZooKeeper**:
- Cluster coordinator for Kafka
- Manages metadata and leader election
- Port 2181

**Kafka Broker**:
- Message storage and routing
- Ports 9092 (internal), 9093 (external)
- 7-day retention, snappy compression
- Single broker (development mode)

**Topics Created**:
- `telemetry-metrics`: 12 partitions, 7 days
- `telemetry-logs`: 12 partitions, 7 days
- `telemetry-traces`: 6 partitions, 3 days
- `alerts`: 3 partitions, 7 days
- `dlq`: 3 partitions, 14 days

**Kafka UI**:
- Web interface on port 8080
- Browse topics, messages, consumers

**Key Features**:
- ✅ High throughput (12 partitions)
- ✅ Configurable retention
- ✅ Compression (snappy)
- ✅ Separate listeners for Docker and host
- ✅ Health checks for proper startup
- ✅ External volumes (data persistence)

**Next Steps**:
1. Create network and volumes
2. Start services with `docker-compose up`
3. Verify topics created via Kafka UI
4. Test message production/consumption
