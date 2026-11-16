-- Step 3: TankSession → "TankSessions", linked through the imported Session.LegacyId.
\set ON_ERROR_STOP on
SET work_mem = '512MB';
CREATE UNLOGGED TABLE newapp_stage.tank_session ("Id" bigint, "TankId" bigint, "EngineTankId" bigint, "SessionId" bigint, "HighPressureAfter" text, "HighPressureBefore" text, "LoadCellAfter" text, "LoadCellBefore" text, "Order" integer, "RFID" text);
\copy newapp_stage.tank_session FROM PROGRAM 'gzip -dc TankSession.csv.gz' WITH (FORMAT csv, HEADER true, NULL '\N', ENCODING 'UTF8')
-- Values must be plain numbers or null; anything else stops the import instead of being silently dropped.
DO $$ DECLARE bad bigint; BEGIN
  SELECT count(*) INTO bad FROM newapp_stage.tank_session WHERE
    (nullif(trim("HighPressureBefore"), '') !~ '^-?\d+(\.\d+)?$') OR (nullif(trim("HighPressureAfter"), '') !~ '^-?\d+(\.\d+)?$') OR
    (nullif(trim("LoadCellBefore"), '') !~ '^-?\d+(\.\d+)?$') OR (nullif(trim("LoadCellAfter"), '') !~ '^-?\d+(\.\d+)?$') OR length(trim("RFID")) > 50;
  IF bad > 0 THEN RAISE EXCEPTION '% TankSession rows have non-numeric measurements or RFID longer than 50', bad; END IF;
END $$;
BEGIN;
DROP INDEX "IX_TankSessions_SessionId"; DROP INDEX "IX_TankSessions_TankId"; DROP INDEX "IX_TankSessions_LegacyId"; DROP INDEX "IX_TankSessions_TankRfid_Upper";
ALTER TABLE "TankSessions" DROP CONSTRAINT "FK_TankSessions_Sessions_SessionId", DROP CONSTRAINT "FK_TankSessions_Tanks_TankId";
INSERT INTO "TankSessions" ("Id", "SessionId", "TankRfid", "TankId", "Order", "PressureBefore", "PressureAfter", "WeightBefore", "WeightAfter", "LegacyId", "CreatedDate", "CreatedBy", "IsDeleted")
SELECT uuidv7(), s."Id", nullif(trim(ts."RFID"), ''), rt.tank_id, ts."Order",
       -- NEWAPP Before/After labels are swapped: its "After" is the reading before use.
       nullif(trim(ts."HighPressureAfter"), '')::numeric, nullif(trim(ts."HighPressureBefore"), '')::numeric,
       nullif(trim(ts."LoadCellAfter"), '')::numeric, nullif(trim(ts."LoadCellBefore"), '')::numeric,
       ts."Id", now() AT TIME ZONE 'utc', :'actor', false
FROM newapp_stage.tank_session ts
JOIN "Sessions" s ON s."LegacyId" = ts."SessionId"
LEFT JOIN newapp_stage.rfid_tank rt ON rt.rfid = upper(trim(ts."RFID"));
-- Constraints are re-added (and validated) after the bulk insert.
ALTER TABLE "TankSessions" ADD CONSTRAINT "FK_TankSessions_Sessions_SessionId" FOREIGN KEY ("SessionId") REFERENCES "Sessions" ("Id") ON DELETE RESTRICT;
ALTER TABLE "TankSessions" ADD CONSTRAINT "FK_TankSessions_Tanks_TankId" FOREIGN KEY ("TankId") REFERENCES "Tanks" ("Id") ON DELETE RESTRICT;
CREATE INDEX "IX_TankSessions_SessionId" ON "TankSessions" ("SessionId");
CREATE INDEX "IX_TankSessions_TankId" ON "TankSessions" ("TankId");
CREATE UNIQUE INDEX "IX_TankSessions_LegacyId" ON "TankSessions" ("LegacyId") WHERE "LegacyId" IS NOT NULL;
CREATE INDEX "IX_TankSessions_TankRfid_Upper" ON "TankSessions" (upper("TankRfid"));
COMMIT;
SELECT (SELECT count(*) FROM newapp_stage.tank_session) AS source_rows, (SELECT count(*) FROM "TankSessions" WHERE "LegacyId" IS NOT NULL) AS imported_rows,
       (SELECT count(*) FROM "TankSessions" WHERE "TankId" IS NOT NULL) AS linked_to_tank;
DROP TABLE newapp_stage.tank_session;
