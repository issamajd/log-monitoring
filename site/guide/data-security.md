---
title: Data & security
---

# Local-first, with controlled enrichment

The monitoring system will see sensitive operational data. The default must keep data on the customer site, use read-only access for enrichment, and forward nothing unless it is explicitly enabled.

::: warning Redaction happens at the application level
SQL-parameter and secret masking is done in the **application / instrumentation layer**, before telemetry reaches this pipeline. The monitoring system does **not** perform its own SQL redaction — it stores what it receives. This is a deliberate scope decision for the current milestone.
:::

## Session enrichment

- `SessionID` is the primary support correlation key.
- Safe context (session, operation, username, environment) is attached **by the app itself** as OTel attributes before telemetry leaves the process.
- Sensitive or heavy context (customer/entity details) is resolved **on demand** at investigation time via a **read-only** SQL Server connection, and is not stored per-event.
- The safe-field allowlist lives in `dbo.usp_MonSessionContext` in the application database; the monitoring login receives `EXECUTE` on that procedure and no table or role grants.
- `SESSION_ENRICHMENT` selects the enabled or disabled provisioning variant and defaults to `disabled`.
- Per-customer setup and verification are documented in `deploy/session-enrichment/README.md` in the repository.
- The lookup should cache results and avoid heavy queries against the app database.
- A customer can disable DB enrichment if required.
- Only safe support fields are ever copied into monitoring storage.

## Forwarding policy

Default deployment is customer-site only. Optional forwarding exists only for support cases that need it.

| Data class | Default location | Forwarding rule |
|---|---|---|
| Errors and health summaries | Customer site | Can be forwarded when enabled. |
| Slow-operation summaries | Customer site | Can be forwarded as metadata and durations. |
| SQL text | Customer site | Forward only when needed and approved. |
| User / session context | Customer site | Forward only safe identifiers or approved fields. |

## Access and configuration

- Use a read-only SQL Server account for session enrichment.
- Store monitoring credentials outside source control and outside public dashboard exports.
- Separate support/operator access from admin access in Grafana.
- Keep retention and forwarding configurable per customer.
- Log monitoring-system changes such as retention updates, forwarding enablement, and alert changes.

::: info Security posture
The system should help support without becoming a second production database full of secrets. The safe default is **local, read-only, and configurable**.
:::
