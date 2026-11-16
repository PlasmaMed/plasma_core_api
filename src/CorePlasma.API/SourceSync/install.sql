-- Run explicitly as a schema administrator against the reviewed Core SQL database.
-- Runtime does not execute this script. No dbo object or native data is changed.
IF DB_NAME()<>N'core_plasma' AND NOT(DB_NAME()=N'core_source_sync_validation_20261109' AND EXISTS(
 SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'core_source_sync_validation_owner'
 AND CONVERT(nvarchar(128),value)=N'core-source-sync-validation-v1'))
 THROW 51000,'Source sync installer requires Production core_plasma or the exact owned validation database.',1;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
DECLARE @lock int;
EXEC @lock=sys.sp_getapplock @Resource=N'core-source-sync-install-v1',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=10000;
IF @lock<0 THROW 51000,'Source sync installation lock unavailable.',1;
IF SCHEMA_ID(N'source_sync') IS NOT NULL AND NOT EXISTS
 (SELECT 1 FROM sys.extended_properties WHERE class=3 AND major_id=SCHEMA_ID(N'source_sync') AND name=N'core_source_sync_owner' AND CONVERT(nvarchar(128),value)=N'core-source-sync-v1')
 THROW 51000,'Refusing to adopt an unowned source_sync schema.',1;
IF SCHEMA_ID(N'source_sync') IS NULL
BEGIN
 EXEC(N'CREATE SCHEMA source_sync AUTHORIZATION dbo');
 EXEC sys.sp_addextendedproperty @name=N'core_source_sync_owner',@value=N'core-source-sync-v1',@level0type=N'SCHEMA',@level0name=N'source_sync';
END;
IF OBJECT_ID(N'source_sync.sources',N'U') IS NULL
BEGIN
 CREATE TABLE source_sync.sources(
  source_id nvarchar(16) COLLATE Latin1_General_100_BIN2 NOT NULL PRIMARY KEY,
  installation_id uniqueidentifier NOT NULL,generation uniqueidentifier NOT NULL,
  snapshot nvarchar(max) NOT NULL,begin_hash binary(32) NOT NULL,
  state varchar(16) NOT NULL CHECK(state IN('seeding','finalizing','ready')),raw_error varchar(80) NULL,
  projection_state varchar(16) NOT NULL DEFAULT 'pending',projection_error varchar(80) NULL,
  updated_at datetime2(7) NOT NULL DEFAULT SYSUTCDATETIME());
 CREATE TABLE source_sync.tables(
  source_id nvarchar(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
  table_name nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
  metadata nvarchar(max) NOT NULL,object_id int NOT NULL,row_count bigint NOT NULL DEFAULT 0,
  state varchar(16) NOT NULL DEFAULT 'seeding' CHECK(state IN('seeding','indexing','ready')),
  PRIMARY KEY(source_id,table_name),FOREIGN KEY(source_id) REFERENCES source_sync.sources(source_id));
 CREATE TABLE source_sync.chunks(
  source_id nvarchar(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
  generation uniqueidentifier NOT NULL,chunk_id uniqueidentifier NOT NULL,
  table_name nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
  payload_hash binary(32) NOT NULL,row_count bigint NOT NULL,digest_sum binary(32) NOT NULL,digest_xor binary(32) NOT NULL,
  PRIMARY KEY(source_id,generation,chunk_id),FOREIGN KEY(source_id) REFERENCES source_sync.sources(source_id));
 CREATE TABLE source_sync.events(
  source_id nvarchar(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
  installation_id uniqueidentifier NOT NULL,event_id bigint NOT NULL,generation uniqueidentifier NOT NULL,
  transaction_id bigint NOT NULL,table_name nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
  payload_hash binary(32) NOT NULL,payload nvarchar(max) NOT NULL,
  projection_state varchar(16) NOT NULL DEFAULT 'pending',projection_error varchar(80) NULL,
  received_at datetime2(7) NOT NULL DEFAULT SYSUTCDATETIME(),
  PRIMARY KEY(source_id,installation_id,event_id),FOREIGN KEY(source_id) REFERENCES source_sync.sources(source_id));
 CREATE INDEX IX_source_sync_projection ON source_sync.events(source_id,projection_state,event_id);
 CREATE TABLE source_sync.batches(
  source_id nvarchar(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
  installation_id uniqueidentifier NOT NULL,batch_id uniqueidentifier NOT NULL,
  generation uniqueidentifier NOT NULL,payload_hash binary(32) NOT NULL,
  PRIMARY KEY(source_id,installation_id,batch_id),FOREIGN KEY(source_id) REFERENCES source_sync.sources(source_id));
 CREATE TABLE source_sync.event_jobs(
  job_id bigint IDENTITY NOT NULL UNIQUE,source_id nvarchar(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
  installation_id uniqueidentifier NOT NULL,batch_id uniqueidentifier NOT NULL,generation uniqueidentifier NOT NULL,
  state varchar(16) NOT NULL DEFAULT 'receiving' CHECK(state IN('receiving','queued','applied','failed')),
  part_count int NULL,event_count bigint NULL,payload_hash binary(32) NULL,error_code varchar(80) NULL,
  updated_at datetime2(7) NOT NULL DEFAULT SYSUTCDATETIME(),
  PRIMARY KEY(source_id,installation_id,batch_id),FOREIGN KEY(source_id) REFERENCES source_sync.sources(source_id));
 CREATE TABLE source_sync.event_parts(
  source_id nvarchar(16) COLLATE Latin1_General_100_BIN2 NOT NULL,installation_id uniqueidentifier NOT NULL,
  batch_id uniqueidentifier NOT NULL,part_index int NOT NULL,payload_hash binary(32) NOT NULL,
  event_count int NOT NULL,first_event_id bigint NOT NULL,last_event_id bigint NOT NULL,payload nvarchar(max) NOT NULL,
  PRIMARY KEY(source_id,installation_id,batch_id,part_index),
  FOREIGN KEY(source_id,installation_id,batch_id) REFERENCES source_sync.event_jobs(source_id,installation_id,batch_id));
END;
IF SCHEMA_ID(N'source_quanly') IS NOT NULL AND NOT EXISTS
 (SELECT 1 FROM sys.extended_properties WHERE class=3 AND major_id=SCHEMA_ID(N'source_quanly') AND name=N'core_source_sync_owner' AND CONVERT(nvarchar(128),value)=N'core-source-sync-v1')
 THROW 51000,'Refusing to adopt an unowned source_quanly schema.',1;
IF SCHEMA_ID(N'source_newapp') IS NOT NULL AND NOT EXISTS
 (SELECT 1 FROM sys.extended_properties WHERE class=3 AND major_id=SCHEMA_ID(N'source_newapp') AND name=N'core_source_sync_owner' AND CONVERT(nvarchar(128),value)=N'core-source-sync-v1')
 THROW 51000,'Refusing to adopt an unowned source_newapp schema.',1;
IF SCHEMA_ID(N'source_quanly') IS NULL
BEGIN
 EXEC(N'CREATE SCHEMA source_quanly AUTHORIZATION dbo');
 EXEC sys.sp_addextendedproperty @name=N'core_source_sync_owner',@value=N'core-source-sync-v1',@level0type=N'SCHEMA',@level0name=N'source_quanly';
END;
IF SCHEMA_ID(N'source_newapp') IS NULL
BEGIN
 EXEC(N'CREATE SCHEMA source_newapp AUTHORIZATION dbo');
 EXEC sys.sp_addextendedproperty @name=N'core_source_sync_owner',@value=N'core-source-sync-v1',@level0type=N'SCHEMA',@level0name=N'source_newapp';
END;
IF DATABASE_PRINCIPAL_ID(N'core_plasma_api_test') IS NOT NULL
BEGIN
 DENY SELECT,INSERT,UPDATE,DELETE,ALTER ON SCHEMA::source_quanly TO core_plasma_api_test;
 DENY SELECT,INSERT,UPDATE,DELETE,ALTER ON SCHEMA::source_newapp TO core_plasma_api_test;
 -- Keep NativeProjection / NativeBindings accessible to their native adapters.
 DENY SELECT,INSERT,UPDATE,DELETE,ALTER ON OBJECT::source_sync.sources TO core_plasma_api_test;
 DENY SELECT,INSERT,UPDATE,DELETE,ALTER ON OBJECT::source_sync.tables TO core_plasma_api_test;
 DENY SELECT,INSERT,UPDATE,DELETE,ALTER ON OBJECT::source_sync.chunks TO core_plasma_api_test;
 DENY SELECT,INSERT,UPDATE,DELETE,ALTER ON OBJECT::source_sync.events TO core_plasma_api_test;
 DENY SELECT,INSERT,UPDATE,DELETE,ALTER ON OBJECT::source_sync.batches TO core_plasma_api_test;
 DENY SELECT,INSERT,UPDATE,DELETE,ALTER ON OBJECT::source_sync.event_jobs TO core_plasma_api_test;
 DENY SELECT,INSERT,UPDATE,DELETE,ALTER ON OBJECT::source_sync.event_parts TO core_plasma_api_test;
END;
COMMIT;
-- Provision a dedicated runtime principal separately: CONNECT, CREATE TABLE,
-- SELECT/INSERT/UPDATE/DELETE on source_sync and the two fixed raw schemas,
-- ALTER only on source_quanly/source_newapp; explicitly DENY all dbo data/ALTER
-- to that dedicated principal. Deny native API roles
-- access to raw source schemas, which can contain private source account data.
