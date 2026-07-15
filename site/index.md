---
layout: home

hero:
  name: Log Monitoring
  text: Support-first observability
  tagline: A practical monitoring system built around the logs we already have — so support can find errors, spot slow operations, and understand one session fast, on on-prem Windows deployments.
  actions:
    - theme: brand
      text: Read the overview
      link: /guide/overview
    - theme: alt
      text: Milestone 1 scope
      link: /guide/milestone-1

features:
  - title: Milestone 1
    details: The first usable release — dual-source ingestion, search, slow-operation views, dashboards, and basic alerts. What's in, what's deliberately out.
    link: /guide/milestone-1
  - title: Architecture
    details: The dual-source telemetry pipeline — existing SystemTrace logs plus OpenTelemetry from app code, through the collector into Elasticsearch and TimescaleDB.
    link: /guide/architecture
  - title: Deployment & sizing
    details: Same-server and dedicated monitoring-server profiles, sizing tiers, resource guardrails, and online/offline install.
    link: /guide/deployment-sizing
  - title: Support workflows
    details: The default triage path, the first dashboards, search filters, and the basic alert set.
    link: /guide/support-workflows
  - title: Data & security
    details: Local-first data residency, read-only session enrichment, and controlled forwarding.
    link: /guide/data-security
  - title: Roadmap
    details: The milestone sequence after Milestone 1 — Kafka, log intelligence, hardening, proactive alerting, and ML.
    link: /guide/roadmap
---
