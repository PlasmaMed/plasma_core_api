Run from `CorePlasma_API`:

```text
dotnet run --project scripts/QuanlyAccountImportTests
```

Without `SOURCE_PROJECTION_SQL_TEST_CONNECTION`, only the offline connection guards run;
the output explicitly says SQL integration checks were not run. No API key, real user,
real credential, remote connection or HTTP login is used.

For actual SQL integration checks, use the existing isolated SQL Server fixture, after
its EF/native schema and `src/CorePlasma.API/QuanlyAccounts/install.sql` have been applied.
This runner never creates, migrates, clears or drops a database or installs SQL itself.
The only accepted database is exactly `core_source_sync_validation_20261109`, at an exact
loopback host (`127.0.0.1`, `localhost` or `[::1]`, optionally `tcp:` and a numeric port).
The open connection must also report that exact database name and database extended
property `core_source_sync_validation_owner=core-source-sync-validation-v1`. Remote
failover, named instances, attach files and lookalike loopback hostnames are rejected.

Load `SOURCE_PROJECTION_SQL_TEST_CONNECTION` from the fixture's existing private
`fixture.env`; do not put a connection string or password into source control or command
history. Run in the same host network namespace as the fixture SQL loopback endpoint.
All account passwords and source HMAC material are generated in memory for these tests.
The runner passes no real data to the importer and prints only fixed check labels.

All SQL checks share one outer transaction on the guarded fixture connection. Synthetic
employee/customer profiles, source identity mappings, login accounts and importer ledger
changes roll back in `finally`, including on a failed assertion. Follow-up queries verify
that the synthetic login accounts and profiles disappeared. No existing row is selected
for update or deletion by a source-derived identifier.

Checks exercise the actual importer, EF/SQL collation, production password verifier and
account guard: valid staff/customer credentials; wrong password rejection; PBKDF2 upgrade;
idempotent replay; manual password and lock preservation; changed source credentials;
canonical merged customer membership versus distinct customers; inactive/deleted users;
deactivation and no automatic reactivation; existing account/profile preservation;
username collisions; malformed hashes/salts; unknown roles; no automatic administrator
or permission grants; whole-snapshot collision preflight before the first account is
created (including SQL trailing-space equality); exact one-character, two-character
and Unicode usernames.
The fixture context also registers the production audit interceptors and verifies that
imported HMAC and upgraded PBKDF2 credentials are masked in creation/update audit logs.
Changed source credentials disable an owned active account, while an administrative
Core lock remains intact. Synthetic audit rows roll back with the account changes.
Malformed source status also quarantines an owned active account without changing its
stored password or identity.
Physical-deletion regression checks remove users entirely from a complete, ready source
snapshot. Owned employee/customer logins must become inactive while parent profiles,
manual locks/deletions, Core administrators, another installation's accounts and unowned
Core accounts remain protected. Seeding state and generation changes during snapshot or
immediately before absence mutation must prevent quarantine.

To publish for the Linux fixture host:

```text
dotnet publish scripts/QuanlyAccountImportTests/QuanlyAccountImportTests.csproj -c Release -r linux-x64 --self-contained false -o <fixture-artifact-directory>
dotnet <fixture-artifact-directory>/QuanlyAccountImportTests.dll
```

Use a dedicated artifact directory outside the API project. The fixture needs .NET 10.
The final success line states the check count and confirms transaction rollback.
