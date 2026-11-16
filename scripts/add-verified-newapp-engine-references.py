"""Add only the five verified NEWAPP machines required for historical coverage.

Uses a reviewed source metadata file and writes matching Core data-change logs in
the same transaction. Existing or deleted references cause the whole operation to
stop rather than replace records. Secrets are read locally and never printed.
"""
import datetime
import json
import os
import pathlib
import subprocess
import uuid

root = pathlib.Path('/home/plasma_admin/plm-core-sync-20261012-ssh')
rows = json.loads((root / 'source-missing-engine-metadata.json').read_text())
expected = {'PLT-37-49-BV-M3', 'PLT-37-50-BV-M3', 'PLT-26L1-06-DE', 'PLT-26L1-09-DE', 'PLT-26L1-10-DE'}
expected_ids = {'PLT-37-49-BV-M3': 1989, 'PLT-37-50-BV-M3': 1990,
                'PLT-26L1-06-DE': 1994, 'PLT-26L1-09-DE': 1997, 'PLT-26L1-10-DE': 1998}
assert len(rows) == 5 and {row['serial'] for row in rows} == expected
assert all(not row['deleted'] and row['status'] == 2 and row['id'] > 0 for row in rows)
assert all(row['id'] == expected_ids[row['serial']] and row['name'] == row['serial']
           and row['version'] == '3.0' and row['legacy_id'] is None for row in rows)
config = json.loads(pathlib.Path('/home/plasma_admin/api-core.plasma.vn/appsettings.Production.json').read_text())
parts = {a.strip().lower(): b.strip() for a, b in (x.split('=', 1) for x in config['ConnectionStrings']['DefaultConnection'].split(';') if '=' in x)}
env = os.environ.copy()
for name, field in [('PGHOST', 'host'), ('PGPORT', 'port'), ('PGDATABASE', 'database'), ('PGUSER', 'username'), ('PGPASSWORD', 'password')]:
    env[name] = parts[field]

def quote(value):
    return 'NULL' if value is None else "'" + str(value).replace("'", "''") + "'"

statements = ["BEGIN;", "SET LOCAL lock_timeout='5s';", "SET LOCAL statement_timeout='30s';"]
statements.append('LOCK TABLE public."Engines" IN SHARE ROW EXCLUSIVE MODE;')
audit = []
for row in rows:
    serial = row['serial'].strip()
    name = (row['name'] or serial).strip()
    assert 0 < len(serial) <= 50 and 0 < len(name) <= 250
    model = row.get('version')
    assert model is None or len(model) <= 100
    identifier = str(uuid.uuid4())
    description = 'Đồng bộ từ NEWAPP; trạng thái nguồn: máy đang tại khách hàng.'
    values = {'Id': identifier, 'EngineCode': serial, 'EngineName': name, 'SerialNumber': serial,
              'Model': model, 'LegacyId': row.get('legacy_id'), 'Status': 'Active', 'Description': description}
    changes = {key: {'old': None, 'new': value} for key, value in values.items()}
    changes['SourceEngineId'] = {'old': None, 'new': row['id']}
    changes['SourceStatus'] = {'old': None, 'new': 'AtClient'}
    guard = "upper(trim(\"EngineCode\"))=upper(%s) OR upper(trim(\"SerialNumber\"))=upper(%s)" % (quote(serial), quote(serial))
    if row.get('legacy_id'):
        guard += ' OR "LegacyId"=' + quote(row['legacy_id'])
    statements.append("DO $$ BEGIN IF EXISTS(SELECT 1 FROM \"Engines\" WHERE %s) THEN RAISE EXCEPTION 'A verified machine already has a Core reference; review before resuming'; END IF; END $$;" % guard)
    columns = ','.join('"' + key + '"' for key in values)
    literals = ','.join(quote(value) for value in values.values())
    statements.append('INSERT INTO "Engines" (%s,"CreatedDate","IsDeleted") VALUES (%s,CURRENT_TIMESTAMP AT TIME ZONE \'UTC\',false);' % (columns, literals))
    statements.append('INSERT INTO "DataChangeLogs" ("Id","EntityName","EntityId","Action","Changes","ChangedDate","ChangedByUsername","ChangedByUserType","CorrelationId") VALUES (%s,\'Engines\',%s,\'Created\',%s::jsonb,CURRENT_TIMESTAMP AT TIME ZONE \'UTC\',\'Newapp integration\',\'System\',\'history-backfill-20261012\');' % (quote(str(uuid.uuid4())), quote(identifier), quote(json.dumps(changes, ensure_ascii=False))))
    audit.append({'sourceEngineId': row['id'], 'serial': serial, 'coreEngineId': identifier})
statements.append('COMMIT;')
sql = '\n'.join(statements) + '\n'
reviewed = root / 'verified-engine-reference-inserts.sql'
reviewed.write_text(sql)
reviewed.chmod(0o600)
with (root / 'verified-engine-reference-inserts.log').open('wb') as log:
    subprocess.run(['psql', '-X', '-v', 'ON_ERROR_STOP=1'], input=sql.encode(), env=env, stdout=log, stderr=log, check=True)
(root / 'verified-engine-reference-results.json').write_text(json.dumps(audit, indent=2))
print('VERIFIED_SOURCE_MACHINE_REFERENCES_ADDED', len(audit))
