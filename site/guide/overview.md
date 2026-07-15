---
title: Overview
---

# Build a practical monitoring system around the logs we already have

This project is for our own on-prem customer deployments, where the application usually runs as a Windows service on a single server. The first usable version should help **support** find errors quickly, identify slow operations, and understand what happened in a single session or request — without opening massive raw log files.

| | |
|---|---|
| **Primary user** | Support team |
| **Deployment** | Windows on-prem |
| **Searchable retention** | 14–30 days |
| **Last updated** | 2026-07-14 |

## Direction

The original plan has a solid backbone, but it needs to be refocused around current reality: very large existing application logs plus direct OpenTelemetry instrumentation.

- Keep Elasticsearch, TimescaleDB, Grafana, OpenTelemetry, and Kafka in the target architecture.
- Make Milestone 1 **support-first**, not AI-first or cluster-first.
- Support both same-server and separate monitoring-server deployments.
- Use Docker Compose as the primary deployment model.

The first pipeline must ingest **both** telemetry sources:

- **Existing `SystemTrace_*.log` files** — they already contain API, BP, DQ, SQL, session, operation, and timing details.
- **OpenTelemetry from app code** — we can modify the application to add better correlation, traces, metrics, and structured events.

## What the system should answer

Priorities are ordered. The whole plan is organized around answering these questions fast.

| Rank | Need | What the system should answer |
|---|---|---|
| 1 | Find errors fast | What failed in this customer / time window, and which session, operation, BP, DQ, SQL, or component was involved? |
| 2 | Find slow operations | Which API, business process, data query, SQL statement, or scheduler job is slow? |
| 3 | Spot risky data changes | Are there high-risk updates/deletes, high row counts, missing `WHERE` clauses, or unusual table/entity changes? |
| 4 | Understand one request/session | What happened across the timeline of a `SessionID`, `OperationID`, API call, job, BP, DQ, and SQL execution? |
| 5 | Reduce huge log-file pain | How do we search, retain, archive, and rotate large logs without manually opening massive files? |

## Known environment

- Customer deployments are on-prem.
- Windows service hosting is the current norm.
- SQL Server is the application database.
- Docker is acceptable.
- Online **and** offline installation modes are required.

## Target hardware

- 4+ CPU cores.
- More than 8 GB RAM, 16 GB+ preferred.
- About 1 TB disk.
- Ability to ask customers for more cores/RAM when needed.

## Non-negotiables

- Keep data local by default.
- Use read-only DB access for enrichment.
- Make central forwarding optional and filtered.

::: tip Redaction is handled upstream
Secret and SQL-parameter masking happens in the **application / instrumentation layer** before telemetry reaches this pipeline. The monitoring system stores what it receives and does not perform its own SQL redaction. See [Data & security](/guide/data-security).
:::

## The plan

These pages are the working plan and should be revised as implementation decisions become real code and measured results.

- **[Milestone 1](/guide/milestone-1)** — the first support-ready release.
- **[Architecture](/guide/architecture)** — the dual-source telemetry pipeline.
- **[Deployment & sizing](/guide/deployment-sizing)** — same-server and dedicated-server profiles.
- **[Support workflows](/guide/support-workflows)** — the first dashboards and investigation paths.
- **[Data & security](/guide/data-security)** — residency, enrichment, and forwarding rules.
- **[Roadmap](/guide/roadmap)** — the milestone sequence after Milestone 1.
