#!/bin/sh
# Usage: scripts/newapp-import/run.sh <export folder> [database]
# Imports the NEWAPP event export into Sessions / TankSessions / TankEngineHistories. Stops when free disk < 4 GB.
set -eu
EXPORT_DIR=$(cd "$1" && pwd); DB=${2:-core_plasma}; HERE=$(cd "$(dirname "$0")" && pwd)
ACTOR=$(psql -h localhost -d "$DB" -At -c "SELECT \"Id\" FROM \"Employees\" WHERE \"EmployeeCode\" = 'ADMIN' AND NOT \"IsDeleted\"")
[ -n "$ACTOR" ] || { echo "Admin employee not found"; exit 1; }
DATA_DIR=$(psql -h localhost -d "$DB" -At -c "SHOW data_directory")
check_disk() { FREE_KB=$(df -k "$DATA_DIR" | awk 'NR==2 {print $4}'); echo "   free disk: $((FREE_KB / 1024 / 1024)) GB"; [ "$FREE_KB" -gt 4194304 ] || { echo "Less than 4 GB free: stopping."; exit 1; }; }
cd "$EXPORT_DIR"
for step in 01_stage_codes 02_sessions 03_tank_sessions 04_tank_engine_history 05_finish; do
  check_disk; echo "== $step ($(date +%H:%M:%S))"
  psql -h localhost -d "$DB" -X -v ON_ERROR_STOP=1 -v actor="$ACTOR" -f "$HERE/$step.sql"
done
check_disk; echo "== done ($(date +%H:%M:%S))"
