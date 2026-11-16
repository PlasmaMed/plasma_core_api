# Legacy source synchronization checks

See `../../LEGACY_SYNC.md` for the wire contract, projection rules and deployment prerequisites.

Run on an isolated local PostgreSQL server with a role allowed to create a disposable database:

```sh
TEST_PG_CONNECTION='Host=127.0.0.1;Port=5432;Username=local_test_admin;Database=postgres' \
  dotnet run --project scripts/LegacySyncTests --disable-build-servers
```

Only loopback or `/private/tmp/` socket hosts are accepted. All fixtures are synthetic. The runner creates a uniquely named `core_legacy_tests_*` database, applies real migrations and deletes its owned database in `finally`. Existing databases and Production are never migrated or seeded. The server must provide the `unaccent` extension used by Core's original migrations.

The current runner covers all four entity types and both source systems, including source-domain namespace binding, physical-ID collisions requiring explicit mapping, same-number cross-system identities, merged aliases, original-receipt retries after sender upgrades, sanitized user bootstrap, no authentication-account writes, and metadata endpoint permissions. Repeated creates with new delivery UUIDs must update the same mapped native row, while mismatched snapshot IDs or native GUIDs must leave data, source IDs, revisions and receipts unchanged. It applies both integration migrations against synthetic historical ledgers before exercising the HTTP receiver.
