# Log Monitoring

Support-first observability for on-prem Windows deployments.

The current plan lives in the VitePress docs site under [`site/`](site/). Run `npm install` then `npm run docs:dev` and open http://localhost:5173. Start there before making implementation decisions.

## Current Direction

Milestone 1 is the Support Triage Foundation:

- Add OpenTelemetry instrumentation to FWCORE chokepoints — the single telemetry source.
- Keep writing `SystemTrace_*.log` files as a local backup; the pipeline does not ingest them.
- Search errors by time, session, operation, BP, DQ, SQL, and component.
- Surface slow API, BP, DQ, SQL, and background service work.
- Rely on app-level masking of SQL parameters and secrets; the pipeline stores what it receives.
- Use Elasticsearch for searchable events, TimescaleDB for metrics, and Grafana for dashboards and alerts.
- Keep data local by default.

Kafka, clustered deployment, and AI/ML anomaly detection are later roadmap items, not Milestone 1 requirements.

## Plan Pages

The plan is a VitePress site; sources are in [`site/guide/`](site/guide/).

- [`site/guide/milestone-1.md`](site/guide/milestone-1.md) - first usable release scope
- [`site/guide/architecture.md`](site/guide/architecture.md) - dual-source telemetry pipeline
- [`site/guide/deployment-sizing.md`](site/guide/deployment-sizing.md) - same-server and dedicated monitoring profiles
- [`site/guide/support-workflows.md`](site/guide/support-workflows.md) - dashboards and triage workflow
- [`site/guide/data-security.md`](site/guide/data-security.md) - residency, enrichment, and forwarding rules
- [`site/guide/roadmap.md`](site/guide/roadmap.md) - milestone sequence

## Repository Shape

- `site/` - current planning source of truth (VitePress docs site)
- `docker/` - monitoring stack configuration and provisioning
- `src/Instrumentation/` - .NET OpenTelemetry helper library
- `src/Consumers/` - existing telemetry consumers from the earlier prototype
- `tests/` - component tests and sample outputs

Some implementation files still reflect the earlier prototype. Treat the current `site/` milestone pages as authoritative when deciding what to keep, simplify, or defer.
