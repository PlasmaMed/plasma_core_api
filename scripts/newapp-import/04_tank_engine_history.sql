-- Step 4: TankEngineHistory → "TankEngineHistories".
\set ON_ERROR_STOP on
SET work_mem = '512MB';
CREATE UNLOGGED TABLE newapp_stage.tank_engine_history ("Id" bigint, "EngineId" bigint, "RFID" text, "CreateTime" timestamp, "Type" integer);
\copy newapp_stage.tank_engine_history FROM PROGRAM 'gzip -dc TankEngineHistory.csv.gz' WITH (FORMAT csv, HEADER true, NULL '\N', ENCODING 'UTF8')
DO $$ BEGIN IF EXISTS (SELECT 1 FROM newapp_stage.tank_engine_history WHERE length(trim("RFID")) > 300) THEN RAISE EXCEPTION 'RFID longer than 300'; END IF; END $$;
\set source_table tank_engine_history
\ir create_missing_engines.sql
BEGIN;
DROP INDEX "IX_TankEngineHistories_EngineId_EventTime"; DROP INDEX "IX_TankEngineHistories_TankId"; DROP INDEX "IX_TankEngineHistories_LegacyId"; DROP INDEX "IX_TankEngineHistories_TankRfid_Upper";
INSERT INTO "TankEngineHistories" ("Id", "EngineId", "TankRfid", "TankId", "EventTime", "EventType", "LegacyId", "CreatedDate", "CreatedBy", "IsDeleted")
SELECT uuidv7(), m.engine_id, nullif(trim(h."RFID"), ''), rt.tank_id, h."CreateTime", h."Type", h."Id", now() AT TIME ZONE 'utc', :'actor', false
FROM newapp_stage.tank_engine_history h
JOIN newapp_stage.engine_map m ON m.src_id = h."EngineId"
LEFT JOIN newapp_stage.rfid_tank rt ON rt.rfid = upper(trim(h."RFID"));
CREATE INDEX "IX_TankEngineHistories_EngineId_EventTime" ON "TankEngineHistories" ("EngineId", "EventTime");
CREATE INDEX "IX_TankEngineHistories_TankId" ON "TankEngineHistories" ("TankId");
CREATE UNIQUE INDEX "IX_TankEngineHistories_LegacyId" ON "TankEngineHistories" ("LegacyId") WHERE "LegacyId" IS NOT NULL;
CREATE INDEX "IX_TankEngineHistories_TankRfid_Upper" ON "TankEngineHistories" (upper("TankRfid"));
COMMIT;
SELECT (SELECT count(*) FROM newapp_stage.tank_engine_history) AS source_rows, (SELECT count(*) FROM "TankEngineHistories" WHERE "LegacyId" IS NOT NULL) AS imported_rows,
       (SELECT count(*) FROM "TankEngineHistories" WHERE "TankId" IS NOT NULL) AS linked_to_tank;
DROP TABLE newapp_stage.tank_engine_history;
