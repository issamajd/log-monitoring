-- Session enrichment lookup for the log-monitoring stack.
-- Read-only, single-row, primary-key-seek. Returns a fixed allowlist of
-- support-safe fields as key/value rows.
--
-- EDIT BEFORE RUNNING if this deployment differs:
--   * schema-qualify CLIENTSESSIONLOG / USERS if they are not both in dbo
--   * delete the SYSTEMUSERNAME and LOGOUTTIME lines on FW < 2.2.0.0
CREATE OR ALTER PROCEDURE dbo.usp_MonSessionContext
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
    DECLARE @lo nvarchar(50) = LOWER(CONVERT(nvarchar(50), @g));
    DECLARE @up nvarchar(50) = UPPER(CONVERT(nvarchar(50), @g));

    -- A valid GUID with no session row must still return a row: Grafana's
    -- table panel only shows its "noValue" text for a result with zero
    -- columns, so an empty resultset would render as a bare header row
    -- with no explanation.
    IF NOT EXISTS (SELECT 1 FROM dbo.CLIENTSESSIONLOG WHERE SESSIONID IN (@lo, @up))
    BEGIN
        SELECT 1 AS Ord, N'Session ID' AS [Field],
               N'No session row found (check retention, database, or stored GUID format)' AS [Value];
        RETURN;
    END

    SELECT v.Ord, v.[Field], v.[Value]
    FROM (
        SELECT TOP (1) * FROM dbo.CLIENTSESSIONLOG WHERE SESSIONID IN (@lo, @up)
    ) AS s
    LEFT JOIN dbo.USERS AS u ON u.USERNAME = s.USERNAME
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
        (10, N'OS user',              CONVERT(nvarchar(100), s.SYSTEMUSERNAME)),   -- FW >= 2.2.0.0
        (11, N'Client IP',            CONVERT(nvarchar(100), s.IPADDRESS)),
        (12, N'Client time zone',     CONVERT(nvarchar(100), s.TIMEZONENAME)),
        (13, N'Login (server)',       CONVERT(nvarchar(30),  s.CREATEDDATE, 126)),
        (14, N'Login (client clock)', CONVERT(nvarchar(30),  s.LOCALTIME, 126)),
        (15, N'Logout',               CONVERT(nvarchar(30),  s.LOGOUTTIME, 126)),  -- FW >= 2.2.0.0
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
