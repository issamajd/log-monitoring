-- Safety guard: this script must be edited before it can run. Replace every
-- <<...>> placeholder below, then DELETE these two guard lines. Without this,
-- an unedited run would create a live login whose password is published in
-- this repository.
RAISERROR(N'01-create-readonly-login.sql: replace the <<...>> placeholders, then delete this guard.', 16, 1);
SET NOEXEC ON;

-- 1) Server-level login (any database context):
CREATE LOGIN [mon_session_lookup]
    WITH PASSWORD = N'<<generate a strong password; do NOT commit it>>',
         CHECK_POLICY = ON, DEFAULT_DATABASE = [<<rndfwcore_db>>];
GO

-- 2) In the rndfwcore application database:
USE [<<rndfwcore_db>>];
GO

CREATE USER [mon_session_lookup] FOR LOGIN [mon_session_lookup];

-- Deliberately NO role membership. Not db_datareader. Not db_owner.
-- The account's entire capability is one procedure:
GRANT EXECUTE ON OBJECT::dbo.usp_MonSessionContext TO [mon_session_lookup];

-- OPTIONAL hardening. Ownership chaining means the proc still works, because
-- permission checks on tables referenced by a same-owner proc are skipped
-- entirely (including DENY). ONLY apply this if step 4 of the README's
-- verification passes afterwards -- if CLIENTSESSIONLOG has a different
-- owner, chaining breaks and this line breaks the lookup.
-- DENY SELECT ON SCHEMA::dbo TO [mon_session_lookup];
GO
