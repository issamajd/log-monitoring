# SystemTrace Secret Redaction Design

## Context

The baseline `SystemTrace_*.log` parser extracts common columns and trace key/value fields. It currently preserves every value unchanged, including passwords or tokens that may appear in connection strings, messages, data, or trace fields. Its integration-style test also depends on a large local `.log` file ignored by Git, so the test is not portable to a clean checkout.

Support staff need the SQL text and customer values for diagnosis and already have database access. Redaction therefore targets access secrets, not business data.

## Goals

- Mask passwords, tokens, API keys, client secrets, and equivalent access-secret fields before a parsed event leaves the parser.
- Preserve SQL text, SQL parameter values, server names, database names, user names, timings, row counts, and other diagnostic content.
- Apply the same masking rule to known event properties and arbitrary `TraceInfo` entries.
- Replace the ignored real-log test dependency with a small sanitized fixture tracked in Git.
- Keep the parser API and existing event model stable.

## Redaction Rules

The replacement marker is `[REDACTED]`.

1. If a parsed field name is itself sensitive, replace its whole value. The initial sensitive-name set is `password`, `pwd`, `token`, `access token`, `refresh token`, `id token`, `api key`, `client secret`, and `authorization`. Matching is case-insensitive and ignores spaces, hyphens, and underscores.
2. In otherwise useful text, replace only values assigned to sensitive names. For example, `Server=db01;User Id=app;Password=secret` becomes `Server=db01;User Id=app;Password=[REDACTED]`.
3. Mask bearer-token values in authorization text while retaining the surrounding label.
4. Do not alter SQL literals, SQL parameters, customer values, server/database names, user identifiers, hashes that are not labeled as secrets, or unrelated text.
5. Apply redaction at the parser boundary, before assigning `Message`, `Data`, or `TraceInfo`, so downstream storage cannot accidentally receive the original access secret.

## Components And Data Flow

Add a focused internal redaction component in the parser project. `SystemTraceLineParser` continues to split and classify each field, then passes the parsed key and value through the redactor before storing it on `SystemTraceEvent`.

The redactor will use deterministic, case-insensitive patterns for direct secret fields, embedded key/value assignments, and bearer authorization values. It will preserve field names and non-secret text so support searches remain useful. Redaction is best-effort and must not make an otherwise parseable line fail.

## Portable Fixture

Add a small sanitized text fixture under the parser test project and copy it to the test output directory. Tests will locate it from `AppContext.BaseDirectory`, making them independent of the repository working directory and of ignored production log files.

The fixture will include representative timing, SQL, row-count, slash-containing, connection-string, and secret-field examples without real customer data.

## Testing

Use the existing console test harness and implement behavior test-first.

- Prove a connection-string password is masked while server, database, and user name remain visible.
- Prove direct token/API-key fields and bearer tokens are masked.
- Prove SQL text, literals, and parameter/customer values remain unchanged.
- Prove the tracked fixture parses from the build output and retains representative parsed fields.
- Run the parser tests and the complete solution build after implementation.

## Out Of Scope

- Extracting SQL operation, table names, danger flags, or normalized duration fields.
- Building the Windows file-tail service.
- Changing Elasticsearch or Grafana access controls.
- Removing all customer data from logs.
