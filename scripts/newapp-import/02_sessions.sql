-- Step 2: Session → "Sessions". Times are UTC in the source and are kept as they are (also invalid ones).
\set ON_ERROR_STOP on
SET work_mem = '512MB';
CREATE UNLOGGED TABLE newapp_stage.session ("Id" bigint, "EngineId" bigint, "StartTime" timestamp, "EndTime" timestamp, "AverageSpeed" text, "EnviromentTemperatureAfter" text, "EnviromentTemperatureBefore" text, "ErrorCode" text, "HandleTemperatureAfter" text, "HandleTemperatureBefore" text, "Mode" text, "PlasmaOnTime" text, "UserCode" text);
\copy newapp_stage.session FROM PROGRAM 'gzip -dc Session.csv.gz' WITH (FORMAT csv, HEADER true, NULL '\N', ENCODING 'UTF8')
\set source_table session
\ir create_missing_engines.sql
BEGIN;
-- Bulk load without secondary indexes, rebuilt afterwards (much faster than index maintenance per row).
DROP INDEX "IX_Sessions_EngineId_StartTime"; DROP INDEX "IX_Sessions_StartTime"; DROP INDEX "IX_Sessions_LegacyId";
INSERT INTO "Sessions" ("Id", "EngineId", "StartTime", "EndTime", "Mode", "AverageSpeed", "PlasmaOnTime", "EnvironmentTemperatureBefore", "EnvironmentTemperatureAfter",
                        "HandleTemperatureBefore", "HandleTemperatureAfter", "ErrorCode", "UserCode", "LegacyId", "CreatedDate", "CreatedBy", "IsDeleted")
SELECT uuidv7(), m.engine_id, s."StartTime", s."EndTime", s."Mode", s."AverageSpeed", s."PlasmaOnTime", s."EnviromentTemperatureBefore", s."EnviromentTemperatureAfter",
       s."HandleTemperatureBefore", s."HandleTemperatureAfter", s."ErrorCode", s."UserCode", s."Id", now() AT TIME ZONE 'utc', :'actor', false
FROM newapp_stage.session s JOIN newapp_stage.engine_map m ON m.src_id = s."EngineId";
CREATE INDEX "IX_Sessions_EngineId_StartTime" ON "Sessions" ("EngineId", "StartTime");
CREATE INDEX "IX_Sessions_StartTime" ON "Sessions" ("StartTime");
CREATE UNIQUE INDEX "IX_Sessions_LegacyId" ON "Sessions" ("LegacyId") WHERE "LegacyId" IS NOT NULL;
COMMIT;
SELECT (SELECT count(*) FROM newapp_stage.session) AS source_rows, (SELECT count(*) FROM "Sessions" WHERE "LegacyId" IS NOT NULL) AS imported_rows;
DROP TABLE newapp_stage.session;
