# Kafka Partitions Deep Dive

## What Are Partitions?

When you create 12 partitions for a Kafka topic, each partition is a **separate, ordered log** stored on disk.

**Physical reality**: A partition = a directory with log files.

---

## Physical Storage Structure

### On Disk (Single Broker)

```
/var/lib/kafka/data/
├── telemetry-metrics-0/          ← Partition 0
│   ├── 00000000000000000000.log  ← Segment file (actual data)
│   ├── 00000000000000000000.index ← Offset index (for fast lookups)
│   ├── 00000000000000000000.timeindex ← Timestamp index
│   └── leader-epoch-checkpoint   ← Replication metadata
│
├── telemetry-metrics-1/          ← Partition 1
│   ├── 00000000000000000000.log
│   ├── 00000000000000000000.index
│   ├── 00000000000000000000.timeindex
│   └── leader-epoch-checkpoint
│
├── telemetry-metrics-2/          ← Partition 2
│   └── ...
│
... (continues to partition 11)
│
└── telemetry-metrics-11/         ← Partition 11
    └── ...
```

**Key point**: Each partition is an **independent append-only log** with its own files.

---

## What Happens When You Create 12 Partitions?

### Command

```bash
kafka-topics --create \
  --topic telemetry-metrics \
  --partitions 12 \
  --replication-factor 1
```

### Step-by-Step Process

#### Step 1: ZooKeeper Records Metadata

```
/brokers/topics/telemetry-metrics
{
  "version": 1,
  "partitions": {
    "0": [1],    ← Partition 0 assigned to broker 1
    "1": [1],    ← Partition 1 assigned to broker 1
    "2": [1],
    "3": [1],
    "4": [1],
    "5": [1],
    "6": [1],
    "7": [1],
    "8": [1],
    "9": [1],
    "10": [1],
    "11": [1]    ← Partition 11 assigned to broker 1
  }
}
```

**Why all on broker 1?**
- You only have **1 broker** (single-server setup)
- All partitions must live somewhere
- In a 3-broker cluster, they'd be distributed across brokers

---

#### Step 2: Broker Creates Directories

Kafka broker creates 12 directories:

```bash
mkdir telemetry-metrics-0
mkdir telemetry-metrics-1
mkdir telemetry-metrics-2
...
mkdir telemetry-metrics-11
```

---

#### Step 3: Initialize Segment Files

Each partition gets initial files:

```
telemetry-metrics-0/
├── 00000000000000000000.log        ← Active segment (receives new writes)
├── 00000000000000000000.index      ← Offset → file position mapping
├── 00000000000000000000.timeindex  ← Timestamp → offset mapping
└── leader-epoch-checkpoint         ← Leader election history
```

**Files explained**:

1. **`.log`**: Actual message data (binary format)
2. **`.index`**: Maps offset to byte position in `.log` (for fast seeks)
3. **`.timeindex`**: Maps timestamp to offset (for time-based queries)
4. **`leader-epoch-checkpoint`**: Tracks replication epochs (for failover)

---

## How Messages Are Distributed Across Partitions

### Producer Partitioning Strategies

#### Strategy 1: Key-Based Partitioning (With Key)

```java
// Producer sends message with key
ProducerRecord<String, String> record = new ProducerRecord<>(
    "telemetry-metrics",
    "server-01",        // ← Key
    "{\"cpu\": 75.5}"   // ← Value
);
producer.send(record);
```

**Partition selection**:
```java
partition = hash(key) % partition_count
partition = hash("server-01") % 12
```

**Example**:
```
hash("server-01") = 487923847
487923847 % 12 = 7

Message goes to partition 7 ✅
```

**Guarantee**: All messages with **same key** go to **same partition** (ordered).

**Use case**: Ensure all metrics from `server-01` are processed in order.

---

#### Strategy 2: Round-Robin Partitioning (No Key)

```java
// Producer sends message without key
ProducerRecord<String, String> record = new ProducerRecord<>(
    "telemetry-metrics",
    null,               // ← No key
    "{\"cpu\": 75.5}"   // ← Value
);
producer.send(record);
```

**Partition selection**:
```
Message 1  → Partition 0
Message 2  → Partition 1
Message 3  → Partition 2
...
Message 12 → Partition 11
Message 13 → Partition 0  (loops back)
```

**Benefit**: Even distribution across partitions.

**Trade-off**: No ordering guarantee across messages.

---

#### Strategy 3: Custom Partitioner

```java
public class CustomPartitioner implements Partitioner {
    @Override
    public int partition(String topic, Object key, byte[] keyBytes,
                         Object value, byte[] valueBytes, Cluster cluster) {
        // Custom logic
        if (key.toString().startsWith("critical-")) {
            return 0;  // Always partition 0 for critical metrics
        }
        return hash(key) % cluster.partitionCountForTopic(topic);
    }
}
```

**Use case**: Priority partitions, geo-based routing, etc.

---

## Parallelism: The Power of Multiple Partitions

### Single Partition (Bottleneck)

```
Producer → [Partition 0] → Consumer (1 thread)
           (sequential)

Throughput: ~10 MB/sec (single consumer limit)
```

**Problem**: Can't scale beyond single consumer's capacity.

---

### 12 Partitions (Parallel Processing)

```
                    ┌─[Partition 0]─→ Consumer Thread 1
                    ├─[Partition 1]─→ Consumer Thread 2
                    ├─[Partition 2]─→ Consumer Thread 3
Producer(s) ────────┼─[Partition 3]─→ Consumer Thread 4
(key-based or       ├─[Partition 4]─→ Consumer Thread 5
 round-robin)       ├─[Partition 5]─→ Consumer Thread 6
                    ├─[Partition 6]─→ Consumer Thread 7
                    ├─[Partition 7]─→ Consumer Thread 8
                    ├─[Partition 8]─→ Consumer Thread 9
                    ├─[Partition 9]─→ Consumer Thread 10
                    ├─[Partition 10]→ Consumer Thread 11
                    └─[Partition 11]→ Consumer Thread 12
```

**Throughput**: 12 × 10 MB/sec = **120 MB/sec** 🚀

**Key insight**: More partitions = more parallelism = higher throughput.

---

## Consumer Groups and Partition Assignment

### What is a Consumer Group?

**Definition**: A group of consumers that cooperate to consume a topic.

**Rule**: Each partition is assigned to **exactly one consumer** in a group.

---

### Scenario 1: 1 Consumer Instance

```
Consumer Group: "metrics-processor"
  └─ Consumer Instance 1
       ├─ Assigned: Partitions 0-11 (all 12)
       └─ Processes: All messages sequentially
```

**Throughput**: Limited to single consumer's capacity.

**Problem**: Not utilizing available parallelism.

---

### Scenario 2: 3 Consumer Instances

```
Consumer Group: "metrics-processor"
  ├─ Consumer Instance 1
  │    ├─ Assigned: Partitions 0, 1, 2, 3
  │    └─ Processes: 4 partitions in parallel
  │
  ├─ Consumer Instance 2
  │    ├─ Assigned: Partitions 4, 5, 6, 7
  │    └─ Processes: 4 partitions in parallel
  │
  └─ Consumer Instance 3
       ├─ Assigned: Partitions 8, 9, 10, 11
       └─ Processes: 4 partitions in parallel
```

**Throughput**: 3× improvement ✅

**Load balancing**: Automatic (Kafka assigns partitions evenly).

---

### Scenario 3: 12 Consumer Instances (Optimal)

```
Consumer Group: "metrics-processor"
  ├─ Consumer Instance 1  → Partition 0
  ├─ Consumer Instance 2  → Partition 1
  ├─ Consumer Instance 3  → Partition 2
  ├─ Consumer Instance 4  → Partition 3
  ├─ Consumer Instance 5  → Partition 4
  ├─ Consumer Instance 6  → Partition 5
  ├─ Consumer Instance 7  → Partition 6
  ├─ Consumer Instance 8  → Partition 7
  ├─ Consumer Instance 9  → Partition 8
  ├─ Consumer Instance 10 → Partition 9
  ├─ Consumer Instance 11 → Partition 10
  └─ Consumer Instance 12 → Partition 11
```

**Throughput**: 12× maximum parallelism! 🎯

**Perfect scaling**: Each consumer handles exactly 1 partition.

---

### Scenario 4: 15 Consumer Instances (Over-provisioned)

```
Consumer Group: "metrics-processor"
  ├─ Consumer Instance 1  → Partition 0
  ├─ Consumer Instance 2  → Partition 1
  ...
  ├─ Consumer Instance 12 → Partition 11
  ├─ Consumer Instance 13 → IDLE ❌ (no partition assigned)
  ├─ Consumer Instance 14 → IDLE ❌
  └─ Consumer Instance 15 → IDLE ❌
```

**Problem**: **Can't have more active consumers than partitions** in a consumer group.

**Result**: 3 consumers waste resources (do nothing).

**Solution**: Add more partitions or use multiple consumer groups.

---

## Multi-Broker Cluster Distribution

### With 3 Brokers (Production)

```
Broker 1 (Leader):
├── telemetry-metrics-0/  (Leader)
├── telemetry-metrics-3/  (Leader)
├── telemetry-metrics-6/  (Leader)
├── telemetry-metrics-9/  (Leader)
└── telemetry-metrics-1/  (Follower - replica from Broker 2)

Broker 2 (Leader):
├── telemetry-metrics-1/  (Leader)
├── telemetry-metrics-4/  (Leader)
├── telemetry-metrics-7/  (Leader)
├── telemetry-metrics-10/ (Leader)
└── telemetry-metrics-2/  (Follower - replica from Broker 3)

Broker 3 (Leader):
├── telemetry-metrics-2/  (Leader)
├── telemetry-metrics-5/  (Leader)
├── telemetry-metrics-8/  (Leader)
├── telemetry-metrics-11/ (Leader)
└── telemetry-metrics-0/  (Follower - replica from Broker 1)
```

**Distribution**: Kafka spreads partition **leaders** evenly across brokers.

**Replication**: Each partition has:
- **1 leader** (handles reads/writes)
- **N-1 followers** (replicate data for fault tolerance)

**Benefit**: Load balanced, fault tolerant.

---

## Ordering Guarantees

### Within a Partition: Strict Ordering ✅

```
Partition 5:
  Offset 0: {server: "server-01", cpu: 50%}  (written first)
  Offset 1: {server: "server-01", cpu: 60%}  (written second)
  Offset 2: {server: "server-01", cpu: 70%}  (written third)
```

**Guarantee**: Consumer reads in **exact order** written (0 → 1 → 2).

**Use case**: Process metrics from a server in time order.

---

### Across Partitions: No Ordering ❌

```
Partition 3:
  Offset 100: {timestamp: 10:00:00, server: "server-01", cpu: 50%}

Partition 7:
  Offset 50:  {timestamp: 09:59:59, server: "server-02", cpu: 60%}
```

**Problem**: Consumer might read partition 7 before partition 3, even though timestamp is older.

**Solution**: Use **keys** to keep related messages in the same partition.

---

## Performance Impact: Why 12 Partitions?

### Throughput Calculation

**Assumptions**:
- Single partition throughput: ~10 MB/sec
- Target: 100k metrics/sec
- Average metric size: 1 KB

**Math**:
```
Required throughput:
100k metrics/sec × 1 KB = 100 MB/sec

Partitions needed:
100 MB/sec ÷ 10 MB/sec per partition = 10 partitions

With 12 partitions:
12 × 10 MB/sec = 120 MB/sec (20% headroom) ✅
```

---

### Rule of Thumb

```
Partitions = (Target Throughput) / (Single Partition Throughput) + Headroom
```

**Too few partitions**:
- ❌ Bottleneck (can't achieve target throughput)
- ❌ Poor parallelism

**Too many partitions**:
- ❌ More metadata overhead
- ❌ More open file handles
- ❌ Slower rebalancing
- ❌ Higher memory usage

**Sweet spot**: Enough for throughput + some headroom (10-20%).

---

## Storage Impact

### Segment Files

Each partition creates segments (default 1 GB):

```
telemetry-metrics-0/
├── 00000000000000000000.log  (1 GB - full, eligible for deletion)
├── 00000000000000100000.log  (1 GB - full)
├── 00000000000000200000.log  (500 MB - active segment)
└── ...
```

**Active segment**: Currently receiving writes (one per partition).

**Closed segments**: Full segments (can be compressed or deleted).

---

### Retention and Storage

**Configuration**:
```yaml
KAFKA_LOG_RETENTION_HOURS: 168  # 7 days
```

**Storage calculation**:
```
If writing 100 MB/sec:
100 MB/sec × 86400 sec/day × 7 days = 60.5 TB total

With 12 partitions:
60.5 TB / 12 partitions ≈ 5 TB per partition
```

**In practice**: Compression (snappy) reduces to ~2 TB per partition.

---

## Segment Rolling

### When Does Kafka Create a New Segment?

**Triggers**:
1. **Size**: Segment reaches 1 GB (`log.segment.bytes`)
2. **Time**: Segment older than 7 days (`log.segment.ms`)

**Example**:
```
00:00:00 - Start writing to segment 00000000000000000000.log
01:30:00 - Segment reaches 1 GB
01:30:01 - Close segment, create new segment 00000000000001000000.log
```

---

### Why Rolling Matters

**Benefits of segments**:
1. **Efficient deletion**: Drop entire segment (fast)
2. **Compression**: Compress closed segments (save space)
3. **Compaction**: Compact old data (for log compaction topics)

**Without segments**: Would need to delete individual messages (very slow).

---

## Visualizing Message Flow

### Producer to Partition

```
Producer A ──┐
             ├─→ hash("server-01") % 12 = 7 ──→ [Partition 7]
Producer B ──┘

Producer C ──┐
             ├─→ hash("server-02") % 12 = 3 ──→ [Partition 3]
Producer D ──┘
```

**Key point**: Same key = same partition (ordering preserved).

---

### Partition to Consumer

```
[Partition 0] ──→ Consumer Thread 1 ──→ Write to TimescaleDB
[Partition 1] ──→ Consumer Thread 2 ──→ Write to TimescaleDB
[Partition 2] ──→ Consumer Thread 3 ──→ Write to TimescaleDB
...
[Partition 11] ──→ Consumer Thread 12 ──→ Write to TimescaleDB

(All threads work in parallel!)
```

**Key point**: Parallel consumption = higher throughput.

---

## Rebalancing (Consumer Group Changes)

### What is Rebalancing?

**Definition**: Reassigning partitions when consumers join/leave a group.

### Scenario: Consumer Crashes

**Before**:
```
Consumer 1: Partitions 0-3
Consumer 2: Partitions 4-7
Consumer 3: Partitions 8-11  ← CRASHES
```

**After rebalance**:
```
Consumer 1: Partitions 0-5  (gained 2 partitions)
Consumer 2: Partitions 6-11 (gained 4 partitions)
Consumer 3: (dead)
```

**Process**:
1. Kafka detects Consumer 3 missed heartbeat
2. Trigger rebalance
3. Reassign partitions evenly to remaining consumers
4. Resume processing

**Downtime**: ~5-10 seconds during rebalance (configurable).

---

### Scenario: Adding a Consumer

**Before**:
```
Consumer 1: Partitions 0-5
Consumer 2: Partitions 6-11
```

**After adding Consumer 3**:
```
Consumer 1: Partitions 0-3  (released 2 partitions)
Consumer 2: Partitions 4-7  (released 4 partitions)
Consumer 3: Partitions 8-11 (new consumer, gained 4 partitions)
```

**Benefit**: Load automatically rebalanced.

---

## Key Takeaways

| Aspect | What Happens |
|--------|-------------|
| **Physical** | 12 directories on disk with log/index files |
| **Distribution** | Hash(key) % 12 or round-robin determines partition |
| **Parallelism** | Up to 12 consumers can process simultaneously |
| **Ordering** | Guaranteed within partition, not across partitions |
| **Rebalancing** | Adding/removing consumers redistributes partitions |
| **Storage** | Each partition has independent segment files |
| **Brokers** | In cluster, partitions spread across brokers for load balancing |
| **Throughput** | More partitions = more parallelism = higher throughput |

---

## Best Practices

### Choosing Partition Count

**Factors to consider**:
1. **Target throughput**: Higher throughput = more partitions
2. **Consumer capacity**: Can you scale consumers?
3. **Ordering requirements**: Need strict ordering? Use fewer partitions with keys
4. **Broker resources**: Each partition uses memory/file handles
5. **Rebalancing overhead**: More partitions = slower rebalances

**Formula**:
```
Partitions = max(
  (Target Throughput / Partition Throughput),
  (Number of Consumer Threads)
) + Headroom (10-20%)
```

---

### Using Keys Effectively

**✅ Good key choices**:
- `server_id`: All metrics from same server ordered
- `user_id`: All events from same user ordered
- `trace_id`: All spans from same trace ordered

**❌ Bad key choices**:
- `timestamp`: Creates hot partitions (all recent data on one partition)
- Random UUID: Defeats ordering, no better than round-robin
- `null`: No ordering, but even distribution

---

### Monitoring Partition Health

**Metrics to watch**:
1. **Partition size**: All partitions roughly equal size?
2. **Consumer lag**: Any partition falling behind?
3. **Leader distribution**: Leaders evenly distributed across brokers?
4. **Rebalance frequency**: Too many rebalances indicates instability

---

## Summary

**When you create 12 partitions**:

1. **Kafka creates** 12 directories with log/index files
2. **Messages are distributed** via hashing (keyed) or round-robin (keyless)
3. **Each partition is** an independent, ordered log
4. **Consumers can parallelize** up to 12 threads (one per partition)
5. **In a cluster**, partitions spread across brokers for load balancing
6. **Storage scales** with partition count (more partitions = more files)

**The magic**: Partitions enable **horizontal scalability** – add more partitions and consumers to increase throughput! 🎯

---

## Advanced Topics

### Partition Reassignment

**Use case**: Adding new brokers, balancing load.

**Command**:
```bash
kafka-reassign-partitions --execute --reassignment-json-file reassign.json
```

**Example**: Move partition 5 from Broker 1 to Broker 3.

---

### Partition Compaction

**Use case**: Keep only latest value per key (for changelog topics).

**Config**:
```yaml
cleanup.policy: compact
```

**Example**:
```
Before compaction:
key="user-1", value="name:John"   (offset 100)
key="user-1", value="name:Jane"   (offset 200)
key="user-2", value="name:Bob"    (offset 150)

After compaction:
key="user-1", value="name:Jane"   (offset 200, kept latest)
key="user-2", value="name:Bob"    (offset 150, only one)
```

**Not used for telemetry**: Time-series data doesn't benefit from compaction.

---

### Partition Leaders and Followers

**In 3-broker cluster with replication factor 2**:

```
Partition 0:
  Leader: Broker 1    (handles all reads/writes)
  Follower: Broker 2  (replicates data)

Partition 1:
  Leader: Broker 2
  Follower: Broker 3

Partition 2:
  Leader: Broker 3
  Follower: Broker 1
```

**Leader election**: If leader fails, follower promoted automatically.

**ISR (In-Sync Replicas)**: Followers that are caught up with leader.
