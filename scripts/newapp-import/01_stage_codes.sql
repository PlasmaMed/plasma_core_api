-- NEWAPP event import, step 1: code maps, engine map, missing engines, RFID → tank map.
-- Run from the export folder (gzip paths are relative). Staging lives in schema newapp_stage (UNLOGGED, dropped in step 5).
\set ON_ERROR_STOP on
SET client_encoding = 'UTF8';
-- Refuse to import twice: re-import requires removing the previous NEWAPP rows first.
DO $$ BEGIN
  IF EXISTS (SELECT 1 FROM "Sessions" WHERE "LegacyId" IS NOT NULL) OR EXISTS (SELECT 1 FROM "TankEngineHistories" WHERE "LegacyId" IS NOT NULL) THEN
    RAISE EXCEPTION 'NEWAPP events are already imported (Sessions/TankEngineHistories contain LegacyId rows).';
  END IF;
END $$;
DROP SCHEMA IF EXISTS newapp_stage CASCADE;
CREATE SCHEMA newapp_stage;
CREATE UNLOGGED TABLE newapp_stage.engine_codes ("Id" bigint PRIMARY KEY, "Serial" text, "Name" text, "IsDeleted" boolean);
CREATE UNLOGGED TABLE newapp_stage.tank_codes ("Id" bigint PRIMARY KEY, "RFID" text, "Serial" text, "IsDeleted" boolean);
\copy newapp_stage.engine_codes FROM PROGRAM 'gzip -dc Engine_codes.csv.gz' WITH (FORMAT csv, HEADER true, NULL '\N', ENCODING 'UTF8')
\copy newapp_stage.tank_codes FROM PROGRAM 'gzip -dc Tank_codes.csv.gz' WITH (FORMAT csv, HEADER true, NULL '\N', ENCODING 'UTF8')

-- Source engine id → engine of this system, matched on Serial (case/space-insensitive).
CREATE UNLOGGED TABLE newapp_stage.engine_map AS
SELECT ec."Id" AS src_id,
       coalesce(nullif(upper(trim(ec."Serial")), ''), 'NEWAPP-ENGINE-' || ec."Id") AS serial,
       nullif(trim(ec."Name"), '') AS name,
       (SELECT e."Id" FROM "Engines" e WHERE NOT e."IsDeleted" AND upper(trim(e."SerialNumber")) = upper(trim(ec."Serial")) ORDER BY e."CreatedDate" LIMIT 1) AS engine_id
FROM newapp_stage.engine_codes ec;
CREATE UNIQUE INDEX ON newapp_stage.engine_map (src_id);

-- RFID → tank, only when unambiguous: RFID of exactly one source tank whose Serial is one of our tanks,
-- or (RFID unknown to the source map) RFID equal to one of our tank codes.
CREATE UNLOGGED TABLE newapp_stage.rfid_tank AS
WITH src AS (
  SELECT upper(trim("RFID")) AS rfid, min(upper(trim("Serial"))) AS serial, count(*) AS n
  FROM newapp_stage.tank_codes WHERE nullif(trim("RFID"), '') IS NOT NULL GROUP BY 1
), candidates AS (
  SELECT s.rfid, t."Id" AS tank_id FROM src s JOIN "Tanks" t ON NOT t."IsDeleted" AND upper(t."TankCode") = s.serial WHERE s.n = 1
  UNION ALL
  SELECT upper(t."TankCode"), t."Id" FROM "Tanks" t WHERE NOT t."IsDeleted" AND NOT EXISTS (SELECT 1 FROM src WHERE src.rfid = upper(t."TankCode"))
)
SELECT rfid, (array_agg(DISTINCT tank_id))[1] AS tank_id FROM candidates GROUP BY rfid HAVING count(DISTINCT tank_id) = 1;
CREATE UNIQUE INDEX ON newapp_stage.rfid_tank (rfid);

SELECT count(*) AS source_engines, count(engine_id) AS matched_engines, count(*) - count(engine_id) AS unmatched_engines FROM newapp_stage.engine_map;
SELECT count(*) AS rfid_codes_mapped_to_one_tank FROM newapp_stage.rfid_tank;
