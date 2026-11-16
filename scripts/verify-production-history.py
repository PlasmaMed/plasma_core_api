"""Compare committed NEWAPP outbox snapshots with both databases, then replay one event.

Run on the deployment host with permission to read server configuration and use the
local PostgreSQL administrator. It prints only aggregate results, never payloads or keys.
"""
import datetime
import decimal
import json
import os
import pathlib
import re
import subprocess
import urllib.request
import uuid


def source(sql):
    result = subprocess.check_output([
        "docker", "exec", "plasmamed-postgres12", "psql", "-U", "postgres",
        "-d", "plasmamed", "-X", "-At", "-c", sql,
    ])
    return json.loads(result, parse_float=decimal.Decimal)


def core(sql):
    result = subprocess.check_output([
        "runuser", "-u", "postgres", "--", "psql", "-p", "15433",
        "-d", "core_plasma", "-X", "-At", "-c", sql,
    ])
    return json.loads(result, parse_float=decimal.Decimal)


def stamp(value):
    if value is None:
        return None
    # Ubuntu's Python 3.8 fromisoformat does not accept all PostgreSQL fractional widths.
    text = re.sub(r"(?:Z|[+-]00:00)$", "", value)
    return datetime.datetime.strptime(text, "%Y-%m-%dT%H:%M:%S.%f" if "." in text else "%Y-%m-%dT%H:%M:%S")


def measure(value):
    return None if value is None or not str(value).strip() else decimal.Decimal(str(value).strip())


rows = source("""SELECT COALESCE(json_agg(q),'[]'::json) FROM (
    SELECT "EventId" AS event_id,"SourceSessionId" AS source_id,"PayloadJson" AS payload
    FROM "CoreHistoryOutbox" WHERE "DeliveredUtc" IS NOT NULL ORDER BY "Id" DESC LIMIT 20
) q""")
assert rows, "No acknowledged history is available to verify."
ids = ",".join(str(int(row["source_id"])) for row in rows)
fields = {
    "startTime": "StartTime", "endTime": "EndTime", "userCode": "UserCode",
    "mode": "Mode", "averageSpeed": "AverageSpeed", "plasmaOnTime": "PlasmaOnTime",
    "handleTemperatureBefore": "HandleTemperatureBefore", "handleTemperatureAfter": "HandleTemperatureAfter",
    "environmentTemperatureBefore": "EnvironmentTemperatureBefore",
    "environmentTemperatureAfter": "EnvironmentTemperatureAfter", "errorCode": "ErrorCode",
}
core_fields = ",".join("'%s',s.\"%s\"" % item for item in fields.items())
source_fields = ",".join("'%s',s.\"%s\"" % (key, name.replace("Environment", "Enviroment")) for key, name in fields.items())
parents = core("""SELECT COALESCE(json_agg(q),'[]'::json) FROM (
    SELECT s."LegacyId" AS source_id,s."SourceRevision" AS revision,
    json_build_object(%s) AS values,e."SerialNumber" AS serial,e."EngineCode" AS code,
    (SELECT json_agg(t) FROM (SELECT c."LegacyId" AS child_id,c."TankRfid" AS rfid,
     c."Order" AS child_order,c."PressureBefore" AS pressure_before,c."PressureAfter" AS pressure_after,
     c."WeightBefore" AS weight_before,c."WeightAfter" AS weight_after
     FROM "TankSessions" c WHERE c."SessionId"=s."Id") t) AS children
    FROM "Sessions" s JOIN "Engines" e ON e."Id"=s."EngineId" WHERE s."LegacyId" IN (%s)
) q""" % (core_fields, ids))
persisted = source("""SELECT COALESCE(json_agg(q),'[]'::json) FROM (
    SELECT s."Id" AS source_id,json_build_object(%s) AS values
    FROM "Session" s WHERE s."Id" IN (%s)
) q""" % (source_fields, ids))
by_id = {int(row["source_id"]): row for row in parents}
source_by_id = {int(row["source_id"]): row for row in persisted}
child_count = user_count = 0
for row in rows:
    payload = json.loads(row["payload"], parse_float=decimal.Decimal)
    actual = by_id[int(payload["sourceSessionId"])]
    saved = source_by_id[int(payload["sourceSessionId"])]["values"]
    assert int(actual["revision"]) == int(payload["sourceRevision"])
    assert payload["engineSerial"].strip().upper() in [(actual["serial"] or "").strip().upper(), (actual["code"] or "").strip().upper()]
    for key in fields:
        expected = payload.get(key)
        if key in ("startTime", "endTime"):
            assert stamp(saved[key]) == stamp(expected) == stamp(actual["values"][key])
        else:
            assert saved[key] == expected
            if key != "userCode" or expected is not None:
                assert actual["values"][key] == expected
    if payload.get("userCode") is not None:
        user_count += 1
    children = {int(child["child_id"]): child for child in actual["children"] or [] if child["child_id"] is not None}
    for child in payload["tankSessions"]:
        target = children[int(child["sourceTankSessionId"])]
        assert target["rfid"] == child.get("tankRfid") and target["child_order"] == child.get("order")
        for left, right in [("pressure_before", "highPressureAfter"), ("pressure_after", "highPressureBefore"), ("weight_before", "loadCellAfter"), ("weight_after", "loadCellBefore")]:
            assert target[left] == measure(child.get(right))
        child_count += 1

config = json.loads(pathlib.Path("/home/plasma_admin/api-core.plasma.vn/appsettings.Production.json").read_text())
first = json.loads(rows[0]["payload"])
request = urllib.request.Request(
    "https://api-core.plasma.vn/api/integration/history/sessions", data=rows[0]["payload"].encode(),
    headers={"X-API-Key": config["ApiKey"]["Key"], "Content-Type": "application/json"}, method="POST",
)
with urllib.request.urlopen(request, timeout=20) as response:
    acknowledgement = json.load(response)
assert acknowledgement["success"] and acknowledgement["data"]["disposition"] == "duplicate"
assert uuid.UUID(acknowledgement["data"]["eventId"]) == uuid.UUID(first["eventId"])
assert acknowledgement["data"]["sourceSessionId"] == first["sourceSessionId"]
report = {
    "productionRecordsChecked": len(rows), "tankRowsChecked": child_count,
    "nonNullUserCodesChecked": user_count, "persistedSourceAndCoreFields": "pass",
    "timestampsAndMeasurementMapping": "pass", "duplicateReplay": "pass",
}
output = pathlib.Path("/home/plasma_admin/plm-core-sync-20261012-ssh/production-verification.json")
output.write_text(json.dumps(report, indent=2))
output.chmod(0o600)
os.chown(output, 1000, 1000)
print("PRODUCTION_DATA_VERIFIED", json.dumps(report))
