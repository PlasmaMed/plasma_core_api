# Durable history synchronization checks

The harness creates a uniquely named local PostgreSQL database, applies the actual Core migrations and the production PLM outbox migration there, and drops only that database in its final cleanup. It never runs production startup, loads operational appsettings or user-secrets, or migrates an existing database. Default admin connection: local `127.0.0.1:5432`, user `kienkien`, database `postgres`. `TEST_PG_CONNECTION` can override local connection credentials without printing them.

Run sequentially after source builds finish:

```sh
dotnet run --project scripts/HistorySyncTests/HistorySyncTests.csproj
```

PLM files are linked from the sibling workspace folder `newapp.plasmamed.vn/plm-api` using relative paths. These legacy source components compile under .NET 10 in the harness; the full PLM application needs its own build validation.

Core HTTP tests use the actual controller, validators, receiver service, EF model, migrations and PostgreSQL transactions. Cases cover replay, stale/global revisions, concurrent deliveries, validation and ownership errors, atomic rollback, pressure/load-cell labels, exact UTC timestamps, omitted user-code preservation, ambiguous RFID, JWT isolation and source-ordered tank freshness. Stable optional `sourceEngineId` cases verify protected `NewAppId` / verified NEWAPP mapping resolution, serial renames and reuse without changing the native GUID, rejection of unknown/conflicting/deleted identities, and compatibility with a pinned normalized payload hash and receipt from before this field existed. Payloads that omit the new field retain the original serial lookup and receipt hash.

Source tests use the actual publisher, worker, enqueuer, outbox migration and PostgreSQL claim store. A minimal source DbContext supplies synthetic legacy tables rather than the full production graph. Cases cover source/outbox atomic commit and rollback, invisibility before commit, actual HTTP delivery to Core, acknowledgement binding, safe retries, concurrent claims, lease expiry/fencing, durable repairable errors and disabled workers. Payloads, credentials, connection strings and operational data are never printed.


Optional `sourceTankId` identifies a NEWAPP Tank.Id only through an existing verified `LegacySyncMappings` entry or protected `Tanks.NewAppId`. A supplied unknown, deleted or contradictory ID returns 409; it does not fall back to a coincidentally matching serial. Activate producer emission only after tank identity bootstrap, or retain the producer's opt-in switch disabled until mappings are ready. Existing immutable outbox rows are not rewritten.

Legacy packets without this field retain serial/legacy-code lookup and can additionally resolve RFID through a unique active verified mapping snapshot (`rfid` or `RFID`). Ambiguity stays unresolved and never creates source ownership. A previously bound tank-session cannot move to another tank, and missing legacy hints no longer erase an established tank link. Nullable fields are omitted from the normalized hash so stored pre-upgrade receipts remain replayable.
