-- Pre-flight check for the session-enrichment lookup.
--
-- READ-ONLY: creates nothing, changes nothing. Run this FIRST, before
-- 01-create-readonly-login.sql or 02-create-lookup-proc.sql, to discover what
-- 02 must be edited to. Safe to run with any login that can read the catalog.
--
--   sqlcmd -S <server>[\<instance>] -U <user> -P <password> -d <database> -C \
--          -i 00-preflight-check.sql
--
-- The framework does NOT install these tables in [dbo]. It uses a schema named
-- after the connection (e.g. [master] on an rnds4assets DEV database), so the
-- schema this reports is almost always what 02 needs to be qualified with.
SET NOCOUNT ON;
GO

PRINT '=== 1. Which schema holds the tables? (02 must be qualified with this) ===';
SELECT s.name AS [schema], t.name AS [table]
FROM sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id
WHERE t.name IN ('CLIENTSESSIONLOG', 'USERS')
ORDER BY t.name;
GO

-- Everything below resolves the schema itself, so it works regardless of the
-- login's default schema.
DECLARE @sch sysname = (
    SELECT TOP (1) s.name
    FROM sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id
    WHERE t.name = 'CLIENTSESSIONLOG');

IF @sch IS NULL
BEGIN
    PRINT '!! CLIENTSESSIONLOG not found (or not visible to this login).';
    PRINT '!! Enrichment cannot work against this database as configured.';
    RETURN;
END

PRINT '';
PRINT 'Resolved schema: ' + @sch;

DECLARE @tbl nvarchar(300) = QUOTENAME(@sch) + N'.[CLIENTSESSIONLOG]';
DECLARE @usr nvarchar(300) = QUOTENAME(@sch) + N'.[USERS]';

PRINT '';
PRINT '=== 2. Which fields the allowlist can actually use ===';
PRINT '   Missing SYSTEMUSERNAME/LOGOUTTIME => delete those lines from 02 (FW < 2.2.0.0).';
PRINT '   Missing CREATEDDATE => 02 loses its authoritative "Login (server)" time.';
SELECT c.name AS [column], TYPE_NAME(c.user_type_id) AS [type], c.max_length, c.is_nullable
FROM sys.columns c
WHERE c.object_id = OBJECT_ID(@tbl)
ORDER BY c.column_id;

PRINT '';
PRINT '=== 3. Readable? How much history? (bounds how far back lookups work) ===';
DECLARE @sql nvarchar(max) = N'
SELECT COUNT(*) AS total_rows, MIN(CREATEDDATE) AS oldest, MAX(CREATEDDATE) AS newest
FROM ' + @tbl + N';

SELECT TOP (3) SESSIONID, LEN(SESSIONID) AS id_len, USERNAME, MACHINENAME, CREATEDDATE
FROM ' + @tbl + N' ORDER BY CREATEDDATE DESC;';
EXEC sp_executesql @sql;

PRINT '';
PRINT '=== 4. Does a real telemetry session resolve? (expect 1) ===';
PRINT '   Replace the GUID with one taken from a session_id in Elasticsearch.';
DECLARE @probe nvarchar(50) = N'527deb57-3127-48a2-b152-4727ce9d85e9';
SET @sql = N'SELECT COUNT(*) AS matches FROM ' + @tbl +
           N' WHERE SESSIONID IN (LOWER(@p), UPPER(@p));';
EXEC sp_executesql @sql, N'@p nvarchar(50)', @p = @probe;

PRINT '';
PRINT '=== 5. Is the USERS join reachable from this login? ===';
PRINT '   If this errors, set 02 to skip the join (loses Display name / Company only).';
SET @sql = N'
SELECT TOP (1) s.SESSIONID, s.USERNAME, u.FIRSTNAME, u.LASTNAME, u.COMPANY
FROM ' + @tbl + N' s LEFT JOIN ' + @usr + N' u ON u.USERNAME = s.USERNAME
ORDER BY s.CREATEDDATE DESC;';
EXEC sp_executesql @sql;
GO
