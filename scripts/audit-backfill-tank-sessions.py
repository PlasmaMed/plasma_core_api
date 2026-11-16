"""Read-only tank-history audit for a frozen backfill Session-ID manifest.

Run only on the deployment host after backfill finishes. SQL is fed through psql
stdin, including docker exec -i, so a large manifest never becomes an argv item.
Only aggregate counts and identifier/reason issues are written to the fixed server
report directory. Source/Core measurement values, RFID tags and credentials are
never logged or exported. No API call, replay, schema change or database write.
"""
import argparse
import collections
import contextlib
import csv
import datetime
import decimal
import hashlib
import itertools
import json
import os
import pathlib
import re
import subprocess
import sys
import tempfile


ROOT = pathlib.Path('/home/plasma_admin/plm-core-sync-20261012-ssh')
CONFIG = pathlib.Path('/home/plasma_admin/api-core.plasma.vn/appsettings.Production.json')
REPORT = ROOT / 'tank-history-backfill-audit.json'
ISSUES = ROOT / 'tank-history-backfill-audit-issues.csv'
PG_OPTIONS = '-c default_transaction_read_only=on -c statement_timeout=1800000 -c DateStyle=ISO,MDY'
PLAIN_DECIMAL = re.compile(r'-?[0-9]+(?:\.[0-9]+)?\Z', re.ASCII)
MAX_DECIMAL_COEFFICIENT = (1 << 96) - 1


class AuditError(Exception):
    """Locally authored, non-sensitive error code; no database error text."""


def read_ids(path, expected_count):
    text = path.read_text(encoding='utf-8-sig')
    if text.lstrip().startswith('['):
        values = json.loads(text)
        if not isinstance(values, list) or any(type(value) is not int for value in values):
            raise AuditError('invalid_id_manifest')
    else:
        lines = [line.strip() for line in text.splitlines() if line.strip()]
        if any(not re.fullmatch(r'[0-9]+', line, re.ASCII) for line in lines):
            raise AuditError('invalid_id_manifest')
        values = [int(line) for line in lines]
    if not values or len(values) > 1000000:
        raise AuditError('invalid_id_manifest_size')
    if any(value <= 0 or value > 9223372036854775807 for value in values):
        raise AuditError('invalid_source_id')
    if any(left >= right for left, right in zip(values, values[1:])):
        raise AuditError('id_manifest_must_be_strictly_ascending')
    if expected_count is not None and len(values) != expected_count:
        raise AuditError('unexpected_id_manifest_count')
    return values


def connection_parts(text):
    # Npgsql connection strings may quote values containing semicolons.
    fields, start, quote, index = [], 0, None, 0
    while index < len(text):
        character = text[index]
        if quote:
            if character == quote:
                if index + 1 < len(text) and text[index + 1] == quote:
                    index += 1
                else:
                    quote = None
        elif character in ('"', "'") and '=' in text[start:index] and not text[start:index].split('=', 1)[1].strip():
            quote = character
        elif character == ';':
            fields.append(text[start:index])
            start = index + 1
        index += 1
    if quote:
        raise AuditError('invalid_core_connection_configuration')
    fields.append(text[start:])
    parts = {}
    for field in fields:
        if not field.strip():
            continue
        if '=' not in field:
            raise AuditError('invalid_core_connection_configuration')
        key, value = field.split('=', 1)
        value = value.strip()
        if len(value) >= 2 and value[0] in ('"', "'") and value[-1] == value[0]:
            value = value[1:-1].replace(value[0] * 2, value[0])
        parts[key.strip().lower()] = value
    return parts


def core_environment(config_path):
    config = json.loads(config_path.read_text(encoding='utf-8-sig'))
    parts = connection_parts(config['ConnectionStrings']['DefaultConnection'])
    aliases = {'PGHOST': ('host', 'server'), 'PGPORT': ('port',),
               'PGDATABASE': ('database', 'initial catalog'),
               'PGUSER': ('username', 'user id', 'user', 'uid'), 'PGPASSWORD': ('password', 'pwd')}
    env = os.environ.copy()
    for variable, names in aliases.items():
        value = next((parts[name] for name in names if name in parts), None)
        if variable == 'PGPORT' and value is None:
            value = '5432'
        if value is None or (not value and variable != 'PGPASSWORD'):
            raise AuditError('incomplete_core_connection_configuration')
        env[variable] = value
    env['PGOPTIONS'] = PG_OPTIONS
    env['PGCONNECT_TIMEOUT'] = '20'
    env['PGAPPNAME'] = 'tank_history_backfill_readonly_audit'
    return env


class CsvQuery:
    def __init__(self, command, sql, env=None):
        self.command, self.sql, self.env = command, sql, env
        self.process = None
        self.input_file = None

    def __enter__(self):
        # Anonymous temporary SQL input contains only query text and numeric IDs.
        # It prevents stdin/stdout pipe deadlock and does not persist query results.
        self.input_file = tempfile.TemporaryFile(mode='w+t', encoding='utf-8')
        self.input_file.write(self.sql + ';\n')
        self.input_file.seek(0)
        try:
            self.process = subprocess.Popen(self.command, stdin=self.input_file, stdout=subprocess.PIPE,
                                            stderr=subprocess.DEVNULL, env=self.env, universal_newlines=True,
                                            encoding='utf-8', bufsize=65536)
        except Exception:
            self.input_file.close()
            raise
        return csv.reader(self.process.stdout)

    def __exit__(self, exception_type, exception, traceback):
        if exception_type and self.process.poll() is None:
            self.process.terminate()
        try:
            code = self.process.wait(timeout=30)
        except subprocess.TimeoutExpired:
            self.process.kill()
            self.process.wait()
            code = 1
        finally:
            self.process.stdout.close()
            self.input_file.close()
        if exception_type is None and code != 0:
            raise AuditError('readonly_query_failed')


def requested_sql(ids):
    # IDs are strictly validated positive int64 values; no path/config value enters SQL.
    return 'WITH requested AS (SELECT unnest(ARRAY[' + ','.join(str(value) for value in ids) + ']::bigint[]) AS id) '


def source_query(ids):
    return 'COPY (' + requested_sql(ids) + '''
        SELECT r.id,s."Id",t."Id",t."SessionId",t."RFID" IS NULL,t."RFID",t."Order",
               t."HighPressureBefore",t."HighPressureAfter",t."LoadCellBefore",t."LoadCellAfter",
               k."Serial",k."OldId"
        FROM requested r LEFT JOIN "Session" s ON s."Id"=r.id
        LEFT JOIN "TankSession" t ON t."SessionId"=s."Id"
        LEFT JOIN "Tank" k ON k."Id"=t."TankId"
        ORDER BY r.id,t."Id" NULLS FIRST
    ) TO STDOUT WITH CSV'''


def core_query(ids):
    return 'COPY (' + requested_sql(ids) + '''
        SELECT r.id,s."Id",s."IsDeleted",t."LegacyId",t."Id",t."SessionId",t."IsDeleted",
               t."TankRfid" IS NULL,t."TankRfid",t."Order",
               t."PressureBefore",t."PressureAfter",t."WeightBefore",t."WeightAfter",t."TankId"
        FROM requested r LEFT JOIN "Sessions" s ON s."LegacyId"=r.id
        LEFT JOIN "TankSessions" t ON t."SessionId"=s."Id"
        ORDER BY r.id,s."Id",t."LegacyId" NULLS FIRST,t."Id"
    ) TO STDOUT WITH CSV'''


def catalog(command, env):
    legacy, code = collections.defaultdict(set), collections.defaultdict(set)
    sql = '''COPY (SELECT "Id","LegacyId",upper(btrim("SerialNumber")),upper(btrim("TankCode"))
             FROM "Tanks" WHERE NOT "IsDeleted" ORDER BY "Id") TO STDOUT WITH CSV'''
    with CsvQuery(command, sql, env) as rows:
        for row in rows:
            if len(row) != 4:
                raise AuditError('invalid_catalog_query_shape')
            tank_id, legacy_id, serial, tank_code = row
            if legacy_id:
                legacy[legacy_id].add(tank_id)
            for value in (serial, tank_code):
                if value:
                    code[value].add(tank_id)
    return legacy, code


def tank_mapping(source_row, catalogs):
    legacy, codes = catalogs
    serial = source_row[11].strip().upper()
    legacy_id = source_row[12].strip()
    legacy_matches = legacy.get(legacy_id, set()) if legacy_id else set()
    serial_matches = codes.get(serial, set()) if serial else set()
    if len(legacy_matches) > 1 or len(serial_matches) > 1:
        return None, 'ambiguous'
    if legacy_matches and serial_matches and legacy_matches != serial_matches:
        return None, 'conflicting'
    if legacy_matches or serial_matches:
        return next(iter(legacy_matches or serial_matches)), 'unique'
    rfid = source_row[5].strip().upper()
    rfid_matches = codes.get(rfid, set()) if rfid else set()
    return (next(iter(rfid_matches)), 'unique') if len(rfid_matches) == 1 else (None, 'ambiguous_or_unknown')


def source_measure(value):
    text = value.strip()
    if not text:
        return None
    if len(text) > 100 or not PLAIN_DECIMAL.fullmatch(text):
        raise ValueError('invalid_source_decimal')
    integer, _, fraction = text.lstrip('-').partition('.')
    coefficient = int(integer + fraction)
    scale = len(fraction)
    while scale and coefficient % 10 == 0:
        coefficient //= 10
        scale -= 1
    if scale > 28 or coefficient > MAX_DECIMAL_COEFFICIENT:
        raise ValueError('source_decimal_not_exactly_representable')
    return decimal.Decimal(text)


def core_measure(value):
    if not value:
        return None
    number = decimal.Decimal(value)
    if not number.is_finite():
        raise ValueError('invalid_core_decimal')
    return number


def grouped(rows, width):
    for parent_id, group in itertools.groupby(rows, key=lambda row: int(row[0])):
        values = list(group)
        if any(len(row) != width for row in values):
            raise AuditError('invalid_history_query_shape')
        yield parent_id, values


def compare(ids, source_rows, core_rows, catalogs):
    totals = collections.Counter({key: 0 for key in (
        'requestedSessions', 'sourceParents', 'coreParents', 'sourceChildren', 'coreChildren',
        'matchedSessions', 'matchedChildren', 'sessionsWithIssues', 'deletedCoreParents',
        'deletedCoreChildren', 'unmappedCoreChildren', 'uniqueTankMappingsCompared', 'validNullTankMappings'
    )})
    reasons, issues = collections.Counter(), []
    source_groups, core_groups = iter(grouped(source_rows, 13)), iter(grouped(core_rows, 15))

    def issue(parent_id, child_id, reason):
        issues.append({'sourceSessionId': parent_id, 'sourceTankSessionId': child_id, 'reason': reason})
        reasons[reason] += 1

    for requested_id in ids:
        left, right = next(source_groups, None), next(core_groups, None)
        if left is None or right is None or left[0] != requested_id or right[0] != requested_id:
            raise AuditError('query_parent_scope_or_order_mismatch')
        sources, targets = left[1], right[1]
        before = len(issues)
        totals['requestedSessions'] += 1
        source_present = bool(sources[0][1])
        parents = {row[1]: row[2] for row in targets if row[1]}
        source_children = [row for row in sources if row[2]]
        target_children = [row for row in targets if row[4]]
        totals['sourceParents'] += int(source_present)
        totals['coreParents'] += len(parents)
        totals['sourceChildren'] += len(source_children)
        totals['coreChildren'] += len(target_children)
        if not source_present:
            issue(requested_id, None, 'missing_source_parent')
        if not parents:
            issue(requested_id, None, 'missing_core_parent')
        elif len(parents) != 1:
            issue(requested_id, None, 'duplicate_core_parent_legacy_id')
        for deleted in parents.values():
            if deleted == 't':
                totals['deletedCoreParents'] += 1
                issue(requested_id, None, 'deleted_core_parent')
        if len(source_children) != len(target_children):
            issue(requested_id, None, 'child_count_mismatch')

        source_by_id = {int(row[2]): row for row in source_children}
        target_by_id = collections.defaultdict(list)
        for row in target_children:
            child_id = int(row[3]) if row[3] else None
            if row[5] not in parents:
                issue(requested_id, child_id, 'child_core_parent_ownership_mismatch')
            if row[6] == 't':
                totals['deletedCoreChildren'] += 1
                issue(requested_id, child_id, 'deleted_core_child')
            if child_id is None:
                totals['unmappedCoreChildren'] += 1
                issue(requested_id, None, 'core_child_without_source_legacy_id')
            elif child_id not in source_by_id:
                issue(requested_id, child_id, 'unexpected_source_child_under_core_parent')
            target_by_id[child_id].append(row)

        for child_id, source in source_by_id.items():
            child_before = len(issues)
            candidates = target_by_id.get(child_id, [])
            if int(source[3]) != requested_id:
                issue(requested_id, child_id, 'child_source_parent_ownership_mismatch')
            if not candidates:
                issue(requested_id, child_id, 'source_child_missing_under_expected_core_parent')
                continue
            if len(candidates) != 1:
                issue(requested_id, child_id, 'duplicate_core_child_legacy_id')
                continue
            target = candidates[0]
            child_active = target[6] != 't' and parents.get(target[5]) != 't' and len(parents) == 1
            if (source[4], source[5]) != (target[7], target[8]):
                issue(requested_id, child_id, 'raw_rfid_mismatch')
            if source[6] != target[9]:
                issue(requested_id, child_id, 'tank_order_mismatch')
            # NEWAPP Before/After labels are swapped exactly once, without unit conversion.
            for source_index, target_index, code in (
                (8, 10, 'pressure_before'), (7, 11, 'pressure_after'),
                (10, 12, 'weight_before'), (9, 13, 'weight_after')
            ):
                try:
                    expected = source_measure(source[source_index])
                except (ValueError, decimal.InvalidOperation):
                    issue(requested_id, child_id, 'invalid_source_' + code)
                    continue
                try:
                    actual = core_measure(target[target_index])
                except (ValueError, decimal.InvalidOperation):
                    issue(requested_id, child_id, 'invalid_core_' + code)
                    continue
                if expected != actual:
                    issue(requested_id, child_id, code + '_mismatch')
            if catalogs is not None:
                expected_tank, disposition = tank_mapping(source, catalogs)
                if disposition == 'conflicting':
                    issue(requested_id, child_id, 'source_tank_hints_conflict')
                elif (target[14] or None) != expected_tank:
                    issue(requested_id, child_id, 'core_tank_mapping_mismatch')
                if expected_tank is None and disposition != 'conflicting':
                    totals['validNullTankMappings'] += int(not target[14])
                elif expected_tank is not None:
                    totals['uniqueTankMappingsCompared'] += 1
            if child_active and len(issues) == child_before:
                totals['matchedChildren'] += 1
        if len(issues) == before:
            totals['matchedSessions'] += 1
        else:
            totals['sessionsWithIssues'] += 1
        if totals['requestedSessions'] % 5000 == 0:
            print('TANK_AUDIT_PROGRESS', totals['requestedSessions'], flush=True)
    if next(source_groups, None) is not None or next(core_groups, None) is not None:
        raise AuditError('query_returned_unrequested_parents')
    return totals, reasons, issues


def write_private(path, write):
    with tempfile.NamedTemporaryFile(mode='w', newline='', encoding='utf-8', dir=str(ROOT),
                                     prefix=path.name + '.', suffix='.partial', delete=False) as stream:
        temporary = pathlib.Path(stream.name)
        try:
            os.chmod(str(temporary), 0o600)
            write(stream)
            stream.flush()
            os.fsync(stream.fileno())
        except Exception:
            temporary.unlink()
            raise
    try:
        os.replace(str(temporary), str(path))
        os.chmod(str(path), 0o600)
    finally:
        if temporary.exists():
            temporary.unlink()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--ids-file', required=True, type=pathlib.Path,
                        help='Frozen original manifest, JSON array or strictly ordered one-ID-per-line text.')
    parser.add_argument('--expected-count', type=int, default=58696)
    parser.add_argument('--configfile', type=pathlib.Path, default=CONFIG)
    parser.add_argument('--issues-csv', action='store_true')
    parser.add_argument('--skip-tank-mapping', action='store_true')
    args = parser.parse_args()
    os.umask(0o077)
    if not ROOT.is_dir():
        raise AuditError('fixed_server_report_directory_missing')
    ids = read_ids(args.ids_file, args.expected_count)
    manifest_hash = hashlib.sha256(('\n'.join(str(value) for value in ids) + '\n').encode('ascii')).hexdigest()
    started = datetime.datetime.now(datetime.timezone(datetime.timedelta(hours=7))).isoformat()
    env = core_environment(args.configfile)
    core_command = ['psql', '-X', '-q', '-At', '-v', 'ON_ERROR_STOP=1']
    source_command = ['docker', 'exec', '-i', '-e', 'PGOPTIONS=' + PG_OPTIONS,
                      'plasmamed-postgres12', 'psql', '-U', 'postgres', '-d', 'plasmamed',
                      '-X', '-q', '-At', '-v', 'ON_ERROR_STOP=1']
    tanks = None if args.skip_tank_mapping else catalog(core_command, env)
    with contextlib.ExitStack() as stack:
        source_rows = stack.enter_context(CsvQuery(source_command, source_query(ids)))
        core_rows = stack.enter_context(CsvQuery(core_command, core_query(ids), env))
        totals, reasons, issues = compare(ids, source_rows, core_rows, tanks)
    report = {
        'startedAtVietnam': started, 'readOnly': True,
        'status': 'pass' if not issues else 'mismatch', 'requestedSessionCount': len(ids),
        'orderedManifestSha256': manifest_hash, 'comparedAllSelectedParentChildren': True,
        'numericMapping': 'source After -> Core Before; source Before -> Core After; exact numeric comparison',
        'tankMappingCompared': tanks is not None,
        'tankMappingScope': 'Current persisted source Tank hints and active Core catalog; receiver precedence and ambiguity rules.' if tanks is not None else 'Skipped.',
        'sourceSoftDeleteScope': 'Source Session and TankSession models have no IsDeleted column; all source rows included.',
        'coreSoftDeleteScope': 'All selected parents and their children included, with IsDeleted checked.',
        'parentOwnershipScope': 'Exact child IDs are matched under their expected selected parent; a child owned elsewhere is reported missing under the expected parent.',
        'totals': dict(totals), 'issueCountsByReason': dict(reasons), 'issues': issues,
        'issuesCsv': str(ISSUES) if args.issues_csv else None,
    }
    if args.issues_csv:
        def write_issues(stream):
            writer = csv.DictWriter(stream, fieldnames=['sourceSessionId', 'sourceTankSessionId', 'reason'])
            writer.writeheader()
            writer.writerows(issues)
        write_private(ISSUES, write_issues)
    write_private(REPORT, lambda stream: json.dump(report, stream, indent=2))
    print('TANK_HISTORY_BACKFILL_AUDIT', json.dumps({'status': report['status'], 'totals': dict(totals),
                                                  'issueCountsByReason': dict(reasons), 'report': str(REPORT)}), flush=True)
    return 0 if not issues else 2


if __name__ == '__main__':
    try:
        sys.exit(main())
    except AuditError as error:
        print('TANK_HISTORY_BACKFILL_AUDIT_FAILED', str(error), file=sys.stderr)
        sys.exit(1)
    except KeyboardInterrupt:
        print('TANK_HISTORY_BACKFILL_AUDIT_FAILED cancelled', file=sys.stderr)
        sys.exit(130)
    except Exception:
        # Exception messages and subprocess stderr can contain SQL/data or configuration.
        print('TANK_HISTORY_BACKFILL_AUDIT_FAILED audit_infrastructure_failed', file=sys.stderr)
        sys.exit(1)
