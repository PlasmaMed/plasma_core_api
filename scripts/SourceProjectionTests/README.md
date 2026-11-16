Run `dotnet run --project CorePlasma_API/scripts/SourceProjectionTests` for adapter/model checks.

Set `SOURCE_PROJECTION_EMIT_SCHEMA=1` to print EF's SQL Server `GenerateCreateScript` only.
This mode opens no connection and changes no database. Apply that output manually to the
owned empty fixture, then apply `SourceSync/install.sql`, `SourceProjection/install.sql`
and `SourceBusiness/install.sql` as appropriate.

Set `SOURCE_PROJECTION_SQL_TEST_CONNECTION` for actual SQL Server transaction/identity
checks. The runner accepts only a loopback SQL Server and database
`core_source_sync_validation_20261109` with database extended property
`core_source_sync_validation_owner=core-source-sync-validation-v1`.
It never creates/migrates a database and never accepts Production. All inserted rows
are synthetic fixture records; the orchestrator owns disposal of the isolated database.

Checks include source-native receipt sequence separation, lost-ack retry, direct engine
ownership, protected customer aliases, staff phone/no-login policy, source-owned order
workflow, transfer items without stock side effects, and owned tank-engine history IDs.
