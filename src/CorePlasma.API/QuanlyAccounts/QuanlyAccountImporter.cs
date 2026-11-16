using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using CorePlasma.API.SourceSync;
using CorePlasma.BAL.Interfaces;
using CorePlasma.Common.Enums;
using CorePlasma.Common.Helpers;
using CorePlasma.DAL.Context;
using CorePlasma.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CorePlasma.API.QuanlyAccounts;

/// <summary>
/// Create-only credentials from the private Quanly mirror. Core-owned accounts, passwords and grants are never adopted.
/// The ledger and account commit together; profile events and business browsing keep their credential exclusion.
/// </summary>
public sealed class QuanlyAccountImporter(ApplicationDbContext db, ISourceSyncRawReader raw,
    IPermissionService permissions, IOptionsMonitor<QuanlyAccountImportOptions> options)
{
    private const string Source = "plasma-bv";
    private const string SourceSystem = "quanly.plasma.vn";
    private static readonly HashSet<string> StaffRoles = ["sysadmin", "manager", "reseller", "warehouse_keeper",
        "shipper", "cs_staff", "cs_lead", "accountant", "factory_manager"];
    private static readonly HashSet<string> QuarantineReasons = ["source_account_changed", "source_username_collision",
        "verified_customer_missing", "ambiguous_customer_binding", "verified_source_mapping_unavailable",
        "verified_profile_missing", "ambiguous_employee_binding", "unknown_source_account_role",
        "unsupported_source_username", "unsupported_source_credential", "source_account_column_missing",
        "invalid_customer_relationship", "customer_relationship_limit", "invalid_source_account_status"];
    private const string EmptyFingerprint = "0000000000000000000000000000000000000000000000000000000000000000";

    /// <summary>Processes a bounded batch from one complete, coherent raw snapshot; the worker retains the returned cursor.</summary>
    public async Task<QuanlyAccountImportScanResult> RunOnceAsync(long afterSourceUserId, CancellationToken ct)
    {
        if (!options.CurrentValue.Enabled) return new(0, 0);
        RequireSqlServer();
        var state = await raw.GetStateAsync("quanly", ct);
        if (state?.Status != "ready") return new(0, 0);
        var snapshot = await raw.ReadProjectionSnapshotAsync("quanly", ["users", "customer_users"], ct);
        if (!snapshot.TryGetValue("users", out var users) || !snapshot.TryGetValue("customer_users", out var links))
            throw new ImportConflict("source_account_tables_missing");
        if ((long)users.Count + links.Count > Math.Clamp(options.CurrentValue.MaxSnapshotRows, 1, 1_000_000))
            throw new ImportConflict("source_account_snapshot_limit");
        var current = await raw.GetStateAsync("quanly", ct);
        if (current?.Status != "ready" || current.InstallationId != state.InstallationId || current.Generation != state.Generation)
            return new(0, 0);
        var indexed = users.Select(user => (Id: Id(user.Values, "id"), Row: user)).OrderBy(item => item.Id).ToArray();
        if (indexed.Any(item => item.Id <= 0 || item.Row.Occurrences != 1) || indexed.GroupBy(item => item.Id).Any(group => group.Count() != 1))
            throw new ImportConflict("ambiguous_source_user_identity");
        // Preflight the ENTIRE snapshot before creating any account. A target collation can collapse two source usernames.
        var collisions = await CollidingSourceIdsAsync(users, ct);
        var present = indexed.ToDictionary(item => item.Id, item => item.Row);
        var ownedIds = await db.Database.SqlQuery<long>($"""
            SELECT SourceUserId AS Value FROM source_sync.QuanlyAccounts
            WHERE InstallationId={state.InstallationId} AND AccountId IS NOT NULL
            """).ToListAsync(ct);
        // A complete authoritative snapshot also revokes access for physically deleted source users.
        // Use the same cursor/budget for present and absent identities so neither group can starve the other.
        var candidates = present.Keys.Concat(ownedIds).Distinct().Order().ToArray();
        var batch = candidates.Where(id => id > afterSourceUserId).Take(Math.Clamp(options.CurrentValue.MaxUsersPerPoll, 1, 1000)).ToArray();
        foreach (var id in batch)
        {
            ct.ThrowIfCancellationRequested();
            if (present.TryGetValue(id, out var row)) await ImportCoreAsync(state.InstallationId, row, links, collisions.Contains(id), ct);
            else await ReconcileAbsentAsync(state, id, ct);
            db.ChangeTracker.Clear();
        }
        return new(batch.Length == 0 || batch[^1] == candidates[^1] ? 0 : batch[^1], batch.Length);
    }

    private async Task ReconcileAbsentAsync(SourceSyncState snapshotState, long id, CancellationToken ct)
    {
        var current = await raw.GetStateAsync("quanly", ct);
        if (!SameReadySource(snapshotState, current)) return;
        await using var ownedTransaction = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
        await db.Database.ExecuteSqlRawAsync("""
            DECLARE @result int;
            EXEC @result=sp_getapplock @Resource=N'CorePlasma:quanly-accounts:v1',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=5000;
            IF @result<0 THROW 51000,'Quanly account import lock unavailable.',1;
            """, ct);
        // Recheck after obtaining the native lock: an unready/replaced baseline is never deletion evidence.
        current = await raw.GetStateAsync("quanly", ct);
        if (!SameReadySource(snapshotState, current)) return;
        var ledger = await db.Database.SqlQuery<AccountLedger>($"""
            SELECT InstallationId,SourceUserId,AccountKind,AccountId,NativeId,Fingerprint,State,Reason
            FROM source_sync.QuanlyAccounts
            WHERE InstallationId={snapshotState.InstallationId} AND SourceUserId={id} AND AccountId IS NOT NULL
            """).SingleOrDefaultAsync(ct);
        if (ledger is null) return;
        Guid? employee = null;
        var state = "disabled";
        var reason = "source_user_absent";
        try { employee = await DisableOwnedAsync(ledger, ct); }
        catch (ImportConflict conflict) { state = "blocked"; reason = conflict.Code; }
        await db.SaveChangesAsync(ct);
        await SaveLedgerAsync(ledger with { State = state, Reason = reason }, ct);
        if (ownedTransaction is not null) await ownedTransaction.CommitAsync(ct);
        if (employee is { } employeeId) await permissions.InvalidateCacheAsync([employeeId], ct);
    }

    private static bool SameReadySource(SourceSyncState expected, SourceSyncState? current) =>
        current?.Status == "ready" && current.InstallationId == expected.InstallationId && current.Generation == expected.Generation;

    /// <summary>Internal adapter entrypoint; caller must supply source-verified, coherent users/customer_users images.</summary>
    public Task<QuanlyAccountImportOutcome> ImportAsync(Guid installationId, SourceSyncRawRow user,
        IReadOnlyList<SourceSyncRawRow> customerLinks, CancellationToken ct) => ImportCoreAsync(installationId, user, customerLinks, false, ct);

    private async Task<QuanlyAccountImportOutcome> ImportCoreAsync(Guid installation, SourceSyncRawRow user,
        IReadOnlyList<SourceSyncRawRow> links, bool sourceCollision, CancellationToken ct)
    {
        RequireSqlServer();
        var id = Id(user.Values, "id");
        if (installation == Guid.Empty || id <= 0 || user.Table != "users" || user.Occurrences != 1)
            return new(id, "blocked", "invalid_source_user_identity", null);
        // Existing fixture transactions are supported; runtime always owns a short, serializable per-account transaction.
        await using var ownedTransaction = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
        await db.Database.ExecuteSqlRawAsync("""
            DECLARE @result int;
            EXEC @result=sp_getapplock @Resource=N'CorePlasma:quanly-accounts:v1',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=5000;
            IF @result<0 THROW 51000,'Quanly account import lock unavailable.',1;
            """, ct);
        var ledger = await db.Database.SqlQuery<AccountLedger>($"""
            SELECT InstallationId,SourceUserId,AccountKind,AccountId,NativeId,Fingerprint,State,Reason
            FROM source_sync.QuanlyAccounts WHERE InstallationId={installation} AND SourceUserId={id}
            """).SingleOrDefaultAsync(ct);
        var fingerprint = ledger?.Fingerprint ?? EmptyFingerprint;
        string outcomeState;
        string? reason = null;
        Guid? employeeToInvalidate = null;
        try
        {
            var sourceActive = Boolean(user.Values, "is_active");
            var deleted = Required(user.Values, "deleted_at") is not null;
            // Disable an owned account even when another source field has changed. Never activate a Core-disabled account.
            if (ledger?.AccountId is not null && (!sourceActive || deleted))
                employeeToInvalidate = await DisableOwnedAsync(ledger, ct);
            var username = Required(user.Values, "username") ?? throw new ImportConflict("unsupported_source_username");
            if (!SupportedUsername(username)) throw new ImportConflict("unsupported_source_username");
            var role = Required(user.Values, "user_role");
            var kind = role == "customer" ? "Customer" : role is not null && StaffRoles.Contains(role) ? "Employee"
                : throw new ImportConflict("unknown_source_account_role");
            if (sourceCollision) throw new ImportConflict("source_username_collision");
            if (ledger?.AccountId is null && deleted) throw new ImportConflict("source_user_deleted");
            var credential = Credential(user.Values);
            var native = kind == "Employee" ? await ResolveEmployeeAsync(id, ct) : await ResolveCustomerAsync(id, links, ct);
            fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new[]
                { "quanly-account-v1", username, role!, credential, kind, native.ToString("D") })));
            if (ledger?.AccountId is not null)
            {
                if (ledger.Fingerprint != fingerprint || ledger.AccountKind != kind || ledger.NativeId != native)
                    throw new ImportConflict("source_account_changed");
                await EnsureOwnedIdentityAsync(ledger, username, ct);
                outcomeState = sourceActive && !deleted ? "applied" : "disabled";
                reason = sourceActive && !deleted ? null : deleted ? "source_user_deleted" : "source_user_inactive";
                // Keep the ORIGINAL binding fingerprint. Replays never write password, username, parent or grants.
            }
            else
            {
                // Ignore global filters: a deleted target account is Core history, not permission to replace its identity.
                if (kind == "Employee")
                {
                    var existing = await db.EmployeeUsers.IgnoreQueryFilters().Where(account => account.EmployeeId == native || account.Username == username)
                        .Select(account => new { account.EmployeeId }).ToListAsync(ct);
                    if (existing.Count > 0) throw new ImportConflict(existing.All(account => account.EmployeeId == native) ? "existing_same_owner" : "existing_core_account");
                    var account = new EmployeeUser { EmployeeId = native, Username = username, PasswordHash = credential,
                        Status = sourceActive ? UserStatus.Active : UserStatus.Inactive, IsSuperAdmin = false };
                    db.EmployeeUsers.Add(account);
                    ledger = new(installation, id, kind, account.Id, native, fingerprint, "created", null);
                    employeeToInvalidate = native;
                }
                else
                {
                    var existing = await db.CustomerUsers.IgnoreQueryFilters().Where(account => account.Username == username)
                        .Select(account => new { account.CustomerId }).ToListAsync(ct);
                    if (existing.Count > 0) throw new ImportConflict(existing.All(account => account.CustomerId == native) ? "existing_same_owner" : "existing_core_account");
                    var fullName = Required(user.Values, "name");
                    if (string.IsNullOrWhiteSpace(fullName) || fullName.Length > 150) throw new ImportConflict("unsupported_source_profile");
                    var email = OptionalProfile(user.Values, "email", 150);
                    var phone = OptionalProfile(user.Values, "phone_number", 100);
                    var account = new CustomerUser { CustomerId = native, Username = username, PasswordHash = credential,
                        FullName = fullName, Email = email, Phone = phone, Status = sourceActive ? UserStatus.Active : UserStatus.Inactive };
                    db.CustomerUsers.Add(account);
                    ledger = new(installation, id, kind, account.Id, native, fingerprint, "created", null);
                }
                outcomeState = "created";
                reason = sourceActive ? null : "source_user_inactive";
            }
        }
        catch (ImportConflict conflict)
        {
            outcomeState = "blocked";
            reason = conflict.Code;
            // An old credential/customer scope must not retain access after the source changes or becomes ambiguous.
            // Only ledger-owned identities may be quarantined; manual locks/deletes and Core SuperAdmins stay protected.
            if (ledger?.AccountId is not null && QuarantineReasons.Contains(reason))
            {
                try { employeeToInvalidate = await DisableOwnedAsync(ledger, ct); }
                catch (ImportConflict identityConflict) { reason = identityConflict.Code; }
            }
        }
        // Save native mutation and ownership atomically. A crash cannot leave an unowned login behind.
        await db.SaveChangesAsync(ct);
        var saved = ledger ?? new AccountLedger(installation, id, null, null, null, fingerprint, outcomeState, reason);
        await SaveLedgerAsync(saved with { State = outcomeState, Reason = reason }, ct);
        if (ownedTransaction is not null) await ownedTransaction.CommitAsync(ct);
        if (employeeToInvalidate is { } employee) await permissions.InvalidateCacheAsync([employee], ct);
        return new(id, outcomeState, reason, saved.AccountId);
    }

    private async Task<Guid> ResolveEmployeeAsync(long sourceId, CancellationToken ct)
    {
        var mapped = await MappingAsync("user", sourceId, ct);
        var representative = await db.Employees.IgnoreQueryFilters().Where(employee => employee.QuanLyId == sourceId)
            .Select(employee => employee.Id).Take(2).ToListAsync(ct);
        if (representative.Count > 1 || mapped is { } map && representative.Any(id => id != map))
            throw new ImportConflict("ambiguous_employee_binding");
        var native = mapped ?? representative.SingleOrDefault();
        if (native == Guid.Empty || !await db.Employees.IgnoreQueryFilters().AnyAsync(employee => employee.Id == native && !employee.IsDeleted, ct))
            throw new ImportConflict("verified_profile_missing");
        return native;
    }

    private async Task<Guid> ResolveCustomerAsync(long userId, IReadOnlyList<SourceSyncRawRow> links, CancellationToken ct)
    {
        var sourceCustomers = new HashSet<long>();
        foreach (var link in links)
        {
            var owner = Id(link.Values, "user_id");
            if (link.Table != "customer_users" || owner <= 0 || link.Occurrences != 1) throw new ImportConflict("invalid_customer_relationship");
            if (owner != userId) continue;
            var customer = Id(link.Values, "customer_id");
            if (customer <= 0) throw new ImportConflict("invalid_customer_relationship");
            sourceCustomers.Add(customer);
        }
        if (sourceCustomers.Count == 0) throw new ImportConflict("verified_customer_missing");
        if (sourceCustomers.Count > 1000) throw new ImportConflict("customer_relationship_limit");
        var targets = new HashSet<Guid>();
        foreach (var sourceCustomer in sourceCustomers)
        {
            var mapped = await MappingAsync("customer", sourceCustomer, ct);
            var representatives = await db.Customers.IgnoreQueryFilters().Where(customer => customer.QuanLyId == sourceCustomer)
                .Select(customer => customer.Id).Take(2).ToListAsync(ct);
            if (representatives.Count > 1 || mapped is { } map && representatives.Any(id => id != map))
                throw new ImportConflict("ambiguous_customer_binding");
            var target = mapped ?? representatives.SingleOrDefault();
            if (target == Guid.Empty || !await db.Customers.IgnoreQueryFilters().AnyAsync(customer => customer.Id == target && !customer.IsDeleted, ct))
                throw new ImportConflict("verified_customer_missing");
            targets.Add(target);
        }
        if (targets.Count != 1) throw new ImportConflict("ambiguous_customer_binding");
        return targets.Single();
    }

    private async Task<Guid?> MappingAsync(string entity, long id, CancellationToken ct)
    {
        var mapping = await db.LegacySyncMappings.AsNoTracking().SingleOrDefaultAsync(item => item.Source == Source && item.Entity == entity && item.EntityId == id, ct);
        if (mapping is null) return null;
        if (mapping.IsDeleted || mapping.SourceSystem != SourceSystem || mapping.CoreId is null)
            throw new ImportConflict("verified_source_mapping_unavailable");
        return mapping.CoreId;
    }

    private async Task EnsureOwnedIdentityAsync(AccountLedger ledger, string username, CancellationToken ct)
    {
        if (ledger.AccountKind == "Employee")
        {
            var account = await db.EmployeeUsers.IgnoreQueryFilters().SingleOrDefaultAsync(item => item.Id == ledger.AccountId, ct);
            if (account is null || account.EmployeeId != ledger.NativeId || account.Username != username || account.IsSuperAdmin)
                throw new ImportConflict("core_account_identity_changed");
        }
        else
        {
            var account = await db.CustomerUsers.IgnoreQueryFilters().SingleOrDefaultAsync(item => item.Id == ledger.AccountId, ct);
            if (account is null || account.CustomerId != ledger.NativeId || account.Username != username)
                throw new ImportConflict("core_account_identity_changed");
        }
    }

    private async Task<Guid?> DisableOwnedAsync(AccountLedger ledger, CancellationToken ct)
    {
        if (ledger.AccountKind == "Employee")
        {
            var account = await db.EmployeeUsers.IgnoreQueryFilters().SingleOrDefaultAsync(item => item.Id == ledger.AccountId, ct);
            if (account is null || account.EmployeeId != ledger.NativeId || account.IsSuperAdmin) throw new ImportConflict("core_account_identity_changed");
            if (!account.IsDeleted && account.Status == UserStatus.Active) account.Status = UserStatus.Inactive;
            return account.EmployeeId;
        }
        var customerAccount = await db.CustomerUsers.IgnoreQueryFilters().SingleOrDefaultAsync(item => item.Id == ledger.AccountId, ct);
        if (customerAccount is null || customerAccount.CustomerId != ledger.NativeId) throw new ImportConflict("core_account_identity_changed");
        if (!customerAccount.IsDeleted && customerAccount.Status == UserStatus.Active) customerAccount.Status = UserStatus.Inactive;
        return null;
    }

    private async Task<HashSet<long>> CollidingSourceIdsAsync(IReadOnlyList<SourceSyncRawRow> users, CancellationToken ct)
    {
        var result = new HashSet<long>();
        foreach (var kind in new[] { "Employee", "Customer" })
        {
            var table = kind == "Employee" ? "EmployeeUsers" : "CustomerUsers";
            var objectName = "dbo." + table;
            var collation = await db.Database.SqlQuery<string>($"""
                SELECT collation_name AS Value FROM sys.columns WHERE object_id=OBJECT_ID({objectName}) AND name=N'Username'
                """).SingleAsync(ct);
            if (!Regex.IsMatch(collation, @"\A[A-Za-z0-9_]+\z", RegexOptions.CultureInvariant)) throw new ImportConflict("unsupported_username_collation");
            var candidates = users.Where(user => user.Values.GetValueOrDefault("deleted_at") is null
                && (kind == "Customer" ? user.Values.GetValueOrDefault("user_role") == "customer"
                    : StaffRoles.Contains(user.Values.GetValueOrDefault("user_role") ?? "")))
                .Select(user => new { Id = Id(user.Values, "id"), Username = user.Values.GetValueOrDefault("username") })
                .Where(user => SupportedUsername(user.Username)).ToArray();
            var payload = JsonSerializer.Serialize(candidates);
            // Only the catalog-verified collation is SQL syntax; every source value stays in one parameter.
            var sql = """
                WITH names AS (SELECT Id,Username FROM OPENJSON({0}) WITH(Id bigint '$.Id',Username nvarchar(50) '$.Username'))
                SELECT Id AS Value FROM names WHERE Username COLLATE
                """ + " " + collation + " IN (SELECT Username COLLATE " + collation
                + " FROM names GROUP BY Username COLLATE " + collation + " HAVING COUNT(*)>1)";
            result.UnionWith(await db.Database.SqlQueryRaw<long>(sql, payload).ToListAsync(ct));
        }
        return result;
    }

    private Task SaveLedgerAsync(AccountLedger value, CancellationToken ct) => db.Database.ExecuteSqlInterpolatedAsync($"""
        UPDATE source_sync.QuanlyAccounts SET AccountKind={value.AccountKind},AccountId={value.AccountId},NativeId={value.NativeId},
          Fingerprint={value.Fingerprint},State={value.State},Reason={value.Reason},UpdatedAt=SYSUTCDATETIME()
        WHERE InstallationId={value.InstallationId} AND SourceUserId={value.SourceUserId};
        IF @@ROWCOUNT=0 INSERT source_sync.QuanlyAccounts(InstallationId,SourceUserId,AccountKind,AccountId,NativeId,Fingerprint,State,Reason)
          VALUES({value.InstallationId},{value.SourceUserId},{value.AccountKind},{value.AccountId},{value.NativeId},{value.Fingerprint},{value.State},{value.Reason});
        """, ct);

    private static string Credential(IReadOnlyDictionary<string, string?> row)
    {
        static string ReadBytes(string? text, int bytes)
        {
            if (text is null || text.Length != bytes * 2 + 2 || !text.StartsWith("\\x", StringComparison.Ordinal))
                throw new ImportConflict("unsupported_source_credential");
            try { return Convert.ToBase64String(Convert.FromHexString(text.AsSpan(2))); }
            catch (FormatException) { throw new ImportConflict("unsupported_source_credential"); }
        }
        var credential = "PLM-HMACSHA512$" + ReadBytes(Required(row, "password_salt"), 128) + "$" + ReadBytes(Required(row, "hashed_password"), 64);
        if (!PasswordHasher.IsLegacyPlmHmacSha512Hash(credential)) throw new ImportConflict("unsupported_source_credential");
        return credential;
    }

    private static string? Required(IReadOnlyDictionary<string, string?> row, string field) => row.TryGetValue(field, out var value)
        ? value : throw new ImportConflict("source_account_column_missing");
    // Legacy sign-in accepts the exact username. Account-creation UI rules must not rename/truncate source identities.
    private static bool SupportedUsername(string? username) => username is { Length: <= 50 }
        && !string.IsNullOrWhiteSpace(username) && !username.Any(char.IsControl);
    private static string? OptionalProfile(IReadOnlyDictionary<string, string?> row, string field, int length)
    {
        var value = Required(row, field);
        if (value?.Length > length) throw new ImportConflict("unsupported_source_profile");
        return value;
    }
    private static long Id(IReadOnlyDictionary<string, string?> row, string field) => row.TryGetValue(field, out var value)
        && long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : 0;
    private static bool Boolean(IReadOnlyDictionary<string, string?> row, string field) => Required(row, field) switch
    { "true" or "t" => true, "false" or "f" => false, _ => throw new ImportConflict("invalid_source_account_status") };
    private void RequireSqlServer()
    { if (!db.Database.IsSqlServer()) throw new ImportConflict("quanly_accounts_require_sql_server"); }
    private sealed class ImportConflict(string code) : Exception(code) { public string Code { get; } = code; }
    private sealed record AccountLedger(Guid InstallationId, long SourceUserId, string? AccountKind, Guid? AccountId,
        Guid? NativeId, string Fingerprint, string State, string? Reason);
}
