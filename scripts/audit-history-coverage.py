"""Read-only streaming comparison of every NEWAPP Session ID with Core history.

Run on the deployment host. Reports contain counts and machine serials, no API keys,
connection passwords, patient codes or measurement payloads.
"""
import argparse
import collections
import csv
import datetime
import json
import os
import pathlib
import subprocess

root = pathlib.Path('/home/plasma_admin/plm-core-sync-20261012-ssh')
parser = argparse.ArgumentParser()
parser.add_argument('--label', choices=['before', 'after', 'latest'], default='before')
parser.add_argument('--max-source-id', type=int)
parser.add_argument('--compare-times', action='store_true')
args = parser.parse_args()
config = json.loads(pathlib.Path('/home/plasma_admin/api-core.plasma.vn/appsettings.Production.json').read_text())
parts = {a.strip().lower(): b.strip() for a, b in (x.split('=', 1) for x in config['ConnectionStrings']['DefaultConnection'].split(';') if '=' in x)}
env = os.environ.copy()
for name, field in [('PGHOST', 'host'), ('PGPORT', 'port'), ('PGDATABASE', 'database'), ('PGUSER', 'username'), ('PGPASSWORD', 'password')]:
    env[name] = parts[field]
options = '-c default_transaction_read_only=on -c statement_timeout=1800000 -c DateStyle=ISO,MDY'
env['PGOPTIONS'] = options
source_cmd = ['docker', 'exec', '-e', 'PGOPTIONS=' + options, 'plasmamed-postgres12', 'psql', '-U', 'postgres', '-d', 'plasmamed', '-X', '-At', '-c']
core_cmd = ['psql', '-X', '-At', '-c']

def read_source(sql):
    return json.loads(subprocess.check_output(source_cmd + [sql]))

def read_core(sql):
    return json.loads(subprocess.check_output(core_cmd + [sql], env=env))

source_engines = read_source('SELECT COALESCE(json_agg(q),\'[]\'::json) FROM (SELECT "Id" AS id,"Serial" AS serial,"IsDeleted" AS deleted FROM "Engine") q')
core_engines = read_core('SELECT COALESCE(json_agg(q),\'[]\'::json) FROM (SELECT "Id" AS id,"SerialNumber" AS serial,"EngineCode" AS code,"IsDeleted" AS deleted FROM "Engines") q')
source_by_id = {int(row['id']): row for row in source_engines}
core_by_id = {str(row['id']): row for row in core_engines}
boundary = args.max_source_id or read_source('SELECT COALESCE(max("Id"),0) FROM "Session"')
assert boundary > 0
started = datetime.datetime.now(datetime.timezone(datetime.timedelta(hours=7))).isoformat()
stats = {int(row['id']): collections.Counter() for row in source_engines}
totals = collections.Counter()

def normalized(value):
    return (value or '').strip().upper()

# Here "active source" means not soft-deleted, regardless of the source operational status.
core_by_reference = collections.defaultdict(set)
for engine in core_engines:
    for value in [engine.get('serial'), engine.get('code')]:
        reference = normalized(value)
        if reference:
            core_by_reference[reference].add(str(engine['id']))
active_source_deleted_references = []
for engine in source_engines:
    if engine.get('deleted'):
        continue
    candidates = core_by_reference.get(normalized(engine.get('serial')), set())
    deleted_ids = sorted(identifier for identifier in candidates if core_by_id[identifier].get('deleted'))
    if deleted_ids:
        active_source_deleted_references.append({
            'sourceEngineId': int(engine['id']), 'machineSerial': engine.get('serial') or '',
            'deletedCoreEngineIds': deleted_ids,
            'activeCoreEngineIds': sorted(identifier for identifier in candidates if not core_by_id[identifier].get('deleted')),
        })

error_file = (root / 'coverage-audit-errors.log').open('wb')
os.chmod(str(root / 'coverage-audit-errors.log'), 0o600)
time_columns = ',"StartTime","EndTime"' if args.compare_times else ''
source_process = subprocess.Popen(source_cmd + ['COPY (SELECT "Id","EngineId"%s FROM "Session" WHERE "Id"<=%d ORDER BY "Id") TO STDOUT WITH CSV' % (time_columns, int(boundary))], stdout=subprocess.PIPE, stderr=error_file, universal_newlines=True, bufsize=65536)
core_process = subprocess.Popen(core_cmd + ['COPY (SELECT "LegacyId","EngineId","IsDeleted"%s FROM "Sessions" WHERE "LegacyId" IS NOT NULL AND "LegacyId"<=%d ORDER BY "LegacyId") TO STDOUT WITH CSV' % (time_columns, int(boundary))], env=env, stdout=subprocess.PIPE, stderr=error_file, universal_newlines=True, bufsize=65536)
core_rows = iter(csv.reader(core_process.stdout))
current = next(core_rows, None)
missing_name = {
    'before': 'all-missing-source-session-ids',
    'after': 'remaining-source-session-ids',
    'latest': 'latest-remaining-source-session-ids',
}[args.label]
temporary = root / (missing_name + '.partial.json')
time_mismatches = []
with temporary.open('w') as missing_file:
    os.chmod(str(temporary), 0o600)
    missing_file.write('[')
    separator = ''
    for source_row in csv.reader(source_process.stdout):
        source_id, engine_id = map(int, source_row[:2])
        machine = stats.setdefault(engine_id, collections.Counter())
        machine['sourceSessions'] += 1
        totals['sourceSessions'] += 1
        while current is not None and int(current[0]) < source_id:
            totals['coreOnlySourceIds'] += 1
            current = next(core_rows, None)
        if current is None or int(current[0]) != source_id:
            machine['missingSessions'] += 1
            totals['missingSessions'] += 1
            missing_file.write(separator + str(source_id))
            separator = ','
        else:
            target = core_by_id.get(current[1], {})
            serial = normalized(source_by_id.get(engine_id, {}).get('serial'))
            if current[2] == 't':
                machine['deletedTargetSessions'] += 1
                totals['deletedTargetSessions'] += 1
            elif not serial or serial not in [normalized(target.get('serial')), normalized(target.get('code'))]:
                machine['engineConflicts'] += 1
                totals['engineConflicts'] += 1
            else:
                machine['matchedSessions'] += 1
                totals['matchedSessions'] += 1
                if not source_by_id.get(engine_id, {}).get('deleted') and target.get('deleted'):
                    machine['activeSourceDeletedTargetEngineSessions'] += 1
                    totals['activeSourceDeletedTargetEngineSessions'] += 1
                if args.compare_times and source_row[2:4] != current[3:5]:
                    machine['timeMismatches'] += 1
                    totals['timeMismatches'] += 1
                    time_mismatches.append(source_id)
            current = next(core_rows, None)
        if totals['sourceSessions'] % 1000000 == 0:
            print('AUDIT_PROGRESS', totals['sourceSessions'], flush=True)
    while current is not None:
        totals['coreOnlySourceIds'] += 1
        current = next(core_rows, None)
    missing_file.write(']\n')
assert source_process.wait() == 0 and core_process.wait() == 0, 'A read-only coverage query failed.'
error_file.close()
os.replace(str(temporary), str(root / (missing_name + '.json')))
report_rows = []
for engine_id, counts in sorted(stats.items()):
    row = source_by_id.get(engine_id, {'serial': '', 'deleted': False})
    status = 'complete' if counts['missingSessions'] == counts['engineConflicts'] == counts['deletedTargetSessions'] == counts['timeMismatches'] == counts['activeSourceDeletedTargetEngineSessions'] == 0 else 'incomplete'
    if counts['sourceSessions'] == 0:
        status = 'no_history'
    report_rows.append({'sourceEngineId': engine_id, 'machineSerial': row.get('serial') or '', 'sourceMachineDeleted': bool(row.get('deleted')), 'sourceSessions': counts['sourceSessions'], 'matchedSessions': counts['matchedSessions'], 'missingSessions': counts['missingSessions'], 'engineConflicts': counts['engineConflicts'], 'deletedTargetSessions': counts['deletedTargetSessions'], 'activeSourceDeletedTargetEngineSessions': counts['activeSourceDeletedTargetEngineSessions'], 'timeMismatches': counts['timeMismatches'], 'status': status})
with (root / ('history-machine-coverage-' + args.label + '.csv')).open('w', newline='', encoding='utf-8-sig') as output:
    writer = csv.DictWriter(output, fieldnames=list(report_rows[0]) if report_rows else [])
    writer.writeheader()
    writer.writerows(report_rows)
summary = {'startedAtVietnam': started, 'sourceMaxSessionId': boundary, 'comparedStartAndEndTime': args.compare_times, 'totals': dict(totals), 'machinesWithHistory': sum(row['sourceSessions'] > 0 for row in report_rows), 'completeMachines': sum(row['status'] == 'complete' for row in report_rows), 'incompleteMachines': sum(row['status'] == 'incomplete' for row in report_rows)}
summary['engineSoftDeleteAudit'] = {
    'activeSourceDefinition': 'Source Engine.IsDeleted=false',
    'activeSourceMachinesWithDeletedCoreReferences': len(active_source_deleted_references),
    'activeSourceMachinesWithOnlyDeletedCoreReferences': sum(not row['activeCoreEngineIds'] for row in active_source_deleted_references),
    'activeSourceHistoryMachinesLinkedToDeletedCoreEngines': sum(row['activeSourceDeletedTargetEngineSessions'] > 0 for row in report_rows),
    'activeSourceSessionsLinkedToDeletedCoreEngines': totals['activeSourceDeletedTargetEngineSessions'],
}
for reference in active_source_deleted_references:
    counts = stats[reference['sourceEngineId']]
    reference['sourceSessions'] = counts['sourceSessions']
    reference['sessionsLinkedToDeletedCoreEngines'] = counts['activeSourceDeletedTargetEngineSessions']
soft_delete_output = root / ('active-source-deleted-core-engine-references-' + args.label + '.json')
soft_delete_output.write_text(json.dumps(active_source_deleted_references, indent=2))
soft_delete_output.chmod(0o600)
(root / ('history-coverage-' + args.label + '.json')).write_text(json.dumps(summary, indent=2))
if args.compare_times:
    mismatch_name = 'time-mismatched-source-session-ids-latest.json' if args.label == 'latest' else 'time-mismatched-source-session-ids.json'
    (root / mismatch_name).write_text(json.dumps(time_mismatches))
print('FULL_COVERAGE_AUDIT', json.dumps(summary), flush=True)
