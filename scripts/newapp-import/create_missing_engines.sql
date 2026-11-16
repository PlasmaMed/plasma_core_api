-- Creates engines for source engine ids used by :source_table that have no matching engine yet (logged in DataChangeLogs).
\set ON_ERROR_STOP on
BEGIN;
CREATE TEMP TABLE new_engines ON COMMIT DROP AS
SELECT DISTINCT ON (m.serial) m.serial, coalesce(m.name, m.serial) AS name, uuidv7() AS id
FROM newapp_stage.engine_map m
WHERE m.engine_id IS NULL AND EXISTS (SELECT 1 FROM newapp_stage.:source_table s WHERE s."EngineId" = m.src_id)
  AND NOT EXISTS (SELECT 1 FROM "Engines" e WHERE NOT e."IsDeleted" AND (upper(e."EngineCode") = m.serial OR upper(e."SerialNumber") = m.serial))
ORDER BY m.serial, m.src_id;
INSERT INTO "Engines" ("Id", "EngineCode", "EngineName", "SerialNumber", "Status", "Description", "CreatedDate", "CreatedBy", "IsDeleted")
SELECT id, serial, name, serial, 'Inactive', 'Tạo tự động khi import lịch sử NEWAPP (snapshot 06/10/2026) vì máy chưa có trong hệ thống.', now() AT TIME ZONE 'utc', :'actor', false FROM new_engines;
INSERT INTO "DataChangeLogs" ("Id", "EntityName", "EntityId", "Action", "Changes", "ChangedBy", "ChangedByUsername", "ChangedByUserType", "ChangedDate")
SELECT uuidv7(), 'Engines', id::text, 'Created',
       jsonb_build_object('EngineCode', jsonb_build_object('old', null, 'new', serial), 'EngineName', jsonb_build_object('old', null, 'new', name),
                          'SerialNumber', jsonb_build_object('old', null, 'new', serial), 'Status', jsonb_build_object('old', null, 'new', 'Inactive')),
       :'actor', 'import:NEWAPP_events_20261006', 'Employee', now() AT TIME ZONE 'utc'
FROM new_engines;
UPDATE newapp_stage.engine_map m SET engine_id = e."Id"
FROM "Engines" e WHERE m.engine_id IS NULL AND NOT e."IsDeleted" AND (upper(e."EngineCode") = m.serial OR upper(e."SerialNumber") = m.serial);
SELECT count(*) AS engines_created FROM new_engines;
COMMIT;
