-- Run offline as the deployment owner. No schema DDL or permission grants occur in request handlers.
SET NOCOUNT ON;
SET XACT_ABORT ON;
IF DB_NAME() <> N'core_plasma' AND NOT (
    DB_NAME() = N'core_source_sync_validation_20261109' AND EXISTS (
        SELECT 1 FROM sys.extended_properties WHERE class = 0 AND name = N'core_source_sync_validation_owner'
        AND CONVERT(nvarchar(128), value) = N'core-source-sync-validation-v1'))
    THROW 51100, 'SourceBusiness install target is not an explicitly owned Core database.', 1;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.CoreBusinessRecords', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CoreBusinessRecords (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_CoreBusinessRecords PRIMARY KEY,
        Source nvarchar(16) NOT NULL, SourceTable nvarchar(128) NOT NULL, Domain nvarchar(40) NOT NULL,
        InstallationId uniqueidentifier NOT NULL, Generation uniqueidentifier NOT NULL,
        SourceKeyHash nvarchar(64) NOT NULL, ExternalKeyJson nvarchar(max) NOT NULL,
        Name nvarchar(500) NOT NULL, SourceStatus nvarchar(100) NULL, SnapshotJson nvarchar(max) NOT NULL,
        CanonicalEntityId uniqueidentifier NULL, LastRawEventId nvarchar(20) NULL, SnapshotHash nvarchar(64) NOT NULL,
        LastSeenRunId uniqueidentifier NOT NULL,
        CreatedDate datetime2 NOT NULL, CreatedBy uniqueidentifier NULL,
        UpdatedDate datetime2 NULL, UpdatedBy uniqueidentifier NULL,
        IsDeleted bit NOT NULL CONSTRAINT DF_CoreBusinessRecords_IsDeleted DEFAULT 0,
        CONSTRAINT CK_CoreBusinessRecords_Source CHECK (Source IN (N'quanly', N'newapp')),
        CONSTRAINT CK_CoreBusinessRecords_Json CHECK (ISJSON(SnapshotJson) = 1 AND ISJSON(ExternalKeyJson) = 1)
    );
    CREATE UNIQUE INDEX IX_CoreBusinessRecords_Source_SourceTable_SourceKeyHash ON dbo.CoreBusinessRecords(Source, SourceTable, SourceKeyHash);
    CREATE INDEX IX_CoreBusinessRecords_Source_SourceTable_IsDeleted ON dbo.CoreBusinessRecords(Source, SourceTable, IsDeleted);
    CREATE INDEX IX_CoreBusinessRecords_IsDeleted ON dbo.CoreBusinessRecords(IsDeleted);
END;
IF OBJECT_ID(N'dbo.CoreBusinessProjectionStates', N'U') IS NULL
    CREATE TABLE dbo.CoreBusinessProjectionStates (
        Source nvarchar(16) NOT NULL CONSTRAINT PK_CoreBusinessProjectionStates PRIMARY KEY,
        InstallationId uniqueidentifier NOT NULL, Generation uniqueidentifier NOT NULL,
        CatalogHash nvarchar(64) NOT NULL, CompletedAt datetime2 NOT NULL
    );
-- Seed definitions only. Super Admin uses the existing bypass; staff receive no automatic grants.
IF OBJECT_ID(N'dbo.Permissions', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.Permissions WHERE PermissionCode = N'SOURCE_BUSINESS_VIEW' AND IsDeleted = 0)
        INSERT dbo.Permissions (Id, PermissionCode, PermissionName, ModuleCode, FunctionCode, ActionCode, SortOrder, IsActive, CreatedDate, IsDeleted)
        VALUES (NEWID(), N'SOURCE_BUSINESS_VIEW', N'Xem nghiệp vụ liên hệ thống', N'SYSTEM', N'SOURCE_BUSINESS', N'VIEW', 1001, 1, SYSUTCDATETIME(), 0);
    IF NOT EXISTS (SELECT 1 FROM dbo.Permissions WHERE PermissionCode = N'MENU_SOURCE_BUSINESS' AND IsDeleted = 0)
        INSERT dbo.Permissions (Id, PermissionCode, PermissionName, ModuleCode, FunctionCode, ActionCode, SortOrder, IsActive, CreatedDate, IsDeleted)
        VALUES (NEWID(), N'MENU_SOURCE_BUSINESS', N'Xem menu Nghiệp vụ liên hệ thống', N'MENU', N'MENU_SOURCE_BUSINESS', N'VIEW', 1002, 1, SYSUTCDATETIME(), 0);
END;
COMMIT;
