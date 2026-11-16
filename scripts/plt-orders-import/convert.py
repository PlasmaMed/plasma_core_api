#!/usr/bin/env python3
"""Convert a mongoexport of PLT plasma_med_center.Invoice (JSON lines, canonical extended JSON)
into two tab-separated files for PostgreSQL COPY: orders.tsv and order_tanks.tsv.
Usage: convert.py <Invoice.jsonl> <output dir>"""
import json
import sys
from pathlib import Path

NULL = r"\N"


def oid(value):
    if isinstance(value, dict) and "$oid" in value:
        return value["$oid"]
    return None if value is None else str(value)


def date(value):
    # {"$date": "2026-10-08T14:28:10.229Z"} or {"$date": {"$numberLong": "..."}}
    if isinstance(value, dict) and "$date" in value:
        raw = value["$date"]
        if isinstance(raw, dict) and "$numberLong" in raw:
            from datetime import datetime, timezone
            return datetime.fromtimestamp(int(raw["$numberLong"]) / 1000, tz=timezone.utc).strftime("%Y-%m-%d %H:%M:%S.%f")
        return str(raw).replace("T", " ").replace("Z", "")
    return None


def number(value):
    if isinstance(value, dict):
        value = value.get("$numberInt") or value.get("$numberLong") or value.get("$numberDouble")
    return None if value is None else str(int(float(value)))


def cell(value):
    if value is None:
        return NULL
    return str(value).replace("\\", "\\\\").replace("\t", "\\t").replace("\n", "\\n").replace("\r", "\\r")


def main(source, out_dir):
    out = Path(out_dir)
    out.mkdir(parents=True, exist_ok=True)
    orders = tanks = 0
    with open(source, encoding="utf-8") as src, \
            open(out / "orders.tsv", "w", encoding="utf-8") as o, \
            open(out / "order_tanks.tsv", "w", encoding="utf-8") as t:
        for line in src:
            if not line.strip():
                continue
            doc = json.loads(line)
            legacy = oid(doc["_id"])
            o.write("\t".join(cell(v) for v in [
                legacy, number(doc.get("invoiceIdx")), doc.get("type"), doc.get("status"), doc.get("deliveryOption"),
                doc.get("store"), oid(doc.get("clientUserId")), oid(doc.get("ownerUser")), oid(doc.get("deliveryUserId")),
                doc.get("note"), date(doc.get("createdAt")), date(doc.get("updatedAt"))]) + "\n")
            orders += 1
            for index, tank in enumerate(doc.get("tanks") or []):
                t.write("\t".join(cell(v) for v in [legacy, oid(tank), index]) + "\n")
                tanks += 1
    print(f"orders={orders} order_tanks={tanks}")


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
