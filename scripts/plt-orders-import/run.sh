#!/bin/sh
# Import PLT orders into Core Plasma. Run on the server (MongoDB of plt1.plasmamed.com.vn is local there).
# Usage: run.sh <export dir> [psql connection args...]   e.g. run.sh ~/plt_orders "-h 127.0.0.1 -p 15433 -U core_plasma -d core_plasma"
# With an existing <export dir>/Invoice.jsonl (copied from the server) the MongoDB export is skipped.
set -eu
DIR=$1; shift; PSQL="psql ${*:--d core_plasma} -X -v ON_ERROR_STOP=1"; HERE=$(cd "$(dirname "$0")" && pwd)
mkdir -p "$DIR"
if [ ! -s "$DIR/Invoice.jsonl" ]; then
  mongoexport --quiet -d plasma_med_center -c Invoice --jsonFormat=canonical --out "$DIR/Invoice.jsonl"
fi
python3 -I "$HERE/convert.py" "$DIR/Invoice.jsonl" "$DIR"
ACTOR=$($PSQL -At -c "SELECT \"Id\" FROM \"Employees\" WHERE \"EmployeeCode\" = 'ADMIN' AND NOT \"IsDeleted\"")
cd "$DIR" && $PSQL -v actor="$ACTOR" -f "$HERE/import.sql"
