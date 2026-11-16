#!/bin/sh
# Step 1: stream the workbook into UNLOGGED staging tables (schema newapp_stage). Nothing is written to app tables here.
# Usage: 01_stage.sh <workbook.xlsx> [database]
set -eu
XLSX=$(cd "$(dirname "$1")" && pwd)/$(basename "$1"); DB=${2:-core_plasma}; HERE=$(cd "$(dirname "$0")" && pwd); BIN="$HERE/xlsx2tsv-bin"
[ -x "$BIN" ] || (cd "$HERE/xlsx2tsv" && go build -o "$BIN" .)
PSQL="psql -h localhost -d $DB -X -v ON_ERROR_STOP=1 -q"
$PSQL <<'SQL'
DO $$ BEGIN
  IF EXISTS (SELECT 1 FROM "Sessions" WHERE "LegacyId" IS NOT NULL) THEN
    RAISE EXCEPTION 'NEWAPP sessions are already imported (Sessions contain LegacyId rows).';
  END IF;
END $$;
DROP SCHEMA IF EXISTS newapp_stage CASCADE;
CREATE SCHEMA newapp_stage;
CREATE UNLOGGED TABLE newapp_stage.engine_codes ("Id" bigint PRIMARY KEY, "Serial" text, "Name" text, "IsDeleted" boolean);
-- One row per history line (sheets "Sử dụng 1..22") or per session without tank ("Phiên chưa có bình", tank columns NULL).
CREATE UNLOGGED TABLE newapp_stage.usage (
  "Serial" text, "RFID" text, "TsId" bigint, "TsSessionId" bigint, "EngineId" bigint, "Order" integer, "TankId" bigint, "EngineTankId" bigint,
  "HighPressureAfter" text, "HighPressureBefore" text, "LoadCellAfter" text, "LoadCellBefore" text,
  "SessionId" bigint, "AverageSpeed" text, "EnviromentTemperatureAfter" text, "EnviromentTemperatureBefore" text, "ErrorCode" text,
  "HandleTemperatureAfter" text, "HandleTemperatureBefore" text, "Mode" text, "PlasmaOnTime" text, "UserCode" text,
  "EngineSourceId" bigint, "StartTime" timestamp, "EndTime" timestamp);
SQL
# Sheet "Máy": Id, IsDeleted, Serial, Name
$PSQL -c "\\copy newapp_stage.engine_codes FROM PROGRAM '\"$BIN\" \"$XLSX\" sheet25.xml A,I,G,B'"
COLS=A,B,E,F,G,H,I,J,K,L,M,N,O,P,Q,R,S,T,U,V,W,X,Y,AR,AS
load() { $PSQL -c "\\copy newapp_stage.usage FROM PROGRAM '\"$BIN\" \"$XLSX\" $1 $COLS'"; }
# 4 parallel streams over the 23 history sheets (sheet2..sheet24).
load sheet2.xml,sheet6.xml,sheet10.xml,sheet14.xml,sheet18.xml,sheet22.xml & P1=$!
load sheet3.xml,sheet7.xml,sheet11.xml,sheet15.xml,sheet19.xml,sheet23.xml & P2=$!
load sheet4.xml,sheet8.xml,sheet12.xml,sheet16.xml,sheet20.xml,sheet24.xml & P3=$!
load sheet5.xml,sheet9.xml,sheet13.xml,sheet17.xml,sheet21.xml & P4=$!
FAIL=0; for p in $P1 $P2 $P3 $P4; do wait $p || FAIL=1; done
[ $FAIL -eq 0 ] || { echo "Staging failed"; exit 1; }
$PSQL -c 'ANALYZE newapp_stage.usage' -c 'SELECT count(*) AS usage_rows, count("TsId") AS tank_rows, count(DISTINCT "SessionId") AS sessions FROM newapp_stage.usage'
