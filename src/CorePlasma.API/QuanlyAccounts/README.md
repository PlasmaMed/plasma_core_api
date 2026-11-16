# Internal Quanly account import

This opt-in worker imports login accounts from the private, ready Quanly mirror into native Core account tables. It has no HTTP route. Register `AddQuanlyAccountImportServices(configuration)` and run the reviewed `install.sql` once before enabling it.

```json
{
  "QuanlyAccountImport": {
    "Enabled": false,
    "PollSeconds": 60,
    "MaxUsersPerPoll": 200,
    "MaxSnapshotRows": 100000
  }
}
```

Each scan obtains one coherent `users`/`customer_users` snapshot through `ISourceSyncRawReader`, checks ready installation/generation before and after reading, preflights all source username collisions using each target username column's actual SQL collation, and processes at most the configured batch. The batch combines present source users with ledger-owned identities absent from this complete snapshot. The worker retains one numeric scan cursor and wraps after a complete pass. The total snapshot row limit pauses unexpectedly large source contracts; the existing raw-reader interface materializes this selected snapshot before the limit can be checked. It never reads large history/session tables.

Staff accounts require a verified `plasma-bv` user mapping or representative `Employees.QuanLyId`. Customer accounts require `customer_users` relationships resolved through verified Core customer mappings/representative IDs. Multiple source customer aliases resolving to one canonical customer are accepted; missing/deleted/conflicting or multiple canonical targets block the account. No name, phone or email match establishes identity.

The adapter accepts only verified PostgreSQL bytea hex values for Quanly's 128-byte HMAC salt and 64-byte hash, and constructs the existing `PLM-HMACSHA512` tag in memory. Normal sign-in validates the old password and upgrades it to Core PBKDF2. The importer does not need plaintext passwords. Exact legacy usernames from 1 to 50 characters are retained; whitespace-only/control-character usernames are rejected, and no username is trimmed, normalized or truncated.

New staff accounts always have `IsSuperAdmin=false`; the importer grants no permissions. Existing Core accounts, including an account already owned by the same profile, are preserved and reported as blocked (`existing_same_owner` / `existing_core_account`). A source collision blocks all members before creation. Existing Core rows are checked with global filters disabled, so source import cannot reclaim a deleted Core identity.

Each new account and its `source_sync.QuanlyAccounts` ownership ledger commit in one serializable transaction under a transaction application lock. Ledger data contains only source installation/user IDs, native/account target IDs, an opaque descriptor fingerprint, fixed state/reason codes and timestamps. It contains no usernames, passwords, salts or password hashes. The ledger must stay private to internal projection operations and aggregate diagnostics.

Replay never updates credentials, username, parent or grants, and never reactivates a disabled/deleted account. Source role/username/credential/target changes block with `source_account_changed`, retaining the original fingerprint and binding. Source collisions or missing/ambiguous verified identities also block. These conflicts quarantine an owned Active account as Inactive so an old password/customer scope cannot retain access. The account ID and native parent must still match its original ledger ownership before mutation. A source-owned account also becomes Inactive when its source user is inactive or soft-deleted; manual Locked/Inactive/Deleted states are retained. An account subsequently promoted to Core SuperAdmin is protected. New soft-deleted source users are skipped rather than resurrected.

Physical deletion is reconciled only from the complete authoritative, ready snapshot. An absent user can disable only a ledger-owned account for that same installation: an owned Active login becomes Inactive and its ledger records `disabled` / `source_user_absent`, preserving its original fingerprint and binding. Ready installation/generation is rechecked immediately before reconciliation and again after obtaining the native transaction lock. Missing/unready/seeding or changed baselines skip absence reconciliation. Manual Locked/Inactive/Deleted states and Core SuperAdmins remain protected; no Core profile/account is deleted or reparented. If the source user later returns, the importer never reactivates the quarantined account automatically.

Account status changes invalidate the linked employee's permission cache. Existing JWT/password-reset semantics are unchanged; the worker does not claim to revoke all already issued tokens. Public source-business/profile adapters retain their credential exclusions. Logs contain counts and exception type names only.

For fixture tests, `ImportAsync(installationId, user, customerLinks, ct)` accepts synthetic coherent images and joins an existing fixture transaction; production calls use the scanner preflight before reaching this entrypoint. SQL fixtures must use the explicitly guarded database/owner in `install.sql` and roll back their mutations.
