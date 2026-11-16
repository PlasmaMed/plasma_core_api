-- OFFLINE ONLY. Run after SourceSync/install.sql in the verified SQL Server Core database.
-- No runtime DDL, destructive migration or source-database writes.
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;
IF DB_NAME() <> N'core_plasma' AND NOT (DB_NAME()=N'core_source_sync_validation_20261109' AND EXISTS
 (SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'core_source_sync_validation_owner'
  AND CONVERT(nvarchar(100),value)=N'core-source-sync-validation-v1'))
THROW 51000, 'Refusing native projection DDL outside the configured Core/owned fixture.', 1;
BEGIN TRANSACTION;
IF SCHEMA_ID(N'source_sync') IS NULL THROW 51000, 'Install the owned SourceSync schema first.', 1;
IF OBJECT_ID(N'source_sync.NativeProjection',N'U') IS NULL
CREATE TABLE source_sync.NativeProjection (
    Source nvarchar(40) COLLATE Latin1_General_100_BIN2 NOT NULL,
    InstallationId uniqueidentifier NOT NULL,
    Generation uniqueidentifier NOT NULL,
    Entity nvarchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
    EntityId bigint NOT NULL,
    CoreId uniqueidentifier NULL,
    SnapshotHash varchar(64) NOT NULL,
    State varchar(16) NOT NULL CHECK (State IN ('applied','blocked','unsupported','protected')),
    ErrorCode varchar(80) NULL,
    Revision varchar(100) NOT NULL,
    UpdatedAt datetime2 NOT NULL,
    CONSTRAINT PK_SourceNativeProjection PRIMARY KEY (Source,InstallationId,Generation,Entity,EntityId)
);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'source_sync.NativeProjection') AND name=N'IX_SourceNativeProjection_State')
CREATE INDEX IX_SourceNativeProjection_State ON source_sync.NativeProjection(Source,State,Entity);

-- Source-native bindings are independent of PLT ObjectIds and order numbers.
IF OBJECT_ID(N'source_sync.NativeBindings',N'U') IS NULL
CREATE TABLE source_sync.NativeBindings (
    Source nvarchar(40) COLLATE Latin1_General_100_BIN2 NOT NULL,
    InstallationId uniqueidentifier NOT NULL,
    Entity nvarchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
    EntityId bigint NOT NULL,
    CoreId uniqueidentifier NOT NULL,
    CreatedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_SourceNativeBindings PRIMARY KEY(Source,InstallationId,Entity,EntityId),
    CONSTRAINT UQ_SourceNativeBindings_Core UNIQUE(Source,InstallationId,Entity,CoreId)
);

IF COL_LENGTH(N'dbo.Orders',N'SourceManaged') IS NULL
ALTER TABLE dbo.Orders ADD SourceManaged bit NOT NULL CONSTRAINT DF_Orders_SourceManaged DEFAULT 0,
 SourceSystem nvarchar(100) NULL, SourceRecordId nvarchar(100) NULL,
 SourceStatusCode nvarchar(100) NULL, SourceStatusLabel nvarchar(250) NULL, SourceDetailsJson nvarchar(max) NULL;
IF COL_LENGTH(N'dbo.TankTransfers',N'SourceManaged') IS NULL
ALTER TABLE dbo.TankTransfers ADD SourceManaged bit NOT NULL CONSTRAINT DF_TankTransfers_SourceManaged DEFAULT 0,
 SourceSystem nvarchar(100) NULL, SourceRecordId nvarchar(100) NULL,
 SourceStatusCode nvarchar(100) NULL, SourceStatusLabel nvarchar(250) NULL, SourceDetailsJson nvarchar(max) NULL;
-- Source phone profiles can contain extensions/multiple numbers; preserve them without truncation.
IF COL_LENGTH(N'dbo.Employees',N'Phone') < 200
ALTER TABLE dbo.Employees ALTER COLUMN Phone nvarchar(100) NULL;
-- Source-owned Quanly transfer notes exceed the historical 500/1000-character
-- voucher widths. Widen only the known nullable, unindexed nvarchar column.
IF OBJECT_ID(N'dbo.TankTransfers',N'U') IS NOT NULL
BEGIN
 DECLARE @transferNoteColumn int,@transferNoteBytes smallint,@transferNoteNullable bit,@transferNoteType sysname,@transferNoteCollation sysname;
 SELECT @transferNoteColumn=column_id,@transferNoteBytes=max_length,@transferNoteNullable=is_nullable,
  @transferNoteType=TYPE_NAME(user_type_id),@transferNoteCollation=collation_name
 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.TankTransfers',N'U') AND name=N'Note' AND is_computed=0;
 IF @transferNoteColumn IS NULL OR @transferNoteType<>N'nvarchar' OR @transferNoteBytes NOT IN(1000,2000,-1) OR @transferNoteNullable<>1
  OR @transferNoteCollation IS NULL OR @transferNoteCollation COLLATE Latin1_General_100_BIN2 LIKE N'%[^A-Za-z0-9_]%'
  THROW 51000,'Unexpected transfer Note definition; reviewed width-only migration required.',1;
 IF EXISTS(SELECT 1 FROM sys.index_columns WHERE object_id=OBJECT_ID(N'dbo.TankTransfers',N'U') AND column_id=@transferNoteColumn)
  THROW 51000,'Indexed transfer Note requires a separately reviewed migration.',1;
 IF @transferNoteBytes<>-1
 BEGIN
  DECLARE @widenTransferNote nvarchar(max)=N'ALTER TABLE dbo.TankTransfers ALTER COLUMN Note nvarchar(max) COLLATE '+@transferNoteCollation+N' NULL;';
  EXEC sys.sp_executesql @widenTransferNote;
 END;
END;
COMMIT;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.Orders') AND name=N'IX_Orders_SourceSystem_SourceRecordId')
CREATE UNIQUE INDEX IX_Orders_SourceSystem_SourceRecordId ON dbo.Orders(SourceSystem,SourceRecordId) WHERE SourceRecordId IS NOT NULL;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.TankTransfers') AND name=N'IX_TankTransfers_SourceSystem_SourceRecordId')
CREATE UNIQUE INDEX IX_TankTransfers_SourceSystem_SourceRecordId ON dbo.TankTransfers(SourceSystem,SourceRecordId) WHERE SourceRecordId IS NOT NULL;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.LegacySyncMappings') AND name=N'IX_LegacySyncMappings_Source_LastSequence')
CREATE INDEX IX_LegacySyncMappings_Source_LastSequence ON dbo.LegacySyncMappings(Source,LastSequence);
