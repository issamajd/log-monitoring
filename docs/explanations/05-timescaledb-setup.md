# TimescaleDB Docker Compose Setup Explained

## Overview

**TimescaleDB** is PostgreSQL with time-series superpowers – optimized for metrics storage and fast time-based queries.

**File**: `docker/timescaledb/docker-compose.timescaledb.yml`

**What it does**:
- Stores metrics from Kafka consumers
- Provides fast SQL queries for Grafana dashboards
- Automatic data retention and compression
- Pre-computed rollups for long-term analysis

---

## Architecture Position

```
Kafka (telemetry-metrics) → Metrics Consumer → TimescaleDB → Grafana
```

---

## What Is TimescaleDB?

**TimescaleDB = PostgreSQL + Time-Series Extensions**

**Benefits over plain PostgreSQL**:
- ✅ 10-100x faster for time-series queries
- ✅ Automatic partitioning by time (hypertables)
- ✅ Data compression (90% space savings)
- ✅ Continuous aggregates (pre-computed rollups)
- ✅ Automatic data retention policies
- ✅ SQL-compatible (use standard SQL)

**Use case**: Store millions of metrics efficiently, query quickly.

---

## Service Configuration

### Image (Line 8)

```yaml
image: timescale/timescaledb:latest-pg15
```

**Breakdown**:
- **timescale/timescaledb**: Official TimescaleDB image
- **latest**: Latest stable version
- **pg15**: Built on PostgreSQL 15

**Why pg15?**
- Stable, production-ready
- Good performance
- Wide compatibility

---

### Ports (Lines 11-12)

```yaml
ports:
  - "5432:5432"
```

**Standard PostgreSQL port**: 5432

**Access from host**:
```bash
psql -h localhost -p 5432 -U monitoring -d metrics
```

**Access from Docker**:
```bash
psql -h timescaledb -p 5432 -U monitoring -d metrics
```

---

## Environment Variables (Lines 13-17)

### POSTGRES_USER

```yaml
POSTGRES_USER: ${TIMESCALE_USER:-monitoring}
```

**Syntax**: `${ENV_VAR:-default_value}`

**Meaning**:
1. Try to read `TIMESCALE_USER` from environment
2. If not set, use `monitoring` as default

**Usage**:
```bash
# Use default
docker-compose up
# Username: monitoring

# Override with environment variable
export TIMESCALE_USER=admin
docker-compose up
# Username: admin

# Or in .env file
TIMESCALE_USER=admin
```

---

### POSTGRES_PASSWORD

```yaml
POSTGRES_PASSWORD: ${TIMESCALE_PASSWORD:-monitoring123}
```

**Default**: `monitoring123`

**⚠️ Security Warning**:
- Change in production!
- Use Docker secrets or vault
- Never commit passwords to Git

**Production example**:
```yaml
POSTGRES_PASSWORD_FILE: /run/secrets/db_password
```

---

### POSTGRES_DB

```yaml
POSTGRES_DB: ${TIMESCALE_DB:-metrics}
```

**Default**: `metrics`

**What it does**: Creates database named `metrics` on first startup.

**Multiple databases** (if needed):
```sql
-- Connect to default postgres db
psql -U monitoring -d postgres

-- Create additional databases
CREATE DATABASE logs;
CREATE DATABASE traces;
```

---

### TIMESCALEDB_TELEMETRY

```yaml
TIMESCALEDB_TELEMETRY: "off"
```

**Purpose**: Disable usage telemetry sent to TimescaleDB developers.

**Why disable?**
- Privacy/compliance requirements
- Reduce network calls
- Not needed for functionality

---

## Volumes (Lines 18-20)

### 1. Data Volume

```yaml
- timescaledb-data:/var/lib/postgresql/data
```

**Purpose**: Persistent storage for database files.

**Contents**:
```
/var/lib/postgresql/data/
├── base/              ← Database data files
│   └── metrics/       ← Your metrics database
├── pg_wal/            ← Write-Ahead Log (for crash recovery)
├── pg_stat/           ← Statistics
├── pg_logical/        ← Logical replication data
└── postgresql.conf    ← Runtime configuration
```

**Why external volume?**
- Data survives container restarts
- Can be backed up independently
- Portable across environments

**Create manually**:
```bash
docker volume create timescaledb-data
```

---

### 2. Init Script

```yaml
- ./init.sql:/docker-entrypoint-initdb.d/init.sql:ro
```

**Purpose**: Initialization script (runs on **first startup only**).

**Path**: `/docker-entrypoint-initdb.d/` is a special directory in PostgreSQL images.

**Behavior**:
- **First startup**: Runs all `.sql` and `.sh` files in alphabetical order
- **Subsequent startups**: Skipped (database already exists)

**`:ro`**: Read-only mount (container can't modify the file).

**Re-running init.sql**:
```bash
# Must delete volume to re-run
docker volume rm timescaledb-data
docker volume create timescaledb-data
docker-compose up
```

---

## Health Check (Lines 23-27)

```yaml
healthcheck:
  test: ["CMD-SHELL", "pg_isready -U ${TIMESCALE_USER:-monitoring} -d ${TIMESCALE_DB:-metrics}"]
  interval: 10s
  timeout: 5s
  retries: 5
```

### pg_isready Command

**Purpose**: PostgreSQL utility to check if database is ready to accept connections.

**What it does**:
```bash
pg_isready -U monitoring -d metrics
# Output if healthy: timescaledb:5432 - accepting connections
# Exit code: 0 = healthy ✅, != 0 = unhealthy ❌
```

**Why not just check port?**
```bash
# Bad: Only checks if port is open
nc -z localhost 5432

# Good: Checks if database is actually ready
pg_isready -U monitoring -d metrics
```

**Health check lifecycle**:
```
Container starts → starting
First success → healthy ✅
5 consecutive failures → unhealthy ❌
```

---

## PostgreSQL Tuning Parameters (Lines 29-68)

### Command Structure

```yaml
command:
  - "postgres"
  - "-c"
  - "parameter=value"
  - "-c"
  - "parameter=value"
  ...
```

**Purpose**: Override default PostgreSQL settings for time-series workload.

**Without tuning**: PostgreSQL defaults are optimized for OLTP (many small transactions), not time-series (bulk inserts + analytical queries).

---

### 1. TimescaleDB Extension (Line 32)

```yaml
shared_preload_libraries=timescaledb
```

**Purpose**: Load TimescaleDB extension at PostgreSQL startup.

**Critical**: Without this, TimescaleDB features won't work!

**What it does**:
- Registers hypertable functions
- Enables compression
- Enables continuous aggregates
- Enables retention policies

---

### 2. Connection Settings (Line 34)

```yaml
max_connections=200
```

**Purpose**: Maximum simultaneous database connections.

**Default**: 100 (too low for high-concurrency systems)

**Your setting**: 200

**Calculation**:
```
max_connections = (consumers × threads) + queries + admin + buffer

Example breakdown:
- 3 consumer services × 10 threads = 30
- Grafana dashboards (concurrent users) = 20
- ML service queries = 10
- Admin/maintenance = 10
- Buffer = 30
Total = 100 → Set to 200 for headroom
```

**Trade-off**: More connections = more memory per connection.

---

### 3. Memory Settings

#### shared_buffers=512MB (Line 36)

**Purpose**: PostgreSQL's internal cache for data pages.

**What it does**: Keeps frequently accessed data in memory (avoids disk reads).

**Rule of thumb**: 25% of total RAM

**Calculation**:
```
If server has 2 GB RAM → 512 MB (25%) ✅
If server has 4 GB RAM → 1 GB (25%)
If server has 16 GB RAM → 4 GB (25%)
```

**Your setting**: 512 MB (assumes ~2 GB RAM available)

**Why not more?**
- PostgreSQL also uses OS page cache
- Total memory = shared_buffers + OS cache
- Setting too high leaves no room for OS cache

**Performance impact**:
```
shared_buffers too low → More disk I/O → Slower queries
shared_buffers optimal → Data cached → Fast queries ✅
shared_buffers too high → Less OS cache → Actually slower
```

---

#### effective_cache_size=1536MB (Line 38)

**Purpose**: Tell PostgreSQL how much RAM the OS uses for disk caching.

**Not actual memory**: Just a hint for the query planner.

**Rule of thumb**: 50-75% of total RAM

**Calculation**:
```
If server has 2-3 GB RAM → 1.5 GB (50-75%) ✅
```

**How it affects queries**:
```sql
-- With effective_cache_size = 1.5 GB
-- Planner thinks: "OS cache is large, index scans are fast"
SELECT * FROM metrics WHERE metric_name = 'cpu.usage';
-- Query plan: Uses index (fast) ✅

-- With effective_cache_size = 128 MB
-- Planner thinks: "OS cache is small, sequential scans might be faster"
-- Query plan: Full table scan (slow) ❌
```

---

#### maintenance_work_mem=128MB (Line 40)

**Purpose**: Memory for maintenance operations.

**Operations affected**:
- `CREATE INDEX`: Index building
- `VACUUM`: Garbage collection
- `ALTER TABLE`: Schema changes

**Default**: 64 MB
**Your setting**: 128 MB (2x faster maintenance)

**Why more is better**:
```
maintenance_work_mem = 64 MB  → CREATE INDEX takes 5 minutes
maintenance_work_mem = 128 MB → CREATE INDEX takes 2.5 minutes ✅
```

**⚠️ Warning**: Doesn't affect regular queries (only maintenance).

---

#### work_mem=6553kB (Line 52)

**Purpose**: Memory **per query operation** (sorts, hash joins, aggregations).

**Default**: 4 MB
**Your setting**: ~6.5 MB

**Calculation**:
```
work_mem = (total_RAM × 0.25) / max_connections
work_mem = (2 GB × 0.25) / 200 = 2.5 MB

Your setting: 6.5 MB (slightly higher, assumes not all connections active)
```

**Why low?**
- With 200 connections, can't give each much memory
- If all connections use max work_mem simultaneously: 200 × 6.5 MB = 1.3 GB

**Danger of too high**:
```
work_mem = 100 MB
max_connections = 200
Worst case: 200 × 100 MB = 20 GB → Out of memory! ❌
```

**Per-session override** (for heavy queries):
```sql
-- Increase work_mem for this session only
SET work_mem = '256MB';

-- Run complex query
SELECT time_bucket('1 minute', time), AVG(value)
FROM metrics
GROUP BY 1;

-- Automatically resets when session ends
```

---

### 4. Write-Ahead Log (WAL) Settings

#### What is WAL?

**WAL = Write-Ahead Log**: Transaction log for crash recovery.

**How it works**:
1. Write changes to WAL first (sequential, fast)
2. Later, apply changes to data files (random, slower)
3. If crash → Replay WAL to recover

**Benefit**: Durability without sacrificing write performance.

---

#### wal_buffers=16MB (Line 44)

**Purpose**: Buffer for WAL writes before flushing to disk.

**Default**: Auto (usually ~3% of shared_buffers ≈ 15 MB)
**Your setting**: 16 MB

**Why 16 MB?**
- Good for write-heavy workloads (metrics ingestion)
- Reduces disk I/O by batching WAL writes
- Not too large (would delay crash recovery)

---

#### min_wal_size=1GB (Line 54)

**Purpose**: Minimum disk space for WAL before reusing.

**Default**: 80 MB (too small for write-heavy loads)
**Your setting**: 1 GB

**What it does**:
```
WAL disk usage < 1 GB → Keep old WAL files for reuse
WAL disk usage > 1 GB → Can delete old WAL files
```

**Benefit**: Reduces checkpoint frequency (checkpoints are expensive).

---

#### max_wal_size=4GB (Line 56)

**Purpose**: Maximum WAL size before forcing a checkpoint.

**Default**: 1 GB
**Your setting**: 4 GB

**What is a checkpoint?**
- Write all dirty buffers to disk
- Expensive operation (high I/O)
- Causes latency spikes

**Why 4 GB?**
- Fewer checkpoints (smoother performance)
- Longer between I/O spikes

**Trade-off**:
```
Large max_wal_size:
✅ Fewer checkpoints → smoother performance
✅ Better for write-heavy workloads
❌ More disk space for WAL
❌ Longer crash recovery time

Small max_wal_size:
✅ Fast crash recovery
✅ Less disk space
❌ Frequent checkpoints → spiky performance
```

---

### 5. Checkpoint Settings

#### checkpoint_completion_target=0.9 (Line 42)

**Purpose**: Spread checkpoint I/O over time (avoid spikes).

**Range**: 0.0 to 1.0
**Your setting**: 0.9 (90% of checkpoint interval)

**Example**:
```
Checkpoint interval: 5 minutes (based on max_wal_size)

checkpoint_completion_target = 0.5 (50%):
  Spread writes over 2.5 minutes
  Result: Spiky I/O (writes finish too early)

checkpoint_completion_target = 0.9 (90%):
  Spread writes over 4.5 minutes
  Result: Smooth I/O ✅
```

**Why 0.9?**
- Smoothest possible I/O
- Best for SSDs (reduces write amplification)
- Minimal impact on query performance

---

### 6. Query Planner Settings

#### default_statistics_target=100 (Line 46)

**Purpose**: Sample size for query planner statistics.

**Range**: 10 to 10000
**Default**: 100
**Your setting**: 100 (balanced)

**What it does**:
```
Higher value:
✅ More accurate statistics → better query plans
❌ Slower ANALYZE command
❌ More storage for statistics

Lower value:
✅ Fast ANALYZE
❌ Inaccurate statistics → suboptimal query plans
```

**When to increase** (to 200-500):
- Complex queries with many joins
- Uneven data distribution
- Query plans not optimal

---

#### random_page_cost=1.1 (Line 48)

**Purpose**: Cost estimate for random disk reads vs sequential reads.

**Default**: 4.0 (assumes spinning disk)
**Your setting**: 1.1 (assumes SSD)

**What it means**:
```
random_page_cost = 4.0 (spinning disk):
  "Random reads are 4× slower than sequential reads"
  Planner prefers sequential scans

random_page_cost = 1.1 (SSD):
  "Random reads are nearly as fast as sequential reads"
  Planner uses indexes more often ✅
```

**Impact on queries**:
```sql
-- Table: 1 million rows
-- Index: metric_name
SELECT * FROM metrics WHERE metric_name = 'cpu.usage';

With random_page_cost = 4.0:
  Query plan: Sequential scan (reads all 1M rows) ❌

With random_page_cost = 1.1:
  Query plan: Index scan (reads only matching rows) ✅
```

**Rule of thumb**:
- **Spinning disk**: 4.0
- **SSD**: 1.1-1.5
- **NVMe SSD**: 1.0-1.1

---

#### effective_io_concurrency=200 (Line 50)

**Purpose**: Number of simultaneous disk I/O operations.

**Default**: 1 (spinning disk, one head)
**Your setting**: 200 (SSD, many parallel operations)

**What it does**:
```
effective_io_concurrency = 1 (spinning disk):
  "Disk can only do 1 I/O at a time"

effective_io_concurrency = 200 (SSD):
  "Disk can do 200 I/Os simultaneously"
  Enables parallel bitmap heap scans ✅
```

**Performance impact**:
```sql
-- Query scanning index
SELECT * FROM metrics WHERE time > NOW() - INTERVAL '1 hour';

With effective_io_concurrency = 1:
  Read pages sequentially (slow)

With effective_io_concurrency = 200:
  Read many pages in parallel (fast) ✅
```

---

### 7. Parallel Query Settings

#### max_worker_processes=4 (Line 58)

**Purpose**: Total background worker processes for parallelism.

**Your setting**: 4 (assumes 4-core CPU)

**What it does**: Pool of workers for parallel operations.

**Rule of thumb**: Match CPU core count.

---

#### max_parallel_workers_per_gather=2 (Line 60)

**Purpose**: Max parallel workers **per query operation**.

**Your setting**: 2

**Example**:
```sql
-- This query can use up to 2 parallel workers
SELECT time_bucket('1 minute', time), AVG(value)
FROM metrics
WHERE time > NOW() - INTERVAL '1 hour'
GROUP BY 1;

Query plan:
  Gather (2 workers)
    -> Parallel Seq Scan on metrics
```

**Why not more?**
- Overhead of parallelism (coordination cost)
- Diminishing returns beyond 2-4 workers per query

---

#### max_parallel_workers=4 (Line 62)

**Purpose**: Max parallel workers **across all queries**.

**Your setting**: 4

**Example**:
```
Query 1: Uses 2 workers
Query 2: Uses 2 workers
Total: 4 workers (max reached)
Query 3: Cannot use parallelism (no workers available)
```

**Calculation**:
```
max_parallel_workers <= max_worker_processes
max_parallel_workers = 4 (can run 2 parallel queries simultaneously)
```

---

#### max_parallel_maintenance_workers=2 (Line 64)

**Purpose**: Parallel workers for maintenance operations.

**Your setting**: 2

**Operations affected**:
- `CREATE INDEX` (parallel index build)
- `VACUUM` (parallel vacuum)

**Performance**:
```
Without parallelism:
  CREATE INDEX takes 10 minutes

With 2 parallel workers:
  CREATE INDEX takes 5 minutes ✅
```

---

### 8. Timezone Settings (Lines 66-68)

```yaml
log_timezone=UTC
timezone=UTC
```

**Purpose**: Use UTC for all timestamps.

**Why UTC?**
- Standard across distributed systems
- No daylight saving time issues
- Easier correlation (logs, metrics, traces all UTC)

**In queries**:
```sql
-- Stored in UTC
INSERT INTO metrics (time, value) VALUES (NOW(), 75.5);

-- Query in local timezone
SELECT time AT TIME ZONE 'America/New_York', value
FROM metrics
WHERE time > NOW() - INTERVAL '1 hour';
```

---

## Connection String Examples

### From .NET Consumer

```csharp
var connectionString = "Host=timescaledb;Port=5432;Database=metrics;Username=monitoring;Password=monitoring123";

using var conn = new NpgsqlConnection(connectionString);
conn.Open();

// Insert metrics
using var cmd = new NpgsqlCommand(
    "INSERT INTO metrics.raw_metrics (time, metric_name, value, host_name) VALUES (@time, @name, @value, @host)",
    conn
);
cmd.Parameters.AddWithValue("time", DateTime.UtcNow);
cmd.Parameters.AddWithValue("name", "cpu.usage");
cmd.Parameters.AddWithValue("value", 75.5);
cmd.Parameters.AddWithValue("host", "server-01");
cmd.ExecuteNonQuery();
```

---

### From Python ML Service

```python
import psycopg2

conn = psycopg2.connect(
    host="timescaledb",
    port=5432,
    database="metrics",
    user="monitoring",
    password="monitoring123"
)

cursor = conn.cursor()
cursor.execute("""
    SELECT time, value
    FROM metrics.raw_metrics
    WHERE metric_name = 'cpu.usage'
      AND time > NOW() - INTERVAL '24 hours'
""")
rows = cursor.fetchall()
```

---

### From Grafana

**Datasource configuration**:
```yaml
Host: timescaledb:5432
Database: metrics
User: monitoring
Password: monitoring123
SSL Mode: disable
```

**Query example**:
```sql
SELECT
  time_bucket('5 minutes', time) AS time,
  AVG(value) as value,
  metric_name
FROM metrics.raw_metrics
WHERE
  $__timeFilter(time) AND
  metric_name = 'cpu.usage'
GROUP BY 1, metric_name
ORDER BY 1
```

---

## Performance Tuning Summary

| Parameter | Your Value | Purpose | Tuned For |
|-----------|-----------|---------|-----------|
| `shared_buffers` | 512 MB | Data cache | ~2 GB RAM system |
| `effective_cache_size` | 1.5 GB | OS cache hint | ~3 GB total RAM |
| `max_connections` | 200 | Connection limit | High concurrency |
| `work_mem` | 6.5 MB | Per-query memory | Many connections |
| `maintenance_work_mem` | 128 MB | Maintenance ops | Faster index builds |
| `wal_buffers` | 16 MB | WAL cache | Write-heavy workload |
| `max_wal_size` | 4 GB | Checkpoint trigger | Fewer checkpoints |
| `random_page_cost` | 1.1 | Disk access cost | SSD storage |
| `effective_io_concurrency` | 200 | I/O parallelism | SSD |
| `max_parallel_workers` | 4 | Query parallelism | 4-core CPU |

---

## Scaling for Different System Sizes

### Small System (2 GB RAM, 2 cores)

```yaml
# Current configuration ✅
shared_buffers=512MB
effective_cache_size=1536MB
max_connections=200
max_parallel_workers=2
```

---

### Medium System (8 GB RAM, 4 cores)

```yaml
shared_buffers=2GB
effective_cache_size=6GB
max_connections=300
work_mem=8MB
maintenance_work_mem=512MB
max_parallel_workers=4
max_parallel_workers_per_gather=2
```

---

### Large System (32 GB RAM, 16 cores)

```yaml
shared_buffers=8GB
effective_cache_size=24GB
max_connections=500
work_mem=16MB
maintenance_work_mem=2GB
max_parallel_workers=16
max_parallel_workers_per_gather=4
```

---

## Troubleshooting

### Check PostgreSQL Version

```bash
docker exec timescaledb psql -U monitoring -d metrics -c "SELECT version();"
```

### Check TimescaleDB Extension

```bash
docker exec timescaledb psql -U monitoring -d metrics -c "SELECT * FROM pg_extension WHERE extname = 'timescaledb';"
```

### View Current Settings

```bash
docker exec timescaledb psql -U monitoring -d metrics -c "SHOW shared_buffers;"
docker exec timescaledb psql -U monitoring -d metrics -c "SHOW max_connections;"
```

### Monitor Active Connections

```sql
SELECT count(*) FROM pg_stat_activity;
```

### Check Database Size

```sql
SELECT pg_size_pretty(pg_database_size('metrics'));
```

### View Slow Queries

```sql
SELECT query, mean_exec_time, calls
FROM pg_stat_statements
ORDER BY mean_exec_time DESC
LIMIT 10;
```

---

## Summary

**TimescaleDB Configuration**:
- ✅ PostgreSQL 15 with TimescaleDB extension
- ✅ Tuned for time-series workload (2 GB RAM, 4 cores)
- ✅ 200 concurrent connections
- ✅ SSD-optimized settings
- ✅ Parallel query execution
- ✅ Automatic init script on first startup
- ✅ Health checks for container orchestration

**Key Tunables**:
- **Memory**: Scale `shared_buffers` and `effective_cache_size` with RAM
- **Connections**: Adjust `max_connections` based on workload
- **Parallelism**: Match `max_parallel_workers` to CPU cores
- **WAL**: Increase `max_wal_size` for write-heavy loads

**Next Steps**:
1. Adjust memory settings for your available RAM
2. Create external volume: `docker volume create timescaledb-data`
3. Review and customize `init.sql` schema
4. Start service and verify health
5. Monitor performance metrics in production
