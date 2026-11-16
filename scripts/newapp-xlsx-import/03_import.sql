-- Step 3: missing engines, then Sessions and TankSessions. EngineId is always the current Engines.Id (mapped by Serial),
-- never the NEWAPP id. NEWAPP ids are kept in LegacyId. Run with -v actor=<employee id>.
\set ON_ERROR_STOP on
SET work_mem = '512MB';
\set tag 'import:NEWAPP_Lich_su_su_dung_binh_ghep_3_bang.xlsx'

-- 3a. Engines used by the history but unknown here: created as Inactive and logged.
BEGIN;
CREATE TEMP TABLE new_engines ON COMMIT DROP AS
SELECT DISTINCT ON (m.serial) m.serial, coalesce(m.name, m.serial) AS name, uuidv7() AS id
FROM newapp_stage.engine_map m
WHERE m.engine_id IS NULL AND EXISTS (SELECT 1 FROM newapp_stage.usage u WHERE u."EngineId" = m.src_id)
ORDER BY m.serial, m.src_id;
INSERT INTO "Engines" ("Id", "EngineCode", "EngineName", "SerialNumber", "Status", "Description", "CreatedDate", "CreatedBy", "IsDeleted")
SELECT id, serial, name, serial, 'Inactive', 'Tạo tự động khi import lịch sử sử dụng bình NEWAPP (snapshot 06/10/2026) vì máy chưa có trong hệ thống.', now() AT TIME ZONE 'utc', :'actor', false FROM new_engines;
INSERT INTO "DataChangeLogs" ("Id", "EntityName", "EntityId", "Action", "Changes", "ChangedBy", "ChangedByUsername", "ChangedByUserType", "ChangedDate")
SELECT uuidv7(), 'Engines', id::text, 'Created',
       jsonb_build_object('EngineCode', jsonb_build_object('old', null, 'new', serial), 'EngineName', jsonb_build_object('old', null, 'new', name),
                          'SerialNumber', jsonb_build_object('old', null, 'new', serial), 'Status', jsonb_build_object('old', null, 'new', 'Inactive')),
       :'actor', :'tag', 'Employee', now() AT TIME ZONE 'utc'
FROM new_engines;
UPDATE newapp_stage.engine_map m SET engine_id = n.id FROM new_engines n WHERE m.engine_id IS NULL AND m.serial = n.serial;
SELECT count(*) AS engines_created FROM new_engines;
COMMIT;
DO $$ BEGIN
  IF EXISTS (SELECT 1 FROM newapp_stage.usage u JOIN newapp_stage.engine_map m ON m.src_id = u."EngineId" WHERE m.engine_id IS NULL LIMIT 1) THEN
    RAISE EXCEPTION 'Some sessions still have no engine';
  END IF;
END $$;

-- 3b. RFID → tank, only when exactly one tank matches (TankCode / SerialNumber, exact or ignoring leading zeros of the number part).
CREATE UNLOGGED TABLE newapp_stage.rfid_tank AS
WITH rfids AS (SELECT DISTINCT upper(trim("RFID")) AS rfid FROM newapp_stage.usage WHERE nullif(trim("RFID"), '') IS NOT NULL),
codes AS (
  SELECT t."Id", upper(trim(c)) AS code FROM "Tanks" t, unnest(ARRAY[t."TankCode", t."SerialNumber"]) c WHERE NOT t."IsDeleted" AND nullif(trim(c), '') IS NOT NULL
),
exact AS (SELECT r.rfid, c."Id" FROM rfids r JOIN codes c ON c.code = r.rfid),
loose AS (
  SELECT r.rfid, c."Id" FROM rfids r JOIN codes c
    ON regexp_replace(c.code, '^([A-Z]+)0+(\d)', '\1\2') = regexp_replace(r.rfid, '^([A-Z]+)0+(\d)', '\1\2')
  WHERE r.rfid ~ '^[A-Z]+\d+$' AND NOT EXISTS (SELECT 1 FROM exact e WHERE e.rfid = r.rfid)
)
SELECT rfid, min("Id"::text)::uuid AS tank_id FROM (SELECT * FROM exact UNION ALL SELECT * FROM loose) x
GROUP BY rfid HAVING count(DISTINCT "Id") = 1;
CREATE UNIQUE INDEX ON newapp_stage.rfid_tank (rfid);

-- 3c. Sessions (one per NEWAPP Session.Id; lines of the same session are identical, checked in step 2).
BEGIN;
DROP INDEX "IX_Sessions_EngineId_StartTime"; DROP INDEX "IX_Sessions_StartTime"; DROP INDEX "IX_Sessions_LegacyId";
INSERT INTO "Sessions" ("Id", "EngineId", "StartTime", "EndTime", "Mode", "AverageSpeed", "PlasmaOnTime", "EnvironmentTemperatureBefore", "EnvironmentTemperatureAfter",
                        "HandleTemperatureBefore", "HandleTemperatureAfter", "ErrorCode", "UserCode", "LegacyId", "CreatedDate", "CreatedBy", "IsDeleted")
SELECT uuidv7(), m.engine_id, s."StartTime", s."EndTime", s."Mode", s."AverageSpeed", s."PlasmaOnTime", s."EnviromentTemperatureBefore", s."EnviromentTemperatureAfter",
       s."HandleTemperatureBefore", s."HandleTemperatureAfter", s."ErrorCode", s."UserCode", s."SessionId", now() AT TIME ZONE 'utc', :'actor', false
FROM (SELECT DISTINCT ON ("SessionId") * FROM newapp_stage.usage ORDER BY "SessionId") s
JOIN newapp_stage.engine_map m ON m.src_id = s."EngineId"
ORDER BY s."SessionId";
CREATE INDEX "IX_Sessions_EngineId_StartTime" ON "Sessions" ("EngineId", "StartTime");
CREATE INDEX "IX_Sessions_StartTime" ON "Sessions" ("StartTime");
CREATE UNIQUE INDEX "IX_Sessions_LegacyId" ON "Sessions" ("LegacyId") WHERE "LegacyId" IS NOT NULL;
COMMIT;

-- 3d. TankSessions, linked through the imported Session.LegacyId.
BEGIN;
DROP INDEX "IX_TankSessions_SessionId"; DROP INDEX "IX_TankSessions_TankId"; DROP INDEX "IX_TankSessions_LegacyId"; DROP INDEX "IX_TankSessions_TankRfid_Upper";
ALTER TABLE "TankSessions" DROP CONSTRAINT "FK_TankSessions_Sessions_SessionId", DROP CONSTRAINT "FK_TankSessions_Tanks_TankId";
INSERT INTO "TankSessions" ("Id", "SessionId", "TankRfid", "TankId", "Order", "PressureBefore", "PressureAfter", "WeightBefore", "WeightAfter", "LegacyId", "CreatedDate", "CreatedBy", "IsDeleted")
SELECT uuidv7(), s."Id", nullif(trim(u."RFID"), ''), rt.tank_id, u."Order",
       -- NEWAPP Before/After labels are swapped: its "After" is the reading before use.
       nullif(trim(u."HighPressureAfter"), '')::numeric, nullif(trim(u."HighPressureBefore"), '')::numeric,
       nullif(trim(u."LoadCellAfter"), '')::numeric, nullif(trim(u."LoadCellBefore"), '')::numeric,
       u."TsId", now() AT TIME ZONE 'utc', :'actor', false
FROM newapp_stage.usage u
JOIN "Sessions" s ON s."LegacyId" = u."SessionId"
LEFT JOIN newapp_stage.rfid_tank rt ON rt.rfid = upper(trim(u."RFID"))
WHERE u."TsId" IS NOT NULL
ORDER BY u."TsId";
ALTER TABLE "TankSessions" ADD CONSTRAINT "FK_TankSessions_Sessions_SessionId" FOREIGN KEY ("SessionId") REFERENCES "Sessions" ("Id") ON DELETE RESTRICT;
ALTER TABLE "TankSessions" ADD CONSTRAINT "FK_TankSessions_Tanks_TankId" FOREIGN KEY ("TankId") REFERENCES "Tanks" ("Id") ON DELETE RESTRICT;
CREATE INDEX "IX_TankSessions_SessionId" ON "TankSessions" ("SessionId");
CREATE INDEX "IX_TankSessions_TankId" ON "TankSessions" ("TankId");
CREATE UNIQUE INDEX "IX_TankSessions_LegacyId" ON "TankSessions" ("LegacyId") WHERE "LegacyId" IS NOT NULL;
CREATE INDEX "IX_TankSessions_TankRfid_Upper" ON "TankSessions" (upper("TankRfid"));
COMMIT;

-- 3e. Summary log rows (per-row logging of ~32M rows is not feasible), drop staging, refresh statistics.
INSERT INTO "DataChangeLogs" ("Id", "EntityName", "EntityId", "Action", "Changes", "ChangedBy", "ChangedByUsername", "ChangedByUserType", "ChangedDate")
SELECT uuidv7(), t.name, 'NEWAPP_Lich_su_su_dung_binh_ghep_3_bang', 'Imported',
       jsonb_build_object('rows', t.n, 'source', 'newapp.plasmamed.com.vn / plasmamed', 'snapshot', '2026-10-06T09:33:33Z', 'file', 'NEWAPP_Lich_su_su_dung_binh_ghep_3_bang.xlsx'),
       :'actor', :'tag', 'Employee', now() AT TIME ZONE 'utc'
FROM (VALUES ('Sessions', (SELECT count(*) FROM "Sessions" WHERE "LegacyId" IS NOT NULL)),
             ('TankSessions', (SELECT count(*) FROM "TankSessions" WHERE "LegacyId" IS NOT NULL))) AS t(name, n);
SELECT (SELECT count(*) FROM "Sessions" WHERE "LegacyId" IS NOT NULL) AS sessions,
       (SELECT count(*) FROM "TankSessions" WHERE "LegacyId" IS NOT NULL) AS tank_sessions,
       (SELECT count(*) FROM "TankSessions" WHERE "TankId" IS NOT NULL) AS linked_to_tank;
DROP SCHEMA newapp_stage CASCADE;
VACUUM ANALYZE "Sessions";
VACUUM ANALYZE "TankSessions";
VACUUM ANALYZE "Engines";
