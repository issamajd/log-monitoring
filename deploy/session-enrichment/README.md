# Session enrichment

This optional integration lets Grafana resolve a `session_id` against the monitored application's SQL Server only when a support engineer opens the **Session Lookup** dashboard. The SQL login can execute one stored procedure and has no table or role grants; the procedure is the fixed safe-field allowlist.

The feature is disabled by default.

## Provision and verify

1. **DBA, in the application database:** review and run [`02-create-lookup-proc.sql`](02-create-lookup-proc.sql). Schema-qualify `CLIENTSESSIONLOG` and `USERS` if they are not both in `dbo`. For FW versions before 2.2.0.0, delete the `SYSTEMUSERNAME` and `LOGOUTTIME` rows marked in the script.

2. **DBA:** review and run [`01-create-readonly-login.sql`](01-create-readonly-login.sql), replacing the database placeholder and generating a strong password. Hand the operator the SQL Server host/port, database, username, and password through the normal secret channel.

3. **Operator:** copy the repository's `.env.example` to `.env`, fill in the four `SESSION_ENRICHMENT_MSSQL_*` values, generate a per-deployment `GF_SECURITY_SECRET_KEY`, and set `SESSION_ENRICHMENT=enabled`.

   ```sh
   openssl rand -base64 32
   ```

   If the SQL password contains `$`, single-quote the value in `.env` or escape each `$` as `$$` — Docker Compose interpolates `$VAR` inside unquoted `.env` values and silently mangles the password otherwise. Simplest is a `$`-free password: `openssl rand -base64 32 | tr -d '$'`.

4. **DBA:** verify the grant is actually tight before trusting the integration:

   ```sql
   EXECUTE AS LOGIN = 'mon_session_lookup';
     EXEC dbo.usp_MonSessionContext @SessionId = '<a real recent SessionID>'; -- expect 16 rows
     SELECT TOP 1 * FROM dbo.USERS;                                          -- expect: permission denied
   REVERT;
   ```

   On FW versions before 2.2.0.0, the procedure returns 14 rows after the two version-gated fields are removed. If the optional `DENY SELECT ON SCHEMA::dbo` hardening is enabled, repeat this check afterwards; ownership chaining must remain intact.

5. **Operator:** recreate Grafana and confirm the enabled provisioning file is mounted:

   ```sh
   docker compose up -d grafana
   docker compose exec grafana cat /etc/grafana/provisioning/datasources/session-enrichment.yaml
   ```

   The output must show the `mssql` datasource block, not the disabled `deleteDatasources` stub.

6. **End to end:** copy a real `session_id` from **Triage & Error Search**, choose **Session context (app DB)**, and confirm the session resolves. If a known session returns nothing, confirm retention and database selection first. If those are correct, the stored GUID format may differ from `Guid.ToString()` `"D"`; fix the stored procedure comparison, not the dashboard.

7. **Disable at any time:** set `SESSION_ENRICHMENT=disabled` and run `docker compose up -d grafana`. This deletes the datasource from `grafana-data` and provisions the explanatory stub dashboard. The `SESSION_ENRICHMENT_MSSQL_*` variables are passed to the container unconditionally, so also blank them in `.env` to remove the credential from the container environment, and revoke or disable the SQL login on the customer's SQL Server.

## Access model

Support engineers should have the Grafana **Viewer** role. Viewers cannot use Explore or edit panel SQL. Grafana administrators can use Explore, but the SQL Server `EXECUTE`-only grant still limits the account to `dbo.usp_MonSessionContext`; it cannot select from application tables.

## Repository layout note

Two committed files exist only to be bind-mount targets:

- `docker/grafana/provisioning/datasources/session-enrichment.yaml`
- `docker/grafana/provisioning/dashboards/json/session-lookup.json`

`docker-compose.yml` mounts the selected `docker/grafana/enrichment/<variant>/` file over each of them; a file bind-mount inside the read-only provisioning mount cannot create its own mountpoint, so these targets must exist in the repository. Do not delete them — Grafana fails to start in **both** modes without them. Their content is a copy of the `disabled` variant so that a compose file without the enrichment mounts still gets the safe default.

## Air-gapped sites

No Grafana plugin is required. MSSQL is a core datasource in Grafana 10.2.2, so there is nothing to download.
