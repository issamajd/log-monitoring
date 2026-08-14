---
title: Roadmap
---

# Keep the north star, shrink the next step

The old plan is useful as a long-term architecture, but the build order now centers on support triage, current logs, OpenTelemetry instrumentation, and practical on-prem deployment.

## Milestone sequence

| Stage | Name | Outcome |
|---|---|---|
| **Milestone 1** | Support Triage Foundation | OpenTelemetry instrumentation, single-broker Kafka buffer, enrichment, Elasticsearch, TimescaleDB, Grafana dashboards, server health, and basic alerts. |
| **Milestone 2** | Pipeline Reliability | Consumer scaling, DLQ replay tooling (dead-letter routing itself landed in Milestone 1), backpressure, and ingestion reliability views on top of the Milestone 1 Kafka pipeline. |
| **Milestone 3** | Log Intelligence | Richer event processing, deduplication, session/request timelines, pattern grouping, and stronger risky-SQL detection. |
| **Milestone 4** | Operational Hardening | Offline-bundle polish, backup/restore, upgrade process, role-based access, retention validation, and production runbooks. |
| **Milestone 5** | Proactive Alerting | Trend, absence, rate-of-change, and correlation alerts once baseline data is trustworthy. |
| **Milestone 6** | AI/ML Insights | Anomaly detection, forecasting, and model monitoring once data quality and operational usage are proven. |

## Keep from the original plan

- OpenTelemetry Collector.
- Kafka as the durable stream layer.
- TimescaleDB for metrics.
- Elasticsearch for log search.
- Grafana dashboards and alerting.
- Phased delivery.
- Future log-intelligence and ML ideas.

## Change from the original plan

- All telemetry comes from OpenTelemetry instrumentation; `SystemTrace_*.log` files stay as a local backup and are not ingested.
- Support team is the first user, not platform administrators.
- Same-server Docker deployment is a first-class profile.
- Clustered deployment and HA are later.
- AI/ML waits until the base data is trusted.
- Read-only session enrichment is an early requirement.

## Open questions to resolve during implementation

- Exact daily log volume per typical customer.
- Exact offline-installer packaging method.
- Threshold defaults after testing with real customer logs.

Resolved so far: the session lookup query and safe-field allowlist (the `usp_MonSessionContext` procedure, live-verified — see `deploy/session-enrichment/README.md`) and the Elasticsearch index mappings/ILM (finalized in the `elasticsearch-init` templates — see [Architecture](./architecture)).
