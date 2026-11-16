-- Step 5: one summary row per table in DataChangeLogs (per-row logging of ~37M events is not feasible), drop staging, refresh statistics.
\set ON_ERROR_STOP on
INSERT INTO "DataChangeLogs" ("Id", "EntityName", "EntityId", "Action", "Changes", "ChangedBy", "ChangedByUsername", "ChangedByUserType", "ChangedDate")
SELECT uuidv7(), t.name, 'NEWAPP_events_import_20261006', 'Imported',
       jsonb_build_object('rows', t.n, 'source', 'newapp.plasmamed.com.vn / plasmamed', 'snapshot', '2026-10-06T09:33:33Z', 'file', t.file),
       :'actor', 'import:NEWAPP_events_20261006', 'Employee', now() AT TIME ZONE 'utc'
FROM (VALUES ('Sessions', (SELECT count(*) FROM "Sessions" WHERE "LegacyId" IS NOT NULL), 'Session.csv.gz'),
             ('TankSessions', (SELECT count(*) FROM "TankSessions" WHERE "LegacyId" IS NOT NULL), 'TankSession.csv.gz'),
             ('TankEngineHistories', (SELECT count(*) FROM "TankEngineHistories" WHERE "LegacyId" IS NOT NULL), 'TankEngineHistory.csv.gz')) AS t(name, n, file);
DROP SCHEMA newapp_stage CASCADE;
VACUUM ANALYZE "Sessions";
VACUUM ANALYZE "TankSessions";
VACUUM ANALYZE "TankEngineHistories";
VACUUM ANALYZE "Engines";
