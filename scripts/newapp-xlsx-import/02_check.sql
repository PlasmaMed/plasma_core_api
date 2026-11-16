-- Step 2: consistency checks on the staged workbook (read-only). Any failure stops the import.
\set ON_ERROR_STOP on
-- Source engine id → engine of this system, matched on Serial (trim, case-insensitive), then on EngineCode.
DROP TABLE IF EXISTS newapp_stage.engine_map;
CREATE UNLOGGED TABLE newapp_stage.engine_map AS
SELECT ec."Id" AS src_id,
       coalesce(nullif(upper(trim(ec."Serial")), ''), 'NEWAPP-ENGINE-' || ec."Id") AS serial,
       nullif(trim(ec."Name"), '') AS name,
       coalesce(
         (SELECT e."Id" FROM "Engines" e WHERE NOT e."IsDeleted" AND upper(trim(e."SerialNumber")) = upper(trim(ec."Serial")) ORDER BY e."CreatedDate" LIMIT 1),
         (SELECT e."Id" FROM "Engines" e WHERE NOT e."IsDeleted" AND upper(trim(e."EngineCode")) = upper(trim(ec."Serial")) ORDER BY e."CreatedDate" LIMIT 1)) AS engine_id
FROM newapp_stage.engine_codes ec;
CREATE UNIQUE INDEX ON newapp_stage.engine_map (src_id);

DO $$ DECLARE n bigint; BEGIN
  SELECT count(*) INTO n FROM newapp_stage.usage WHERE "SessionId" IS NULL OR "StartTime" IS NULL OR "EngineId" IS NULL;
  IF n > 0 THEN RAISE EXCEPTION '% rows without Session.Id / StartTime / EngineId', n; END IF;
  SELECT count(*) INTO n FROM newapp_stage.usage WHERE "TsId" IS NOT NULL AND "TsSessionId" IS DISTINCT FROM "SessionId";
  IF n > 0 THEN RAISE EXCEPTION '% rows where TankSession.SessionId <> Session.Id', n; END IF;
  SELECT count(*) INTO n FROM newapp_stage.usage WHERE "EngineId" IS DISTINCT FROM "EngineSourceId";
  IF n > 0 THEN RAISE EXCEPTION '% rows where Session.EngineId <> Engine.Id', n; END IF;
  SELECT count(*) INTO n FROM newapp_stage.usage u LEFT JOIN newapp_stage.engine_codes ec ON ec."Id" = u."EngineId"
   WHERE ec."Id" IS NULL OR u."Serial" IS DISTINCT FROM ec."Serial";
  IF n > 0 THEN RAISE EXCEPTION '% rows whose Engine.Serial/Id is not in sheet "Máy"', n; END IF;
  SELECT count(*) INTO n FROM (SELECT "TsId" FROM newapp_stage.usage WHERE "TsId" IS NOT NULL GROUP BY 1 HAVING count(*) > 1) d;
  IF n > 0 THEN RAISE EXCEPTION '% duplicated TankSession.Id', n; END IF;
  SELECT count(*) INTO n FROM newapp_stage.usage WHERE "TsId" IS NOT NULL AND (
    nullif(trim("HighPressureBefore"), '') !~ '^-?\d+(\.\d+)?$' OR nullif(trim("HighPressureAfter"), '') !~ '^-?\d+(\.\d+)?$' OR
    nullif(trim("LoadCellBefore"), '') !~ '^-?\d+(\.\d+)?$' OR nullif(trim("LoadCellAfter"), '') !~ '^-?\d+(\.\d+)?$' OR length(trim("RFID")) > 50);
  IF n > 0 THEN RAISE EXCEPTION '% TankSession rows have non-numeric measurements or RFID longer than 50', n; END IF;
  -- The same session repeated on several tank lines must carry identical session values.
  SELECT count(*) INTO n FROM (
    SELECT "SessionId" FROM newapp_stage.usage GROUP BY 1
    HAVING count(DISTINCT ("EngineId", "StartTime", "EndTime", "Mode", "AverageSpeed", "PlasmaOnTime", "ErrorCode", "UserCode",
                           "EnviromentTemperatureBefore", "EnviromentTemperatureAfter", "HandleTemperatureBefore", "HandleTemperatureAfter")) > 1) d;
  IF n > 0 THEN RAISE EXCEPTION '% sessions with conflicting values across lines', n; END IF;
END $$;
SELECT count(*) AS usage_rows, count("TsId") AS tank_session_rows, count(DISTINCT "SessionId") AS sessions, count(DISTINCT "EngineId") AS engines_used FROM newapp_stage.usage;
SELECT count(*) AS source_engines, count(engine_id) AS matched_engines, count(*) - count(engine_id) AS unmatched_engines FROM newapp_stage.engine_map;
