---
title: Deployment & sizing
---

# Flexible full-stack deployment

The full stack should support both the app server and a separate on-prem monitoring server. The same architecture scales by **profile** rather than forcing every customer onto the smallest machine.

## Deployment modes

| Mode | When to use it | Trade-off |
|---|---|---|
| Same app server | Default first on-prem deployment when the customer has one server and Docker is allowed. | Simpler, but monitoring competes with the application for CPU, RAM, and disk. |
| Separate monitoring server | Preferred when the customer can provide a VM/server for monitoring. | Cleaner resource isolation and safer retention, but requires another machine. |
| Optional central forwarding | Only when support needs selected telemetry outside the customer site. | Useful for remote support, but must be explicitly configured and privacy-controlled. |

## Sizing profiles

| Profile | CPU | RAM | Disk | Use |
|---|---|---|---|---|
| **Compact** | 4 cores | 8–12 GB | 500 GB–1 TB SSD | Same-server deployment with strict limits, short retention, and careful alert/dashboard scope. |
| **Standard** | 4–8 cores | 16–32 GB | 1 TB SSD | Recommended profile for 14–30 days searchable logs on a serious customer deployment. |
| **Dedicated monitoring** | 8+ cores | 32 GB+ | 1 TB+ SSD | Preferred for higher log volume, heavier Kafka retention/partitions, longer searchable retention, and future proactive features. |

## Resource guardrails

- Cap Elasticsearch heap by profile.
- Keep enough memory outside Elasticsearch for filesystem cache and the app.
- Use index rollover and retention policies from the start.
- Limit dashboard query ranges by default.
- Track ingestion lag, dropped events, disk usage, and container health.
- Ask for more RAM/cores before enabling heavier retention or Kafka-heavy workloads.

## Installation modes

- **Online install:** Docker Compose pulls images and applies configs.
- **Offline install:** bundle images, compose files, dashboards, configs, scripts, and checksums.
- Both modes configure the same profiles and customer/site settings.
- The installer should validate RAM, CPU, disk, Docker, ports, and write permissions.

## Retention and disk planning

Searchable retention target is 14–30 days. The practical disk estimate should be based on actual daily log volume.

::: tip Planning formula
**Searchable log disk ≈ daily telemetry volume × retention days × 2–3.** Daily raw `SystemTrace_*.log` size is a good first proxy for telemetry volume. The multiplier covers indexing overhead, replicas if any, and operational margin.
:::

- Same-server default should begin with no replicas and conservative shard counts.
- `SystemTrace_*.log` backup files rotate and archive on their own schedule, outside the pipeline.
- Retain SQL text for normal operations, but extract alert/search fields so dashboards don't run expensive text queries every time.
- High-volume customers may need a dedicated monitoring server or shorter searchable retention.
