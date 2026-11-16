"""Read-only verification of a deterministic sample from a completed NEWAPP backfill.

Run on the deployment host. No API call, event replay or database write is made.
Only aggregate counts, source IDs and failure codes are printed or saved; source
payloads, user codes, measurements and database credentials remain in memory.
"""
import argparse
import collections
import datetime
import decimal
import json
import os
import pathlib
import re
import subprocess
import sys
import uuid


FIELDS = {
    "startTime": "StartTime", "endTime": "EndTime", "userCode": "UserCode",
    "mode": "Mode", "averageSpeed": "AverageSpeed", "plasmaOnTime": "PlasmaOnTime",
    "handleTemperatureBefore": "HandleTemperatureBefore",
    "handleTemperatureAfter": "HandleTemperatureAfter",
    "environmentTemperatureBefore": "EnvironmentTemperatureBefore",
    "environmentTemperatureAfter": "EnvironmentTemperatureAfter", "errorCode": "ErrorCode",
}
CHILD_FIELDS = {
    "tankRfid": "RFID", "order": "Order", "highPressureBefore": "HighPressureBefore",
    "highPressureAfter": "HighPressureAfter", "loadCellBefore": "LoadCellBefore",
    "loadCellAfter": "LoadCellAfter",
}
MEASUREMENT_MAP = {
    "pressure_before": "highPressureAfter", "pressure_after": "highPressureBefore",
    "weight_before": "loadCellAfter", "weight_after": "loadCellBefore",
}
PGOPTIONS = "-c default_transaction_read_only=on -c statement_timeout=120000 -c TimeZone=UTC -c DateStyle=ISO,MDY -c standard_conforming_strings=on"


def load_ids(path, sample_size):
    values = json.loads(pathlib.Path(path).read_text())
    if not isinstance(values, list) or not values:
        raise ValueError("invalid ID file")
    if any(type(value) is not int or value <= 0 for value in values):
        raise ValueError("invalid source ID")
    if any(right <= left for left, right in zip(values, values[1:])):
        raise ValueError("unordered source IDs")
    count = min(len(values), sample_size)
    indices = [0] if count == 1 else [index * (len(values) - 1) // (count - 1) for index in range(count)]
    return len(values), [values[index] for index in indices]


def read_json(command, sql, env=None):
    result = subprocess.run(command + [sql], env=env, stdout=subprocess.PIPE,
                            stderr=subprocess.PIPE, timeout=150, check=False)
    if result.returncode != 0:
        # Database/tool errors can include query text; never emit their body.
        raise RuntimeError("read-only database query failed")
    return json.loads(result.stdout, parse_float=decimal.Decimal)


def database_commands(core_config, source_container):
    configuration = json.loads(pathlib.Path(core_config).read_text())
    parts = {name.strip().lower(): value.strip() for name, value in
             (part.split("=", 1) for part in configuration["ConnectionStrings"]["DefaultConnection"].split(";") if "=" in part)}
    env = os.environ.copy()
    for variable, names in {
        "PGHOST": ("host", "server"), "PGPORT": ("port",),
        "PGDATABASE": ("database",), "PGUSER": ("username", "user id", "user"),
        "PGPASSWORD": ("password",),
    }.items():
        value = next((parts[name] for name in names if name in parts), None)
        if value is None and variable != "PGPORT":
            raise ValueError("missing database connection field")
        env[variable] = value or "5432"
    env["PGOPTIONS"] = PGOPTIONS
    source = ["docker", "exec", "-e", "PGOPTIONS=" + PGOPTIONS, source_container,
              "psql", "-U", "postgres", "-d", "plasmamed", "-X", "-At", "-v", "ON_ERROR_STOP=1", "-c"]
    core = ["psql", "-X", "-At", "-v", "ON_ERROR_STOP=1", "-c"]
    return source, core, env


def source_sql(ids):
    numbers = ",".join(str(value) for value in ids)
    parent_fields = ",".join("'%s',s.\"%s\"" % (key, column.replace("Environment", "Enviroment"))
                             for key, column in FIELDS.items())
    child_fields = ",".join("'%s',c.\"%s\"" % pair for pair in CHILD_FIELDS.items())
    # One statement gives the selected snapshots and source rows the same MVCC snapshot.
    return """SELECT json_build_object(
      'outbox',(SELECT COALESCE(json_agg(q),'[]'::json) FROM (
        SELECT DISTINCT ON (o."SourceSessionId") o."Id" AS id,o."EventId" AS event_id,
          o."SourceSessionId" AS source_id,o."PayloadJson" AS payload,o."DeliveredUtc" AS delivered
        FROM public."CoreHistoryOutbox" o WHERE o."SourceSessionId" IN (%s)
        ORDER BY o."SourceSessionId",o."Id" DESC) q),
      'parents',(SELECT COALESCE(json_agg(q),'[]'::json) FROM (
        SELECT s."Id" AS source_id,e."Serial" AS engine_serial,json_build_object(%s) AS values,
          (SELECT COALESCE(json_agg(t),'[]'::json) FROM (
            SELECT c."Id" AS child_id,c."SessionId" AS source_parent_id,
              json_build_object(%s,'tankSerial',tank."Serial",'tankLegacyId',tank."OldId") AS values
            FROM public."TankSession" c LEFT JOIN public."Tank" tank ON tank."Id"=c."TankId"
            WHERE c."SessionId"=s."Id" ORDER BY c."Id") t) AS children
        FROM public."Session" s LEFT JOIN public."Engine" e ON e."Id"=s."EngineId"
        WHERE s."Id" IN (%s) ORDER BY s."Id") q))""" % (numbers, parent_fields, child_fields, numbers)


def literal(value):
    return "'" + str(value).replace("'", "''") + "'"


def core_sql(ids, outbox_rows):
    numbers = ",".join(str(value) for value in ids)
    events = ",".join(literal(str(uuid.UUID(row["event_id"]))) for row in outbox_rows)
    event_predicate = 'r."EventId" IN (%s)' % events if events else "FALSE"
    fields = ",".join("'%s',s.\"%s\"" % pair for pair in FIELDS.items())
    serials, legacy_ids = set(), set()
    for row in outbox_rows:
        payload = json.loads(row["payload"])
        for child in payload.get("tankSessions", []):
            for name in ("tankRfid", "tankSerial"):
                if child.get(name) is not None and child[name].strip():
                    serials.add(child[name].strip().upper())
            if child.get("tankLegacyId") is not None and child["tankLegacyId"].strip():
                legacy_ids.add(child["tankLegacyId"].strip())
    tank_predicates = []
    if legacy_ids:
        tank_predicates.append('t."LegacyId" IN (%s)' % ",".join(literal(value) for value in sorted(legacy_ids)))
    if serials:
        codes = ",".join(literal(value) for value in sorted(serials))
        tank_predicates.append('(upper(trim(t."TankCode")) IN (%s) OR upper(trim(t."SerialNumber")) IN (%s))' % (codes, codes))
    tank_predicate = " OR ".join(tank_predicates) or "FALSE"
    return """SELECT json_build_object(
      'parents',(SELECT COALESCE(json_agg(q),'[]'::json) FROM (
        SELECT s."Id" AS id,s."LegacyId" AS source_id,s."SourceRevision" AS revision,
          s."IsDeleted" AS deleted,e."IsDeleted" AS engine_deleted,
          e."SerialNumber" AS serial,e."EngineCode" AS code,json_build_object(%s) AS values,
          (SELECT COALESCE(json_agg(t),'[]'::json) FROM (
            SELECT c."LegacyId" AS child_id,c."SessionId" AS parent_id,c."TankId" AS tank_id,
              c."IsDeleted" AS deleted,c."TankRfid" AS rfid,c."Order" AS child_order,
              c."PressureBefore" AS pressure_before,c."PressureAfter" AS pressure_after,
              c."WeightBefore" AS weight_before,c."WeightAfter" AS weight_after
            FROM public."TankSessions" c WHERE c."SessionId"=s."Id" ORDER BY c."LegacyId") t) AS children
        FROM public."Sessions" s LEFT JOIN public."Engines" e ON e."Id"=s."EngineId"
        WHERE s."LegacyId" IN (%s) ORDER BY s."LegacyId") q),
      'receipts',(SELECT COALESCE(json_agg(q),'[]'::json) FROM (
        SELECT r."EventId" AS event_id,r."Source" AS source,r."SourceSessionId" AS source_id,
          r."SourceRevision" AS revision,r."SessionId" AS session_id,r."Disposition" AS disposition
        FROM public."HistoryIngestReceipts" r WHERE %s) q),
      'tanks',(SELECT COALESCE(json_agg(q),'[]'::json) FROM (
        SELECT t."Id" AS id,t."LegacyId" AS legacy_id,t."SerialNumber" AS serial,t."TankCode" AS code
        FROM public."Tanks" t WHERE NOT t."IsDeleted" AND (%s)) q))""" % (fields, numbers, event_predicate, tank_predicate)


def stamp(value, canonical=False):
    if value is None:
        return None
    match = re.fullmatch(r"(\d{4}-\d{2}-\d{2})[T ](\d{2}:\d{2}:\d{2})(?:\.(\d{1,7}))?(Z|[+-]\d{2}:\d{2})?", value)
    if match is None:
        raise ValueError("invalid timestamp")
    date, clock, fraction, zone = match.groups()
    fraction = fraction or ""
    if len(fraction) > 6 and fraction[6:] != "0":
        raise ValueError("sub-microsecond timestamp")
    offset = datetime.timedelta()
    if zone and zone != "Z":
        offset = datetime.timedelta(hours=int(zone[1:3]), minutes=int(zone[4:6]))
        if zone[0] == "-":
            offset = -offset
    if canonical and (zone is None or offset != datetime.timedelta()):
        raise ValueError("canonical timestamp must be UTC")
    date_time = datetime.datetime.strptime(date + "T" + clock, "%Y-%m-%dT%H:%M:%S")
    return date_time.replace(microsecond=int(fraction[:6].ljust(6, "0") or "0")) - offset


def measure(value):
    if value is None or not str(value).strip():
        return None
    text = str(value).strip()
    if len(text) > 100 or re.fullmatch(r"-?[0-9]+(?:\.[0-9]+)?", text) is None:
        raise ValueError("invalid measurement")
    return decimal.Decimal(text)


def normalized(value):
    return (value or "").strip().upper()


def resolve_tank(child, tanks):
    legacy = (child.get("tankLegacyId") or "").strip()
    legacy_matches = {row["id"] for row in tanks if legacy and row["legacy_id"] == legacy}

    def codes(value):
        code = normalized(value)
        return {row["id"] for row in tanks if code and code in (normalized(row["serial"]), normalized(row["code"]))}

    serial_matches = codes(child.get("tankSerial"))
    if len(legacy_matches) > 1 or len(serial_matches) > 1:
        return None
    if legacy_matches and serial_matches and legacy_matches != serial_matches:
        raise ValueError("conflicting tank identity")
    if legacy_matches or serial_matches:
        return next(iter(legacy_matches or serial_matches))
    rfid_matches = codes(child.get("tankRfid"))
    return next(iter(rfid_matches)) if len(rfid_matches) == 1 else None


def compare(ids, source, core):
    failures = []
    counts = collections.Counter()

    def fail(source_id, code, child_id=None):
        item = {"sourceSessionId": source_id, "code": code}
        if child_id is not None:
            item["sourceTankSessionId"] = child_id
        failures.append(item)

    def unique(rows, key, source_id, code):
        values = [row.get(key) for row in rows]
        if len(values) != len(set(values)):
            fail(source_id, code)
        return {row.get(key): row for row in rows}

    snapshots = unique(source["outbox"], "source_id", None, "duplicate_source_outbox")
    saved = unique(source["parents"], "source_id", None, "duplicate_source_parent")
    targets = unique(core["parents"], "source_id", None, "duplicate_core_parent")
    receipts = unique(core["receipts"], "event_id", None, "duplicate_core_receipt")
    dates = []
    for source_id in ids:
        before = len(failures)
        row, original, actual = snapshots.get(source_id), saved.get(source_id), targets.get(source_id)
        for value, code in [(row, "missing_outbox"), (original, "missing_source"), (actual, "missing_core")]:
            if value is None:
                fail(source_id, code)
        if row is None or original is None or actual is None:
            continue
        if row["delivered"] is None:
            fail(source_id, "outbox_not_delivered")
        payload = json.loads(row["payload"], parse_float=decimal.Decimal)
        if payload.get("source") != "NEWAPP" or payload.get("sourceSessionId") != source_id:
            fail(source_id, "payload_source_identity")
        if uuid.UUID(payload["eventId"]) != uuid.UUID(row["event_id"]) or payload["sourceRevision"] != row["id"]:
            fail(source_id, "payload_event_revision")
        if actual["deleted"] or actual["engine_deleted"]:
            fail(source_id, "core_deleted_parent_or_engine")
        if actual["revision"] != payload["sourceRevision"]:
            fail(source_id, "core_revision")
        if payload["engineSerial"] != original["engine_serial"]:
            fail(source_id, "source_engine_serial")
        if not normalized(payload["engineSerial"]) or normalized(payload["engineSerial"]) not in (normalized(actual["serial"]), normalized(actual["code"])):
            fail(source_id, "core_engine_identity")
        receipt = receipts.get(str(uuid.UUID(payload["eventId"])))
        if receipt is None:
            fail(source_id, "missing_core_receipt")
        elif (receipt["source"] != "NEWAPP" or receipt["source_id"] != source_id
              or receipt["revision"] != payload["sourceRevision"] or receipt["session_id"] != actual["id"]
              or receipt["disposition"] not in ("applied", "duplicate", "stale")):
            fail(source_id, "core_receipt_identity")
        for key in FIELDS:
            expected = payload.get(key)
            if key in ("startTime", "endTime"):
                try:
                    value = stamp(expected, canonical=True)
                    if stamp(original["values"][key]) != value:
                        fail(source_id, "source_" + key)
                    if stamp(actual["values"][key]) != value:
                        fail(source_id, "core_" + key)
                    counts["timestampFieldsChecked"] += 1
                    if key == "startTime" and value is not None:
                        dates.append(value)
                except (ValueError, TypeError):
                    fail(source_id, "invalid_" + key)
            else:
                if original["values"][key] != expected:
                    fail(source_id, "source_" + key)
                # The ingest contract preserves an existing Core UserCode if the source omits it.
                if key == "userCode" and expected is None:
                    counts["nullUserCodePreservationCases"] += 1
                elif actual["values"][key] != expected:
                    fail(source_id, "core_" + key)
                if key == "userCode" and expected is not None:
                    counts["nonNullUserCodesChecked"] += 1
        incoming = unique(payload["tankSessions"], "sourceTankSessionId", source_id, "duplicate_payload_child")
        source_children = unique(original["children"], "child_id", source_id, "duplicate_source_child")
        core_children = unique(actual["children"], "child_id", source_id, "duplicate_core_child")
        if set(incoming) != set(source_children):
            fail(source_id, "source_child_id_set")
        if set(incoming) != set(core_children):
            fail(source_id, "core_child_id_set")
        for child_id, child in incoming.items():
            original_child, target = source_children.get(child_id), core_children.get(child_id)
            if original_child is None or target is None:
                continue
            counts["tankRowsChecked"] += 1
            if original_child["source_parent_id"] != source_id or target["parent_id"] != actual["id"]:
                fail(source_id, "child_parent_identity", child_id)
            for key in list(CHILD_FIELDS) + ["tankSerial", "tankLegacyId"]:
                if original_child["values"][key] != child.get(key):
                    fail(source_id, "source_child_" + key, child_id)
            if target["deleted"] or target["rfid"] != child.get("tankRfid") or target["child_order"] != child.get("order"):
                fail(source_id, "core_child_identity", child_id)
            try:
                if target["tank_id"] != resolve_tank(child, core["tanks"]):
                    fail(source_id, "core_child_tank_mapping", child_id)
                for name, raw_name in MEASUREMENT_MAP.items():
                    if target[name] != measure(child.get(raw_name)):
                        fail(source_id, "core_child_" + name, child_id)
            except (ValueError, decimal.InvalidOperation):
                fail(source_id, "invalid_child_measurement_or_mapping", child_id)
        counts["sessionsChecked"] += 1
        if len(failures) == before:
            counts["sessionsPassed"] += 1
    return {
        "readOnly": True, "httpCalls": 0, "sampleSourceIds": ids,
        "counts": dict(counts), "failureCount": len(failures), "failures": failures,
        "sampleStartTimeRangeUtc": [value.isoformat() + "Z" for value in (min(dates), max(dates))] if dates else [],
        "result": "pass" if not failures else "fail",
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--ids-file", required=True)
    parser.add_argument("--sample-size", type=int, choices=range(20, 51), default=40)
    parser.add_argument("--core-config", default="/home/plasma_admin/api-core.plasma.vn/appsettings.Production.json")
    parser.add_argument("--source-container", default="plasmamed-postgres12")
    parser.add_argument("--output", help="Optional aggregate JSON report; chmod 0600, no payloads or secrets.")
    args = parser.parse_args()
    try:
        total_ids, ids = load_ids(args.ids_file, args.sample_size)
        source_command, core_command, env = database_commands(args.core_config, args.source_container)
        source = read_json(source_command, source_sql(ids))
        core = read_json(core_command, core_sql(ids, source["outbox"]), env)
        report = compare(ids, source, core)
        report["candidateSourceIds"] = total_ids
        report["sampleMethod"] = "evenly_spaced_in_verified_missing_id_file_including_first_and_last"
        report["verifiedAtUtc"] = datetime.datetime.utcnow().isoformat() + "Z"
        text = json.dumps(report, indent=2)
        if args.output:
            # Create with private permissions from the start and do not replace another report.
            descriptor = os.open(args.output, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
            with os.fdopen(descriptor, "w") as output:
                output.write(text + "\n")
        print("BACKFILL_SAMPLE_VERIFIED", json.dumps(report))
        return 0 if report["result"] == "pass" else 3
    except Exception as exception:
        # External error messages may contain credentials/query output and are intentionally omitted.
        print("READ_ONLY_SAMPLE_CHECK_FAILED", type(exception).__name__, file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
