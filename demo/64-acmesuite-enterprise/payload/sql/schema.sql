-- AcmeSuite database schema — run once at install by the SQL extension's deferred custom action.
-- This is a plain example script; adapt it to your real schema.

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'Tenants')
BEGIN
    CREATE TABLE dbo.Tenants
    (
        TenantId      INT IDENTITY(1,1) PRIMARY KEY,
        Name          NVARCHAR(200) NOT NULL,
        CreatedUtc    DATETIME2     NOT NULL CONSTRAINT DF_Tenants_CreatedUtc DEFAULT SYSUTCDATETIME()
    );
END;

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'AuditLog')
BEGIN
    CREATE TABLE dbo.AuditLog
    (
        AuditId       BIGINT IDENTITY(1,1) PRIMARY KEY,
        TenantId      INT           NOT NULL,
        Message       NVARCHAR(400) NOT NULL,
        LoggedUtc     DATETIME2     NOT NULL CONSTRAINT DF_AuditLog_LoggedUtc DEFAULT SYSUTCDATETIME()
    );
END;
