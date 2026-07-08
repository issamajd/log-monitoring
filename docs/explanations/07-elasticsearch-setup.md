# Elasticsearch Docker Compose Setup Explained

## Overview

**Elasticsearch** is a distributed search and analytics engine designed for log storage and full-text search.

**File**: `docker/elasticsearch/docker-compose.elasticsearch.yml`

**What it does**:
- Stores logs from Kafka consumers
- Provides full-text search capabilities
- Enables log aggregation and analysis
- Automatic index lifecycle management (ILM)
- Web UI via Kibana

---

## Architecture Position

```
Kafka (telemetry-logs) → Logs Consumer → Elasticsearch → Kibana (UI)
                                              ↓
                                         Grafana (dashboards)
```

---

## Services Overview

The compose file defines 3 services:

1. **Elasticsearch**: Search engine and log storage
2. **Elasticsearch-Init**: One-time setup (ILM policies, templates)
3. **Kibana**: Web UI for Elasticsearch

---

## 1. Elasticsearch Service (Lines 7-44)

```yaml
elasticsearch:
  image: docker.elastic.co/elasticsearch/elasticsearch:8.11.0
  container_name: elasticsearch
  hostname: elasticsearch
  environment:
    - node.name=elasticsearch
    - cluster.name=log-monitoring-cluster
    - discovery.type=single-node
    - bootstrap.memory_lock=true
    - xpack.security.enabled=false
    - xpack.security.http.ssl.enabled=false
    - xpack.security.transport.ssl.enabled=false
    - "ES_JAVA_OPTS=-Xms1g -Xmx1g"
    # Performance settings
    - indices.query.bool.max_clause_count=4096
    - search.max_buckets=100000
    - action.destructive_requires_name=true
```

### Image Version

**Version**: 8.11.0 (Elasticsearch 8.x)

**Why 8.x?**
- Latest stable version
- Improved performance over 7.x
- Better memory management
- Enhanced security features

---

### Cluster Configuration

#### node.name=elasticsearch

**Purpose**: Unique identifier for this node.

**In a cluster**:
```yaml
# Node 1
node.name=elasticsearch-1

# Node 2
node.name=elasticsearch-2

# Node 3
node.name=elasticsearch-3
```

---

#### cluster.name=log-monitoring-cluster

**Purpose**: Cluster identifier (all nodes in a cluster must have the same name).

**Why important?**
- Prevents accidental joining of wrong clusters
- Groups nodes together

**Current setup**: Single node (cluster name mostly for consistency).

---

#### discovery.type=single-node

**Purpose**: Run as a single-node cluster (no clustering).

**What it does**:
- Skip cluster discovery (no need to find other nodes)
- Disable quorum checks
- Optimize for single-node deployment

**For production cluster**:
```yaml
# Remove discovery.type=single-node
# Add seed hosts
discovery.seed_hosts: ["elasticsearch-1", "elasticsearch-2", "elasticsearch-3"]
cluster.initial_master_nodes: ["elasticsearch-1", "elasticsearch-2", "elasticsearch-3"]
```

---

#### bootstrap.memory_lock=true

**Purpose**: Lock Elasticsearch JVM memory to prevent swapping.

**Why critical?**
- Swapping is **extremely slow** (kills performance)
- Elasticsearch expects data in RAM
- Swapping = 100-1000x slower queries

**What it does**:
```
OS tries to swap memory to disk → Blocked! ✅
Elasticsearch memory stays in RAM → Fast queries ✅
```

**Requires ulimits** (see lines 24-27):
```yaml
ulimits:
  memlock:
    soft: -1  # Unlimited
    hard: -1  # Unlimited
```

**Without memlock**:
```
Elasticsearch allocates 1 GB RAM
OS swaps 500 MB to disk → Queries become 100x slower ❌
```

---

### Security Settings (Lines 16-18)

```yaml
xpack.security.enabled=false
xpack.security.http.ssl.enabled=false
xpack.security.transport.ssl.enabled=false
```

**⚠️ Development mode**: Security disabled for simplicity.

**What's disabled**:
- **Authentication**: No username/password required
- **Authorization**: No role-based access control
- **Encryption**: HTTP and inter-node communication not encrypted

**For production**:
```yaml
xpack.security.enabled=true
xpack.security.http.ssl.enabled=true
xpack.security.transport.ssl.enabled=true

# Also configure certificates and passwords
```

**Why disable in dev?**
- Faster setup
- Easier testing
- No certificate management

---

### Java Memory Settings (Line 19)

```yaml
ES_JAVA_OPTS=-Xms1g -Xmx1g
```

**Purpose**: Set JVM heap size.

#### -Xms1g (Initial heap)

**Meaning**: Start with 1 GB heap.

#### -Xmx1g (Maximum heap)

**Meaning**: Maximum 1 GB heap.

**Why same value for both?**
- Prevents JVM from resizing heap (expensive operation)
- More predictable memory usage
- Avoids fragmentation

**Rule of thumb**: Set to **50% of available RAM**, max 31 GB.

**Calculation**:
```
System RAM: 4 GB
Available for ES: 2 GB (50% of system)
Heap size: 1 GB (50% of available)
Remaining 1 GB: OS page cache (for Lucene files)
```

**Why not more?**
- Elasticsearch uses **off-heap memory** for Lucene (file system cache)
- Too much heap = less OS cache = slower queries
- Java GC pressure increases with larger heaps

**Scaling**:
```yaml
# For 8 GB RAM system
ES_JAVA_OPTS=-Xms2g -Xmx2g

# For 16 GB RAM system
ES_JAVA_OPTS=-Xms4g -Xmx4g

# For 64 GB RAM system
ES_JAVA_OPTS=-Xms31g -Xmx31g  # Max 31 GB (Java compressed pointers limit)
```

---

### Performance Settings

#### indices.query.bool.max_clause_count=4096 (Line 21)

**Purpose**: Maximum number of clauses in a boolean query.

**Default**: 1024 (too low for complex queries)
**Your setting**: 4096

**Example complex query**:
```json
{
  "query": {
    "bool": {
      "should": [
        {"term": {"host": "server-01"}},
        {"term": {"host": "server-02"}},
        ... (4000+ clauses)
      ]
    }
  }
}
```

**Without increase**: Query fails with "too many clauses" error.

---

#### search.max_buckets=100000 (Line 22)

**Purpose**: Maximum number of aggregation buckets.

**Default**: 10000 (too low for detailed aggregations)
**Your setting**: 100000

**Example**:
```json
{
  "aggs": {
    "by_host": {
      "terms": {"field": "host_name"}
    }
  }
}
```

**If you have 50k unique hosts**: Default limit would fail.

**Why limit?**
- Too many buckets = high memory usage
- Protect against runaway aggregations
- 100k is generous but not dangerous

---

#### action.destructive_requires_name=true (Line 23)

**Purpose**: Prevent accidental deletion of all indices.

**What it does**:
```bash
# This FAILS (no name specified) ✅
curl -X DELETE http://localhost:9200/*

# This works (explicit name)
curl -X DELETE http://localhost:9200/logs-2026-01-01
```

**Why important**: Prevents `DELETE /*` from destroying everything.

---

### ulimits (Lines 24-30)

```yaml
ulimits:
  memlock:
    soft: -1
    hard: -1
  nofile:
    soft: 65536
    hard: 65536
```

#### memlock: -1 (Unlimited)

**Purpose**: Allow unlimited memory locking (required for `bootstrap.memory_lock=true`).

**Without this**: Elasticsearch can't lock memory → swapping occurs.

---

#### nofile: 65536

**Purpose**: Maximum open file descriptors.

**Default**: 1024 (way too low!)
**Your setting**: 65536 (64k files)

**Why Elasticsearch needs many files?**
- Each Lucene segment = multiple files (data, index, metadata)
- Each shard = multiple segments
- Thousands of files per index

**Without increase**:
```
Open files: 1024
Elasticsearch tries to open file #1025 → Error! ❌
Indexing fails, queries fail
```

---

### Volumes (Lines 31-33)

```yaml
volumes:
  - elasticsearch-data:/usr/share/elasticsearch/data
  - ./index-templates:/usr/share/elasticsearch/config/templates:ro
```

#### elasticsearch-data (Persistent storage)

**Purpose**: Store indices, shards, and cluster state.

**Contents**:
```
/usr/share/elasticsearch/data/
├── nodes/
│   └── 0/
│       ├── indices/
│       │   └── logs-000001/
│       │       ├── 0/  (shard 0)
│       │       ├── 1/  (shard 1)
│       │       └── 2/  (shard 2)
│       └── _state/  (cluster state)
```

**Why external volume**: Data survives container restarts.

**Create manually**:
```bash
docker volume create elasticsearch-data
```

---

#### index-templates (Optional)

**Purpose**: Directory for template files (if using file-based templates).

**Note**: Your setup uses API-based templates (elasticsearch-init service), so this is optional.

---

### Ports (Lines 34-36)

```yaml
ports:
  - "9200:9200"
  - "9300:9300"
```

#### 9200 (HTTP API)

**Purpose**: REST API for queries, indexing, management.

**Usage**:
```bash
# Index a log
curl -X POST http://localhost:9200/logs/_doc \
  -H 'Content-Type: application/json' \
  -d '{"message": "Application started", "level": "INFO"}'

# Search logs
curl -X GET http://localhost:9200/logs/_search?q=ERROR

# Cluster health
curl http://localhost:9200/_cluster/health
```

---

#### 9300 (Transport)

**Purpose**: Inter-node communication (cluster transport protocol).

**Use case**:
- In a cluster: Nodes communicate on port 9300
- Single node: Not used (but exposed for future clustering)

**Production cluster**:
```
Node 1: 9300 ←→ Node 2: 9300 ←→ Node 3: 9300
```

---

### Health Check (Lines 39-43)

```yaml
healthcheck:
  test: ["CMD-SHELL", "curl -s http://localhost:9200/_cluster/health | grep -q '\"status\":\"green\"\\|\"status\":\"yellow\"'"]
  interval: 30s
  timeout: 10s
  retries: 5
```

**What it does**: Checks if cluster status is green or yellow.

**Cluster statuses**:
- **Green**: All shards allocated (healthy) ✅
- **Yellow**: All primary shards allocated, some replicas missing (acceptable for single-node) ✅
- **Red**: Some primary shards missing (unhealthy) ❌

**Command breakdown**:
```bash
curl -s http://localhost:9200/_cluster/health
# Returns: {"status":"yellow",...}

grep -q '\"status\":\"green\"\\|\"status\":\"yellow\"'
# Succeeds if status is green OR yellow
```

**Why yellow is OK?**
- Single-node cluster can't have replicas (no other nodes)
- Yellow = primary shards healthy, replicas unassigned
- For single node, yellow is expected and fine

---

## 2. Elasticsearch-Init Service (Lines 47-236)

```yaml
elasticsearch-init:
  image: docker.elastic.co/elasticsearch/elasticsearch:8.11.0
  container_name: elasticsearch-init
  depends_on:
    elasticsearch:
      condition: service_healthy
  entrypoint: ["/bin/bash", "-c"]
  command: |
    "
    # Initialization script
    "
```

**Purpose**: One-time initialization to create:
1. ILM (Index Lifecycle Management) policy
2. Index template
3. Initial index with alias

**Lifecycle**:
1. Waits for Elasticsearch to be healthy
2. Creates ILM policy, template, and index
3. Exits (doesn't keep running)

---

### ILM Policy (Lines 60-107)

```json
{
  "policy": {
    "phases": {
      "hot": {...},
      "warm": {...},
      "cold": {...},
      "delete": {...}
    }
  }
}
```

### What is ILM?

**ILM = Index Lifecycle Management**: Automatic index management based on age and size.

**Phases**:
```
Hot → Warm → Cold → Delete
```

**Analogy**: Like moving files from SSD → HDD → Archive → Trash.

---

### Hot Phase (Lines 65-76)

```json
"hot": {
  "min_age": "0ms",
  "actions": {
    "rollover": {
      "max_age": "1d",
      "max_primary_shard_size": "50gb"
    },
    "set_priority": {
      "priority": 100
    }
  }
}
```

#### What is "Hot" Phase?

**Active index**: Currently receiving writes (new logs).

**Characteristics**:
- ✅ Fastest storage (SSD)
- ✅ High priority for recovery
- ✅ Frequently queried

---

#### Rollover Action

```json
"rollover": {
  "max_age": "1d",
  "max_primary_shard_size": "50gb"
}
```

**Purpose**: Create a new index when conditions are met.

**Triggers** (whichever comes first):
- **max_age: 1 day**: Index is 1 day old
- **max_primary_shard_size: 50 GB**: Any shard reaches 50 GB

**Example**:
```
Day 1:
  logs-000001 (active, receiving writes)

Day 2 (rollover triggered by max_age):
  logs-000001 (rolled over, read-only)
  logs-000002 (new active index, receiving writes)

Day 3:
  logs-000001 (moved to warm phase)
  logs-000002 (rolled over)
  logs-000003 (new active index)
```

**Why rollover?**
- Prevents indices from growing too large (performance degrades)
- Easier deletion (drop entire index instead of documents)
- Better query performance (smaller indices)

---

#### Set Priority

```json
"set_priority": {
  "priority": 100
}
```

**Purpose**: Recovery priority after node restart.

**Priority scale**: Higher = recovered first.

**Example**:
```
Hot indices (priority 100): Recovered first ✅
Warm indices (priority 50): Recovered second
Cold indices (priority 0): Recovered last
```

**Why high priority for hot?**
- Active indices are critical (receiving writes)
- Users query recent data most often

---

### Warm Phase (Lines 77-90)

```json
"warm": {
  "min_age": "2d",
  "actions": {
    "set_priority": {
      "priority": 50
    },
    "shrink": {
      "number_of_shards": 1
    },
    "forcemerge": {
      "max_num_segments": 1
    }
  }
}
```

#### What is "Warm" Phase?

**Older logs**: Still queried occasionally, but less frequently.

**Characteristics**:
- ✅ Optimized for storage (fewer shards)
- ✅ Optimized for reads (merged segments)
- ❌ Read-only (no more writes)

**Transition**: Index moves to warm phase after 2 days.

---

#### Shrink Action

```json
"shrink": {
  "number_of_shards": 1
}
```

**Purpose**: Reduce number of shards.

**Example**:
```
Hot phase: 3 shards
  logs-000001 (shard 0, shard 1, shard 2)

Warm phase: 1 shard
  logs-000001 (shard 0 only)  ← Shards merged into one
```

**Why shrink?**
- **Less overhead**: Fewer shards = less memory per index
- **Faster queries**: Single shard queries are faster
- **Storage savings**: Reduces metadata overhead

**Trade-off**: Can't write to shrunken index (read-only).

---

#### Forcemerge Action

```json
"forcemerge": {
  "max_num_segments": 1
}
```

**Purpose**: Merge Lucene segments for optimal read performance.

**What is a segment?**
```
Elasticsearch index = multiple Lucene segments

Before forcemerge:
  Shard 0: [segment1][segment2][segment3][segment4][segment5]
           Each query searches all 5 segments (slower)

After forcemerge:
  Shard 0: [segment1 - merged]
           Query searches 1 segment (faster) ✅
```

**Benefits**:
- ✅ Faster queries (fewer segments to search)
- ✅ Better compression (removes deleted docs)
- ✅ Less disk space

**Downside**: CPU-intensive operation (takes time).

**Why safe in warm phase?**
- Read-only data (no new writes during merge)
- Lower query load (can afford CPU spike)

---

### Cold Phase (Lines 91-98)

```json
"cold": {
  "min_age": "7d",
  "actions": {
    "set_priority": {
      "priority": 0
    }
  }
}
```

#### What is "Cold" Phase?

**Rarely accessed logs**: Kept for compliance or long-term analysis.

**Characteristics**:
- ✅ Lowest storage cost (could use slower disks)
- ✅ Lowest priority (recovered last after restart)
- ❌ Slower queries (acceptable for old data)

**Transition**: After 7 days.

**Actions**: Only priority change (no shrink/merge, already done in warm).

**Future enhancements**:
```json
"cold": {
  "actions": {
    "searchable_snapshot": {...}  // Move to object storage (S3)
  }
}
```

---

### Delete Phase (Lines 99-104)

```json
"delete": {
  "min_age": "30d",
  "actions": {
    "delete": {}
  }
}
```

**Purpose**: Automatically delete indices older than 30 days.

**Example timeline**:
```
Day 0: logs-000001 created (hot)
Day 2: → warm phase (shrink, forcemerge)
Day 7: → cold phase (low priority)
Day 30: → DELETED ✅
```

**Why 30 days?**
- Balance storage costs vs data retention needs
- Adjust based on compliance requirements

**For longer retention**:
```json
"min_age": "90d"  // Keep logs for 90 days
```

---

## 3. Index Template (Lines 111-218)

```json
{
  "index_patterns": ["logs-*"],
  "template": {
    "settings": {...},
    "mappings": {...}
  }
}
```

### What is an Index Template?

**Purpose**: Automatically apply settings and mappings to new indices matching a pattern.

**Pattern**: `logs-*` matches `logs-000001`, `logs-000002`, etc.

**Applies to**:
- All new indices starting with `logs-`
- Created automatically during rollover

---

### Index Settings (Lines 116-124)

```json
"settings": {
  "number_of_shards": 3,
  "number_of_replicas": 0,
  "index.lifecycle.name": "logs-policy",
  "index.lifecycle.rollover_alias": "logs",
  "index.refresh_interval": "5s",
  "index.translog.durability": "async",
  "index.translog.sync_interval": "5s"
}
```

#### number_of_shards: 3

**Purpose**: Split index into 3 shards for parallelism.

**What is a shard?**
```
Index: logs-000001
├── Shard 0 (1/3 of data)
├── Shard 1 (1/3 of data)
└── Shard 2 (1/3 of data)
```

**Benefits**:
- ✅ Parallel queries (3 shards = 3x faster)
- ✅ Distribute load across nodes (in cluster)
- ✅ Scale storage beyond single node

**Why 3?**
- Good balance for single node
- Enough parallelism for performance
- Not too many (overhead of managing shards)

**Production cluster (3 nodes)**:
```
Node 1: Shard 0, Shard 1
Node 2: Shard 1, Shard 2
Node 3: Shard 2, Shard 0
```

---

#### number_of_replicas: 0

**Purpose**: No replica shards (no copies).

**What is a replica?**
```
With replicas (number_of_replicas: 1):
  Primary: Shard 0 (Node 1)
  Replica: Shard 0 (Node 2)  ← Copy for fault tolerance

Without replicas (number_of_replicas: 0):
  Primary: Shard 0 (Node 1)
  No replica ← No redundancy
```

**Why 0 for single node?**
- Can't allocate replicas (no other nodes)
- Saves disk space (no duplicate data)
- Acceptable for development

**⚠️ Production**: Set to 1+ for fault tolerance.

---

#### index.lifecycle.name: "logs-policy"

**Purpose**: Apply the ILM policy created earlier.

**Result**: This index follows hot → warm → cold → delete lifecycle.

---

#### index.lifecycle.rollover_alias: "logs"

**Purpose**: Use `logs` alias for rollover.

**How it works**:
```
Always write to alias "logs":
  POST /logs/_doc {"message": "Log entry"}

Alias points to current index:
  Day 1: logs → logs-000001 (active)
  Day 2: logs → logs-000002 (active, after rollover)
  Day 3: logs → logs-000003 (active)
```

**Benefit**: Application always writes to `/logs`, no need to track index numbers.

---

#### index.refresh_interval: "5s"

**Purpose**: How often to refresh the index (make new docs searchable).

**What is refresh?**
```
Log inserted → In-memory buffer (not searchable yet)
After 5 seconds → Refresh → Now searchable ✅
```

**Default**: 1 second (more real-time, but higher overhead)
**Your setting**: 5 seconds (balance)

**Trade-offs**:
```
1 second:
✅ Near real-time search
❌ High CPU (frequent refreshes)

30 seconds:
✅ Low overhead
❌ Delayed search (logs not visible for 30s)

5 seconds:
✅ Good balance ← Your setting
```

---

#### index.translog.durability: "async"

**Purpose**: Transaction log (durability) mode.

**What is translog?**
- Write-ahead log for crash recovery
- Similar to PostgreSQL WAL

**Durability modes**:

**request (default)**:
```
Write log → Flush translog to disk → Acknowledge ✅
(Every request waits for disk fsync)
```

**async (your setting)**:
```
Write log → Acknowledge immediately ✅
Flush translog every 5s in background
```

**Trade-offs**:
```
request:
✅ No data loss (durability guaranteed)
❌ Slower writes (wait for disk)

async:
✅ Fast writes (no disk wait)
❌ Potential data loss (up to 5 seconds if crash)
```

**Why async for logs?**
- Logs are less critical (losing 5 seconds OK)
- High throughput more important
- Data already in Kafka (can replay if needed)

---

#### index.translog.sync_interval: "5s"

**Purpose**: How often to sync translog to disk (when using async).

**Default**: 5 seconds
**Your setting**: 5 seconds

**Maximum data loss**: 5 seconds of logs (if Elasticsearch crashes).

---

### Mappings (Lines 125-215)

```json
"mappings": {
  "dynamic": "true",
  "dynamic_templates": [...],
  "properties": {...}
}
```

### What are Mappings?

**Mappings = Schema**: Define how fields are stored and indexed.

**Like SQL**:
```sql
CREATE TABLE logs (
  timestamp TIMESTAMP,
  message TEXT,
  level VARCHAR(10),
  ...
)
```

---

### Dynamic Mapping (Line 126)

```json
"dynamic": "true"
```

**Purpose**: Allow new fields without explicit definition.

**Example**:
```json
// Index a log with a new field "custom_field"
POST /logs/_doc
{
  "message": "Log entry",
  "custom_field": "value"  ← Not in mapping, but allowed
}

// Elasticsearch automatically creates mapping for "custom_field"
```

**Why useful?**
- Flexible (don't need to define every field)
- Handles varied log formats

**Alternative**:
```json
"dynamic": "strict"  // Reject documents with unknown fields
"dynamic": "false"   // Accept but don't index unknown fields
```

---

### Dynamic Templates (Lines 127-137)

```json
"dynamic_templates": [
  {
    "strings_as_keywords": {
      "match_mapping_type": "string",
      "mapping": {
        "type": "keyword",
        "ignore_above": 1024
      }
    }
  }
]
```

**Purpose**: Automatically map string fields as `keyword` (not `text`).

**What it does**:
```json
// Without dynamic template:
"some_new_field": "value"  → Mapped as "text" (analyzed, slower)

// With dynamic template:
"some_new_field": "value"  → Mapped as "keyword" (exact match, fast)
```

**Why keyword by default?**
- Most log fields are identifiers (host names, IDs, levels)
- Exact match queries are more common than full-text search
- Faster and less storage

**ignore_above: 1024**:
- Don't index strings longer than 1024 characters
- Prevents indexing huge values (like stack traces as keywords)

---

### Field Mappings (Lines 138-214)

#### Timestamp Fields (Lines 139-144)

```json
"@timestamp": {
  "type": "date"
},
"time": {
  "type": "date"
}
```

**Purpose**: Store timestamps (for time-based queries and sorting).

**Date type**:
- Stored as milliseconds since epoch
- Supports date math: `now-1h`, `now-7d`
- Sortable and aggregatable

---

#### Message Field (Lines 145-153)

```json
"message": {
  "type": "text",
  "fields": {
    "keyword": {
      "type": "keyword",
      "ignore_above": 32766
    }
  }
}
```

**Multi-field mapping**: Index same content two ways.

**text type**:
- Analyzed (tokenized into words)
- Full-text search
- Example: "Application started successfully"
  → Tokens: ["application", "started", "successfully"]

**keyword type**:
- Not analyzed (exact value)
- Exact match, aggregations
- Example: "Application started successfully"
  → Exact string (no tokenization)

**Usage**:
```json
// Full-text search (uses "message" text field)
GET /logs/_search
{
  "query": {
    "match": {"message": "application"}  ← Matches "Application started..."
  }
}

// Exact match (uses "message.keyword")
GET /logs/_search
{
  "query": {
    "term": {"message.keyword": "Application started successfully"}
  }
}

// Aggregation (uses "message.keyword")
GET /logs/_search
{
  "aggs": {
    "common_messages": {
      "terms": {"field": "message.keyword"}
    }
  }
}
```

**ignore_above: 32766**:
- Max keyword length (32 KB)
- Prevents indexing massive log messages as keyword
- Full message still stored (just not indexed for exact match)

---

#### Keyword Fields (Lines 154-189)

```json
"level": {"type": "keyword"},
"severity": {"type": "keyword"},
"service_name": {"type": "keyword"},
"host_name": {"type": "keyword"},
"environment": {"type": "keyword"},
"trace_id": {"type": "keyword"},
"span_id": {"type": "keyword"},
"correlation_id": {"type": "keyword"},
"user_id": {"type": "keyword"},
"session_id": {"type": "keyword"},
"request_id": {"type": "keyword"},
"exception_type": {"type": "keyword"},
"logger_name": {"type": "keyword"},
"thread_name": {"type": "keyword"}
```

**Why keyword type?**
- Exact match queries (filter by level=ERROR)
- Aggregations (count by service_name)
- Fast (not analyzed)

**Example queries**:
```json
// Filter by level
{"query": {"term": {"level": "ERROR"}}}

// Aggregate by service
{"aggs": {"by_service": {"terms": {"field": "service_name"}}}}

// Filter by trace ID
{"query": {"term": {"trace_id": "abc123"}}}
```

---

#### Text Fields (Lines 193-199)

```json
"exception_message": {
  "type": "text"
},
"stack_trace": {
  "type": "text",
  "index": false
}
```

**exception_message**: Full-text search enabled.

**stack_trace**:
- `"index": false`: Don't index (save space)
- Still stored (can retrieve and display)
- Can't search on it (acceptable for stack traces)

**Why not index stack traces?**
- Very large (thousands of lines)
- Rarely searched (usually filter by exception type first)
- Saves significant disk space and indexing time

---

#### Object Fields (Lines 206-213)

```json
"attributes": {
  "type": "object",
  "dynamic": true
},
"resource": {
  "type": "object",
  "dynamic": true
}
```

**Purpose**: Store nested key-value pairs from OpenTelemetry.

**Example**:
```json
{
  "message": "Request processed",
  "attributes": {
    "http.method": "GET",
    "http.status_code": 200,
    "http.url": "/api/users"
  },
  "resource": {
    "service.name": "api-service",
    "service.version": "1.2.3"
  }
}
```

**Query nested fields**:
```json
{"query": {"term": {"attributes.http.method": "GET"}}}
```

---

## 4. Initial Index Creation (Lines 221-230)

```bash
curl -X PUT 'http://elasticsearch:9200/logs-000001' \
  -H 'Content-Type: application/json' \
  -d '{
    "aliases": {
      "logs": {
        "is_write_index": true
      }
    }
  }'
```

**Purpose**: Create first index with alias.

**What it does**:
1. Create index: `logs-000001`
2. Create alias: `logs` → points to `logs-000001`
3. Mark as write index (receives new writes)

**Result**:
```
Write to /logs → Goes to logs-000001
Read from /logs → Reads from logs-000001

After rollover:
Write to /logs → Goes to logs-000002 (new write index)
Read from /logs → Reads from logs-000001 AND logs-000002
```

---

## 5. Kibana Service (Lines 238-258)

```yaml
kibana:
  image: docker.elastic.co/kibana/kibana:8.11.0
  container_name: kibana
  depends_on:
    elasticsearch:
      condition: service_healthy
  ports:
    - "5601:5601"
  environment:
    - ELASTICSEARCH_HOSTS=http://elasticsearch:9200
    - XPACK_SECURITY_ENABLED=false
    - SERVER_NAME=kibana
    - TELEMETRY_ENABLED=false
```

### What is Kibana?

**Purpose**: Web UI for Elasticsearch.

**Features**:
- **Discover**: Search and explore logs
- **Dashboards**: Visualizations and charts
- **Dev Tools**: Run Elasticsearch queries
- **Index Management**: View indices, ILM status

**Access**: `http://localhost:5601`

---

### Environment Variables

#### ELASTICSEARCH_HOSTS=http://elasticsearch:9200

**Purpose**: Tell Kibana where Elasticsearch is.

---

#### XPACK_SECURITY_ENABLED=false

**Purpose**: Disable authentication (matches Elasticsearch setting).

**Production**: Enable and configure users/roles.

---

#### TELEMETRY_ENABLED=false

**Purpose**: Disable usage telemetry to Elastic.

**Why disable**: Privacy, reduce network calls.

---

### Health Check (Lines 253-257)

```yaml
healthcheck:
  test: ["CMD-SHELL", "curl -s http://localhost:5601/api/status | grep -q 'available'"]
  interval: 30s
  timeout: 10s
  retries: 5
```

**What it does**: Check if Kibana API is available.

**Success**: Kibana ready to accept connections.

---

## Complete Data Flow Example

### 1. Logs Consumer Indexes Log

```bash
curl -X POST http://elasticsearch:9200/logs/_doc \
  -H 'Content-Type: application/json' \
  -d '{
    "@timestamp": "2026-01-14T10:30:00Z",
    "message": "Database connection failed",
    "level": "ERROR",
    "service_name": "api-service",
    "host_name": "server-01",
    "exception_type": "System.Data.SqlClient.SqlException"
  }'
```

### 2. Elasticsearch Processing

```
1. Apply template → logs-000001 (3 shards, ILM policy)
2. Index fields according to mappings
3. Add to in-memory buffer
4. After 5 seconds → Refresh → Searchable ✅
5. Async translog flush (durability)
```

### 3. Search in Kibana

```
Open Kibana → Discover
Filter: level = ERROR
Time range: Last 15 minutes
Result: Shows the error log with all fields
```

### 4. ILM Lifecycle

```
Day 0: logs-000001 (hot, active)
Day 1: Rollover → logs-000002 (new active), logs-000001 (hot, read-only)
Day 2: logs-000001 → warm (shrink to 1 shard, forcemerge)
Day 7: logs-000001 → cold (low priority)
Day 30: logs-000001 → DELETED ✅
```

---

## Performance Tuning

### For High Throughput (50k+ logs/sec)

**Elasticsearch**:
```yaml
ES_JAVA_OPTS=-Xms4g -Xmx4g  # More heap

# Larger bulk thread pool
thread_pool.write.queue_size: 1000

# More shards for parallelism
number_of_shards: 6
```

**Index settings**:
```json
{
  "index.refresh_interval": "30s",  // Less frequent refresh
  "index.translog.sync_interval": "30s",  // Batch more
  "index.translog.flush_threshold_size": "1gb"  // Larger flush
}
```

---

### For Fast Queries

**Mappings**:
```json
{
  // Index only what you query
  "dynamic": "false",

  // More doc_values for aggregations
  "fielddata": {"enabled": false},

  // Use filters (cached)
  "query": {
    "bool": {
      "filter": [  ← Cached, fast
        {"term": {"level": "ERROR"}}
      ]
    }
  }
}
```

---

### For Low Storage

**ILM**:
```json
{
  "rollover": {
    "max_age": "12h",  // Roll over more frequently
    "max_primary_shard_size": "20gb"
  },
  "delete": {
    "min_age": "7d"  // Delete sooner
  }
}
```

**Mappings**:
```json
{
  "message": {
    "type": "text",
    "index": false  // Don't index message (save space)
  },
  "stack_trace": {
    "enabled": false  // Don't store at all
  }
}
```

---

## Troubleshooting

### Check Elasticsearch Health

```bash
curl http://localhost:9200/_cluster/health?pretty
```

### View Indices

```bash
curl http://localhost:9200/_cat/indices?v
```

### Check ILM Status

```bash
curl http://localhost:9200/_ilm/status?pretty
```

### View ILM Policy

```bash
curl http://localhost:9200/_ilm/policy/logs-policy?pretty
```

### Check Index Lifecycle

```bash
curl http://localhost:9200/logs-000001/_ilm/explain?pretty
```

### Search Logs

```bash
curl -X GET http://localhost:9200/logs/_search?pretty \
  -H 'Content-Type: application/json' \
  -d '{
    "query": {
      "match": {"level": "ERROR"}
    },
    "size": 10
  }'
```

### View Index Stats

```bash
curl http://localhost:9200/logs-000001/_stats?pretty
```

---

## Summary

**Elasticsearch Configuration**:
- ✅ Single-node setup (for development)
- ✅ 1 GB JVM heap (tune based on RAM)
- ✅ 3 shards per index (parallelism)
- ✅ ILM policy (hot → warm → cold → delete)
- ✅ 30-day retention
- ✅ Automatic rollover (daily or 50 GB)
- ✅ No replicas (single node)
- ✅ Security disabled (development mode)

**ILM Lifecycle**:
```
Day 0-2: Hot (active, 3 shards)
Day 2-7: Warm (shrink to 1 shard, forcemerge)
Day 7-30: Cold (low priority)
Day 30+: Deleted
```

**Mappings**:
- ✅ Dynamic templates (strings → keywords)
- ✅ Multi-field message (text + keyword)
- ✅ Keyword fields for filters/aggregations
- ✅ Text fields for full-text search
- ✅ Stack traces not indexed (save space)

**Kibana**:
- ✅ Web UI on port 5601
- ✅ Search, visualize, manage indices

**Key Features**:
- ✅ Automatic index lifecycle management
- ✅ Optimized for log storage (rollover, shrink, merge)
- ✅ Flexible schema (dynamic mappings)
- ✅ Fast queries (proper field types)
- ✅ Storage efficient (ILM, compression)

**Next Steps**:
1. Tune heap size for your RAM
2. Adjust ILM retention periods
3. Create Kibana dashboards
4. Monitor index sizes and query performance
5. Enable security for production
