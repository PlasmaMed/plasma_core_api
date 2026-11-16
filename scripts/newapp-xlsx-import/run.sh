#!/bin/sh
# Usage: scripts/newapp-xlsx-import/run.sh <NEWAPP_Lich_su_su_dung_binh_ghep_3_bang.xlsx> [database]
# Imports the NEWAPP tank usage workbook into Sessions / TankSessions. Engines are mapped to current Engines.Id by Serial.
# Stops when free disk < 4 GB. Needs Go (to build xlsx2tsv) and PostgreSQL 18 (uuidv7).
set -eu
XLSX=$1; DB=${2:-core_plasma}; HERE=$(cd "$(dirname "$0")" && pwd)
ACTOR=$(psql -h localhost -d "$DB" -At -c "SELECT \"Id\" FROM \"Employees\" WHERE \"EmployeeCode\" = 'ADMIN' AND NOT \"IsDeleted\"")
[ -n "$ACTOR" ] || { echo "Admin employee not found"; exit 1; }
DATA_DIR=$(psql -h localhost -d "$DB" -At -c "SHOW data_directory")
check_disk() { FREE_KB=$(df -k "$DATA_DIR" | awk 'NR==2 {print $4}'); echo "   free disk: $((FREE_KB / 1024 / 1024)) GB"; [ "$FREE_KB" -gt 4194304 ] || { echo "Less than 4 GB free: stopping."; exit 1; }; }
check_disk; echo "== 01_stage ($(date +%H:%M:%S))"; "$HERE/01_stage.sh" "$XLSX" "$DB"
for step in 02_check 03_import; do
  check_disk; echo "== $step ($(date +%H:%M:%S))"
  psql -h localhost -d "$DB" -X -v ON_ERROR_STOP=1 -v actor="$ACTOR" -f "$HERE/$step.sql"
done
check_disk; echo "== done ($(date +%H:%M:%S))"
