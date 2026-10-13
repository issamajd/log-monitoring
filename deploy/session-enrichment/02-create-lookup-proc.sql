-- Session enrichment lookup for the log-monitoring stack.
-- Read-only, single-row, primary-key-seek. Returns a fixed allowlist of
-- support-safe fields as key/value rows.
--
-- RUN 00-preflight-check.sql FIRST. It prints the two things this script
-- cannot assume, because they vary per deployment:
--
--   1. THE SCHEMA. The framework does NOT install these tables in [dbo]; it
--      uses a schema named after the connection. Replace every <<schema>>
--      below with what preflight section 1 reported (e.g. master).
--
--   2. WHICH COLUMNS EXIST. Preflight section 2 lists them. The lines marked
--      "OPTIONAL" reference columns that are absent on some deployments --
--      delete any whose column preflight did not list, or CREATE PROCEDURE
--      fails with "Invalid column name".
--
-- Verified 2026-07-27 against an rnds4assets DEV database: schema [master];
-- SYSTEMUSERNAME and LOGOUTTIME present; LOCALTIME absent.
CREATE OR ALTER PROCEDURE <<schema>>.usp_MonSessionContext
    @SessionId nvarchar(64)
AS
BEGIN
    SET NOCOUNT ON;
    -- Never block or be blocked by the live application.
    SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

    -- Cost guard: a blank textbox or any non-GUID input costs zero reads.
    DECLARE @g uniqueidentifier = TRY_CONVERT(uniqueidentifier, @SessionId);
    IF @g IS NULL RETURN;

    -- Sargable on both casings; no function applied to the indexed column,
    -- so this stays a PK seek even on a case-sensitive collation.
    -- Widths match SESSIONID nvarchar(100); a canonical GUID is 36 chars.
    DECLARE @lo nvarchar(100) = LOWER(CONVERT(nvarchar(100), @g));
    DECLARE @up nvarchar(100) = UPPER(CONVERT(nvarchar(100), @g));

    -- A valid GUID with no session row must still return a row: Grafana's
    -- table panel only shows its "noValue" text for a result with zero
    -- columns, so an empty resultset would render as a bare header row
    -- with no explanation. (noValue also fills NULL cells, so the panel
    -- maps NULL to a dash: a found session with an empty field must not
    -- read as "not found".)
    IF NOT EXISTS (SELECT 1 FROM <<schema>>.CLIENTSESSIONLOG WHERE SESSIONID IN (@lo, @up))
    BEGIN
        SELECT 1 AS Ord, N'Session ID' AS [Field],
               N'No session row found (check retention, database, or stored GUID format)' AS [Value];
        RETURN;
    END

    SELECT v.Ord, v.[Field], v.[Value]
    FROM (
        SELECT TOP (1) * FROM <<schema>>.CLIENTSESSIONLOG WHERE SESSIONID IN (@lo, @up)
    ) AS s
    LEFT JOIN <<schema>>.USERS AS u ON u.USERNAME = s.USERNAME
    CROSS APPLY (VALUES
        ( 1, N'Session ID',           CONVERT(nvarchar(100), s.SESSIONID)),
        ( 2, N'User name',            CONVERT(nvarchar(100), s.USERNAME)),
        ( 3, N'Display name',         NULLIF(LTRIM(RTRIM(CONCAT(u.FIRSTNAME, N' ', u.LASTNAME))), N'')),
        ( 4, N'Company',              CONVERT(nvarchar(100), u.COMPANY)),
        ( 5, N'Role',                 CONVERT(nvarchar(100), s.ROLENAME)),
        ( 6, N'Profile',              CONVERT(nvarchar(100), s.PROFILENAME)),
        ( 7, N'Application',          CONVERT(nvarchar(100), s.APPLICATIONID)),
        ( 8, N'Client machine',       CONVERT(nvarchar(100), s.MACHINENAME)),
        ( 9, N'Windows domain',       CONVERT(nvarchar(100), s.USERDOMAINNAME)),
        (10, N'OS user',              CONVERT(nvarchar(100), s.SYSTEMUSERNAME)),   -- OPTIONAL: FW >= 2.2.0.0
        (11, N'Client IP',            CONVERT(nvarchar(100), s.IPADDRESS)),
        (12, N'Client time zone',     CONVERT(nvarchar(100), s.TIMEZONENAME)),
        (13, N'Login (server)',       CONVERT(nvarchar(30),  s.CREATEDDATE, 126)),
     -- (14, N'Login (client clock)', CONVERT(nvarchar(30),  s.LOCALTIME, 126)),   -- OPTIONAL: absent on rnds4assets DEV
        (15, N'Logout',               CONVERT(nvarchar(30),  s.LOGOUTTIME, 126)),  -- OPTIONAL: FW >= 2.2.0.0
        (16, N'Server instance',      CONVERT(nvarchar(100), s.SERVERINSTANCEID))
        -- ---------------------------------------------------------------
        -- DELIBERATELY NOT EXPOSED. Adding a line here is a data-policy
        -- decision by the customer's data owner, not a support convenience:
        --   USERS.PASSWORD                     -- secret, never
        --   USERS.EMAIL / TEL / EMPLOYEENO     -- PII
        --   USERS.NOTES                        -- free text, unreviewable
        -- ---------------------------------------------------------------
    ) AS v(Ord, [Field], [Value])
    ORDER BY v.Ord;
END
GO
