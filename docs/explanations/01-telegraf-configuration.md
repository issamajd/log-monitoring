# Telegraf Configuration Explained

## Overview

**Telegraf** is a metrics collection agent that gathers system and application metrics.

**File**: `docker/telegraf/telegraf.conf`

**What it does**:
- Collects OS-level metrics (CPU, memory, disk, network)
- Collects .NET CLR metrics (on Windows)
- Collects Docker container metrics
- Exposes metrics via Prometheus format for OTel Collector to scrape

---

## Architecture Position

```
System Metrics → Telegraf (Port 9273) → OTel Collector → Kafka → TimescaleDB
```

---

## Configuration Sections

### 1. Global Tags (Lines 5-7)

```toml
[global_tags]
  environment = "${DEPLOYMENT_ENV:development}"
  service_namespace = "log-monitoring"
```

**Purpose**: Add these tags to ALL metrics collected by Telegraf.

**Example metric with tags**:
```
cpu_usage_idle{environment="production", service_namespace="log-monitoring", host="server-01"} 45.2
```

**Why useful**: Filter and group metrics by environment in Grafana dashboards.

---

### 2. Agent Configuration (Lines 10-23)

```toml
[agent]
  interval = "10s"
  round_interval = true
  metric_batch_size = 1000
  metric_buffer_limit = 10000
  collection_jitter = "0s"
  flush_interval = "10s"
  flush_jitter = "0s"
```

#### Key Settings:

| Setting | Value | Purpose |
|---------|-------|---------|
| `interval` | 10s | Collect metrics every 10 seconds |
| `round_interval` | true | Align collection to :00, :10, :20, :30, etc. |
| `metric_batch_size` | 1000 | Send 1000 metrics per batch |
| `metric_buffer_limit` | 10000 | Buffer up to 10k metrics if output is slow |
| `flush_interval` | 10s | Send metrics every 10 seconds |

**Collection Timeline**:
```
00:00 → Collect
00:10 → Collect & Flush
00:20 → Collect & Flush
00:30 → Collect & Flush
```

---

### 3. Output Plugin: Prometheus (Lines 30-35)

```toml
[[outputs.prometheus_client]]
  listen = ":9273"
  metric_version = 2
  expiration_interval = "60s"
  collectors_exclude = ["gocollector", "process"]
  path = "/metrics"
```

**What it does**: Exposes metrics in Prometheus format at `http://telegraf:9273/metrics`

**Why Prometheus format?**: OTel Collector can scrape it using the Prometheus receiver.

#### Settings:

- **listen = ":9273"**: Bind to port 9273 (standard Prometheus exporter port)
- **metric_version = 2**: Use Prometheus v2 format (better performance)
- **expiration_interval = "60s"**: Remove metrics not updated for 60 seconds
- **collectors_exclude**: Don't expose Go runtime metrics (not needed)
- **path = "/metrics"**: HTTP endpoint path

**Access from OTel Collector**:
```yaml
# In otel-collector-config.yaml
prometheus:
  config:
    scrape_configs:
      - job_name: 'telegraf'
        targets: ['telegraf:9273']  # ← Scrapes this endpoint
```

---

### 4. Input Plugins (System Metrics)

#### CPU Metrics (Lines 42-47)

```toml
[[inputs.cpu]]
  percpu = true
  totalcpu = true
  collect_cpu_time = false
  report_active = true
  core_tags = false
```

**Metrics collected**:
- `cpu_usage_idle`: Percentage of time CPU is idle
- `cpu_usage_system`: Time spent in kernel mode
- `cpu_usage_user`: Time spent in user mode
- `cpu_usage_iowait`: Time waiting for I/O (Linux only)

**Settings**:
- `percpu = true`: Collect per-CPU core metrics
- `totalcpu = true`: Also collect aggregate across all cores
- `core_tags = false`: Don't add core number as tag (reduces cardinality)

**Example metrics**:
```
cpu_usage_idle{cpu="cpu0"} 55.2
cpu_usage_idle{cpu="cpu1"} 60.8
cpu_usage_idle{cpu="cpu-total"} 58.0
```

---

#### Memory Metrics (Lines 50)

```toml
[[inputs.mem]]
```

**Metrics collected** (cross-platform):
- `mem_total`: Total physical memory (bytes)
- `mem_available`: Available memory (bytes)
- `mem_used`: Used memory (bytes)
- `mem_free`: Free memory (bytes)
- `mem_used_percent`: Percentage used

**Example**:
```
mem_total 8589934592          (8 GB)
mem_available 4294967296       (4 GB)
mem_used_percent 50.0
```

---

#### Disk Metrics (Lines 53-55)

```toml
[[inputs.disk]]
  ignore_fs = ["tmpfs", "devtmpfs", "devfs", "iso9660", "overlay", "aufs", "squashfs"]
  mount_points = ["/", "/var", "/home"]
```

**Metrics collected**:
- `disk_used`: Disk space used (bytes)
- `disk_free`: Disk space free (bytes)
- `disk_used_percent`: Percentage used
- `disk_inodes_used`: Inodes used (Linux)

**Settings**:
- `ignore_fs`: Skip virtual/temporary filesystems
- `mount_points`: Only monitor specific mount points

**Example**:
```
disk_used_percent{path="/",device="/dev/sda1"} 75.5
disk_used_percent{path="/var",device="/dev/sda1"} 82.3
```

---

#### Disk I/O Metrics (Lines 58-59)

```toml
[[inputs.diskio]]
  devices = ["sd*", "nvme*", "xvd*"]
```

**Metrics collected**:
- `diskio_reads`: Number of read operations
- `diskio_writes`: Number of write operations
- `diskio_read_bytes`: Bytes read
- `diskio_write_bytes`: Bytes written
- `diskio_io_time`: Time spent on I/O

**Device patterns**:
- `sd*`: SATA/SCSI disks (sda, sdb, sdc)
- `nvme*`: NVMe SSDs (nvme0n1, nvme1n1)
- `xvd*`: Xen virtual disks (AWS EC2)

---

#### Network Metrics (Lines 62-64)

```toml
[[inputs.net]]
  interfaces = ["eth*", "en*", "ens*"]
  ignore_protocol_stats = false
```

**Metrics collected**:
- `net_bytes_sent`: Bytes transmitted
- `net_bytes_recv`: Bytes received
- `net_packets_sent`: Packets transmitted
- `net_packets_recv`: Packets received
- `net_err_in`: Receive errors
- `net_err_out`: Transmit errors

**Interface patterns**:
- `eth*`: Ethernet interfaces (eth0, eth1)
- `en*`: macOS interfaces (en0, en1)
- `ens*`: systemd-style (ens3, ens4)

---

#### System Metrics (Lines 67)

```toml
[[inputs.system]]
```

**Metrics collected**:
- `system_load1`, `system_load5`, `system_load15`: Load averages
- `system_uptime`: System uptime in seconds
- `system_n_cpus`: Number of CPUs
- `system_n_users`: Number of logged-in users

---

#### Process Metrics (Lines 70)

```toml
[[inputs.processes]]
```

**Metrics collected**:
- `processes_total`: Total number of processes
- `processes_running`: Running processes
- `processes_sleeping`: Sleeping processes
- `processes_stopped`: Stopped processes
- `processes_zombies`: Zombie processes

---

#### Linux-Specific Inputs (Lines 73-82)

##### Kernel Metrics

```toml
[[inputs.kernel]]
```

**Metrics collected** (Linux only):
- `kernel_context_switches`: Context switches per second
- `kernel_interrupts`: Interrupts per second
- `kernel_processes_forked`: Process forks per second

##### Kernel VMStat

```toml
[[inputs.kernel_vmstat]]
```

**Metrics collected** (Linux only):
- `kernel_vmstat_pgpgin`: Pages read from disk
- `kernel_vmstat_pgpgout`: Pages written to disk
- `kernel_vmstat_pswpin`: Pages swapped in
- `kernel_vmstat_pswpout`: Pages swapped out

##### Swap Metrics

```toml
[[inputs.swap]]
```

**Metrics collected**:
- `swap_total`: Total swap space
- `swap_used`: Used swap space
- `swap_free`: Free swap space
- `swap_used_percent`: Percentage used

##### Network Stats

```toml
[[inputs.netstat]]
```

**Metrics collected**:
- `netstat_tcp_established`: Established TCP connections
- `netstat_tcp_time_wait`: Connections in TIME_WAIT
- `netstat_tcp_listen`: Listening TCP ports

---

#### Docker Metrics (Lines 85-96)

```toml
[[inputs.docker]]
  endpoint = "unix:///var/run/docker.sock"
  gather_services = false
  container_name_include = []
  container_name_exclude = []
  timeout = "5s"
  perdevice = true
  perdevice_include = ["cpu", "blkio", "network"]
  total = true
  total_include = ["cpu", "blkio", "network"]
```

**Metrics collected**:
- `docker_container_cpu_usage_percent`: CPU usage per container
- `docker_container_mem_usage`: Memory usage per container
- `docker_container_mem_limit`: Memory limit per container
- `docker_container_net_bytes_recv`: Network bytes received
- `docker_container_net_bytes_sent`: Network bytes sent
- `docker_container_blkio_read_bytes`: Disk read bytes
- `docker_container_blkio_write_bytes`: Disk write bytes

**Settings**:
- **endpoint**: Unix socket to Docker daemon
- **perdevice = true**: Collect per-device stats (per network interface, per disk)
- **total = true**: Also collect aggregate stats per container

**Note**: Requires Docker socket mount in docker-compose:
```yaml
volumes:
  - /var/run/docker.sock:/var/run/docker.sock:ro
```

---

### 5. Windows-Specific Inputs (Lines 105-221)

**All commented out by default** (for cross-platform compatibility).

#### Windows Performance Counters

```toml
# [[inputs.win_perf_counters]]
#   [[inputs.win_perf_counters.object]]
#     ObjectName = "Processor"
#     Instances = ["*"]
#     Counters = [
#       "% Idle Time",
#       "% Processor Time",
#       ...
#     ]
```

**What it does**: Collects Windows Performance Counters (similar to Linux's `/proc` metrics).

#### .NET CLR Metrics (Lines 175-221)

```toml
# [[inputs.win_perf_counters.object]]
#   ObjectName = ".NET CLR Memory"
#   Instances = ["*"]
#   Counters = [
#     "# Bytes in all Heaps",
#     "# Gen 0 Collections",
#     "# Gen 1 Collections",
#     "# Gen 2 Collections",
#     "% Time in GC",
#     ...
#   ]
```

**Purpose**: Monitor .NET application performance.

**Metrics collected**:
- GC collections per generation
- Heap sizes
- Time spent in garbage collection
- Exception rates
- Thread counts
- Lock contention

**When to enable**: Running on Windows monitoring .NET applications.

---

### 6. Processor Plugins (Lines 228-240)

#### Rename Processor

```toml
[[processors.rename]]
  [[processors.rename.replace]]
    tag = "host"
    dest = "hostname"
```

**Purpose**: Rename the `host` tag to `hostname` (for consistency with other systems).

**Before**: `cpu_usage{host="server-01"}`
**After**: `cpu_usage{hostname="server-01"}`

---

#### Starlark Processor (Scripting)

```toml
[[processors.starlark]]
  source = '''
def apply(metric):
    # Add epoch timestamp in milliseconds
    metric.fields["timestamp_ms"] = metric.time // 1000000
    return metric
'''
```

**Purpose**: Run custom transformation logic on metrics.

**What this does**: Adds a `timestamp_ms` field with epoch milliseconds.

**Use case**: Some systems require millisecond timestamps instead of nanoseconds.

---

### 7. Aggregator Plugins (Lines 247-250)

```toml
[[aggregators.basicstats]]
  period = "60s"
  drop_original = false
  stats = ["count", "min", "max", "mean", "stdev"]
```

**Purpose**: Calculate statistics over time windows.

**Settings**:
- **period = "60s"**: Aggregate every 60 seconds
- **drop_original = false**: Keep original metrics + add stats
- **stats**: Calculate count, min, max, mean, standard deviation

**Example**:
```
Original metrics (per 10s):
cpu_usage 50
cpu_usage 55
cpu_usage 60
cpu_usage 58
cpu_usage 62
cpu_usage 57

Aggregated (per 60s):
cpu_usage_mean 57.0
cpu_usage_min 50
cpu_usage_max 62
cpu_usage_stddev 4.2
cpu_usage_count 6
```

---

## Platform-Specific Configuration

### Running on Windows

**Uncomment Windows-specific sections**:
1. `[[inputs.win_perf_counters]]` (lines 105-221)
2. Comment out Linux-specific inputs:
   - `[[inputs.kernel]]` (line 73)
   - `[[inputs.kernel_vmstat]]` (line 76)
   - `[[inputs.docker]]` (line 85) - if using Unix socket

**Create separate config**:
```bash
# Linux
docker/telegraf/telegraf-linux.conf

# Windows
docker/telegraf/telegraf-windows.conf
```

**Use in docker-compose**:
```yaml
telegraf:
  volumes:
    - ./telegraf/telegraf-${OS:-linux}.conf:/etc/telegraf/telegraf.conf:ro
```

---

### Running on Linux

**Current config works out-of-box**.

**Optional**: Uncomment specific inputs based on needs.

---

## Performance Considerations

### Collection Interval

**Current**: 10 seconds

**Trade-offs**:
```
Shorter (5s):
✅ More granular data
✅ Faster anomaly detection
❌ Higher CPU usage
❌ More storage

Longer (30s):
✅ Lower overhead
✅ Less storage
❌ Miss short-lived spikes
❌ Slower detection
```

**Recommendation**: 10s is a good balance for proactive monitoring.

---

### Metric Cardinality

**High cardinality** = Many unique tag combinations (bad for performance/storage).

**Example of high cardinality**:
```toml
# BAD: Per-core tags with many cores
[[inputs.cpu]]
  percpu = true
  core_tags = true  # ← Creates cpu="cpu0", cpu="cpu1", ..., cpu="cpu63"
```

**Better**:
```toml
# GOOD: Aggregate per host
[[inputs.cpu]]
  totalcpu = true
  core_tags = false
```

**Rule of thumb**: Only add tags you'll actually query/filter by.

---

## Integration with OTel Collector

### Data Flow

```
Telegraf → http://telegraf:9273/metrics (Prometheus format)
           ↓
OTel Collector scrapes every 10s
           ↓
Converts to OTLP format
           ↓
Exports to Kafka topic: telemetry-metrics
```

### OTel Collector Configuration

```yaml
# otel-collector-config.yaml
receivers:
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
              action: drop  # Drop Go runtime metrics
```

---

## Troubleshooting

### Check if Telegraf is Running

```bash
docker exec telegraf telegraf --version
```

### View Metrics Endpoint

```bash
curl http://localhost:9273/metrics
```

**Expected output**:
```
# HELP cpu_usage_idle Percentage of time CPU is idle
# TYPE cpu_usage_idle gauge
cpu_usage_idle{cpu="cpu0",hostname="server-01"} 55.2
cpu_usage_idle{cpu="cpu1",hostname="server-01"} 60.8
...
```

### Check Logs

```bash
docker logs telegraf
```

### Test Configuration

```bash
docker exec telegraf telegraf --test --config /etc/telegraf/telegraf.conf
```

---

## Summary

| Component | Purpose | Output |
|-----------|---------|--------|
| **Input Plugins** | Collect metrics from OS/Docker | Raw metrics |
| **Processor Plugins** | Transform/enrich metrics | Modified metrics |
| **Aggregator Plugins** | Calculate statistics | Statistical metrics |
| **Output Plugin** | Expose via Prometheus format | HTTP endpoint :9273 |

**Key Points**:
- ✅ Collects system and Docker metrics
- ✅ Prometheus-compatible output (for OTel Collector)
- ✅ Cross-platform with OS-specific sections
- ✅ 10-second collection interval
- ✅ Low overhead configuration

**Next Steps**:
1. Customize `mount_points` for your disk layout
2. Enable Windows sections if running on Windows
3. Adjust `interval` based on your needs
4. Monitor Telegraf's own resource usage
