# Log Monitoring

Support-first observability for on-prem Windows deployments.

The current plan lives in [`docs/plan/index.html`](docs/plan/index.html). Start there before making implementation decisions.

## Current Direction

Milestone 1 is the Support Triage Foundation:

- Ingest existing `SystemTrace_*.log` files.
- Add OpenTelemetry instrumentation to FWCORE chokepoints.
- Search errors by time, session, operation, BP, DQ, SQL, and component.
- Surface slow API, BP, DQ, SQL, and background service work.
- Redact SQL parameters, passwords, tokens, API keys, and connection-string credentials.
- Use Elasticsearch for searchable events, TimescaleDB for metrics, and Grafana for dashboards and alerts.
- Keep data local by default.

Kafka, clustered deployment, and AI/ML anomaly detection are later roadmap items, not Milestone 1 requirements.

## Plan Pages

- [`docs/plan/milestone-1.html`](docs/plan/milestone-1.html) - first usable release scope
- [`docs/plan/architecture.html`](docs/plan/architecture.html) - dual-source telemetry pipeline
- [`docs/plan/deployment-sizing.html`](docs/plan/deployment-sizing.html) - same-server and dedicated monitoring profiles
- [`docs/plan/support-workflows.html`](docs/plan/support-workflows.html) - dashboards and triage workflow
- [`docs/plan/data-security.html`](docs/plan/data-security.html) - redaction, enrichment, and forwarding rules
- [`docs/plan/roadmap.html`](docs/plan/roadmap.html) - milestone sequence

## Repository Shape

- `docs/plan/` - current planning source of truth
- `docker/` - monitoring stack configuration and provisioning
- `src/Instrumentation/` - .NET OpenTelemetry helper library
- `src/Consumers/` - existing telemetry consumers from the earlier prototype
- `tests/` - component tests and sample outputs

Some implementation files still reflect the earlier prototype. Treat the current `docs/plan/` milestone pages as authoritative when deciding what to keep, simplify, or defer.
