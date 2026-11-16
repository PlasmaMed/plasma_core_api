-- OFFLINE ONLY: install in the verified Core SQL Server database before enabling QuanlyAccountImport.
-- Contains ownership/checkpoint metadata only; never source usernames, hashes, salts or passwords.
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
 THROW 51000, 'Refusing Quanly account DDL outside the configured Core/owned fixture.', 1;
IF SCHEMA_ID(N'source_sync') IS NULL THROW 51000, 'Install the owned SourceSync schema first.', 1;
BEGIN TRANSACTION;
IF OBJECT_ID(N'source_sync.QuanlyAccounts',N'U') IS NULL
CREATE TABLE source_sync.QuanlyAccounts (
 InstallationId uniqueidentifier NOT NULL,
 SourceUserId bigint NOT NULL CHECK (SourceUserId>0),
 AccountKind varchar(8) NULL CHECK (AccountKind IN ('Employee','Customer')),
 AccountId uniqueidentifier NULL,
 NativeId uniqueidentifier NULL,
 Fingerprint varchar(64) NOT NULL,
 State varchar(16) NOT NULL CHECK (State IN ('created','applied','disabled','blocked')),
 Reason varchar(80) NULL,
 CreatedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME(),
 UpdatedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME(),
 CONSTRAINT PK_QuanlyAccounts PRIMARY KEY (InstallationId,SourceUserId),
 CONSTRAINT CK_QuanlyAccounts_Ownership CHECK
  ((AccountId IS NULL AND NativeId IS NULL AND AccountKind IS NULL) OR
   (AccountId IS NOT NULL AND NativeId IS NOT NULL AND AccountKind IS NOT NULL))
);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'source_sync.QuanlyAccounts') AND name=N'IX_QuanlyAccounts_Account')
CREATE UNIQUE INDEX IX_QuanlyAccounts_Account ON source_sync.QuanlyAccounts(AccountKind,AccountId) WHERE AccountId IS NOT NULL;
-- Reuse only reviewed existing principals. The raw transport role must never own native login bindings.
IF DATABASE_PRINCIPAL_ID(N'core_plasma_api_test') IS NOT NULL
 GRANT SELECT,INSERT,UPDATE ON OBJECT::source_sync.QuanlyAccounts TO core_plasma_api_test;
IF DATABASE_PRINCIPAL_ID(N'core_pg_sql_mirror') IS NOT NULL
 DENY SELECT,INSERT,UPDATE,DELETE ON OBJECT::source_sync.QuanlyAccounts TO core_pg_sql_mirror;
COMMIT;
