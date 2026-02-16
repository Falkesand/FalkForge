-- Extensions Showcase: Initial database schema
-- Executed on install by the SQL extension.

CREATE TABLE [dbo].[AppSettings] (
    [Id]       INT            IDENTITY(1,1) NOT NULL PRIMARY KEY,
    [Key]      NVARCHAR(256)  NOT NULL,
    [Value]    NVARCHAR(MAX)  NULL,
    [Created]  DATETIME2      NOT NULL DEFAULT GETUTCDATE()
);

CREATE TABLE [dbo].[AuditLog] (
    [Id]        BIGINT         IDENTITY(1,1) NOT NULL PRIMARY KEY,
    [Action]    NVARCHAR(128)  NOT NULL,
    [User]      NVARCHAR(256)  NOT NULL,
    [Timestamp] DATETIME2      NOT NULL DEFAULT GETUTCDATE(),
    [Details]   NVARCHAR(MAX)  NULL
);

INSERT INTO [dbo].[AppSettings] ([Key], [Value])
VALUES ('SchemaVersion', '1.0.0');
