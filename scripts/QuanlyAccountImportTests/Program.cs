using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CorePlasma.API.QuanlyAccounts;
using CorePlasma.API.SourceSync;
using CorePlasma.BAL.Interfaces;
using CorePlasma.BAL.Services;
using CorePlasma.Common.Enums;
using CorePlasma.Common.Helpers;
using CorePlasma.Common.Interfaces;
using CorePlasma.Common.Models;
using CorePlasma.DAL.Context;
using CorePlasma.DAL.Entities;
using CorePlasma.DAL.Interceptors;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var checks = 0;
void Check(bool condition, string label)
{
    if (!condition) throw new InvalidOperationException("FAIL " + label);
    checks++;
    Console.WriteLine("PASS " + label);
}

var guardTail = ";Initial Catalog=" + FixtureGuard.Database + ";Encrypt=False";
Check(FixtureGuard.AcceptsConnectionString("Server=127.0.0.1,1433" + guardTail), "Guard accepts an exact loopback SQL endpoint and the named fixture");
Check(FixtureGuard.AcceptsConnectionString("Server=tcp:localhost,1433" + guardTail), "Guard accepts explicit loopback TCP");
foreach (var source in new[] { "127.0.0.10", "127.0.0.1.attacker.invalid", "localhost.attacker.invalid", "113.160.132.61", "localhost\\instance" })
    Check(!FixtureGuard.AcceptsConnectionString("Server=" + source + guardTail), "Guard rejects a nonexact loopback endpoint");
Check(!FixtureGuard.AcceptsConnectionString("Server=127.0.0.1;Database=core_plasma;Encrypt=False"), "Guard rejects the production database before opening a connection");
Check(!FixtureGuard.AcceptsConnectionString("Server=127.0.0.1" + guardTail + ";Failover Partner=remote.invalid"), "Guard rejects remote failover");

var connectionString = Environment.GetEnvironmentVariable("SOURCE_PROJECTION_SQL_TEST_CONNECTION");
if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.WriteLine($"PASS {checks} offline guard checks. SQL integration checks NOT RUN: set SOURCE_PROJECTION_SQL_TEST_CONNECTION to the owned isolated fixture.");
    return;
}
if (!FixtureGuard.AcceptsConnectionString(connectionString))
    throw new InvalidOperationException("Only the exact loopback, named isolated fixture is accepted; no connection was opened.");

await using var connection = new SqlConnection(connectionString);
await connection.OpenAsync();
await FixtureGuard.VerifyOpenConnectionAsync(connection);
var auditActor = new SystemActorFixture();
await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(connection)
    .AddInterceptors(new AuditableEntityInterceptor(auditActor), new DataChangeLogInterceptor(auditActor)).Options);
await using var transaction = await db.Database.BeginTransactionAsync();
var raw = new AccountRawFixture();
var permissionService = DispatchProxy.Create<IPermissionService, PermissionFixture>();
var importer = new QuanlyAccountImporter(db, raw, permissionService,
    new FixedOptions<QuanlyAccountImportOptions>(new() { Enabled = true }));
var installation = raw.Installation;
var nonce = Guid.NewGuid().ToString("N")[..12];
var nextId = 1_800_000_000L + Random.Shared.NextInt64(1, 1_000_000);
var fixturePassword = "Synthetic-Quanly mật khẩu-" + nonce;
var permissionCountBefore = await db.EmployeePermissions.IgnoreQueryFilters().CountAsync();
var employeeCountBefore = await db.EmployeeUsers.IgnoreQueryFilters().CountAsync();
var customerCountBefore = await db.CustomerUsers.IgnoreQueryFilters().CountAsync();
var createdAccounts = new HashSet<Guid>();

SourceSyncRawRow Row(string table, params (string Name, string? Value)[] fields) =>
    new(table, fields.ToDictionary(x => x.Name, x => x.Value, StringComparer.Ordinal));
SourceSyncRawRow Replace(SourceSyncRawRow row, params (string Name, string? Value)[] changes)
{
    var values = row.Values.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
    foreach (var change in changes) values[change.Name] = change.Value;
    return row with { Values = values };
}
SourceSyncRawRow User(string role, string? username = null, string? password = null)
{
    var id = ++nextId;
    var salt = RandomNumberGenerator.GetBytes(128);
    var hash = HMACSHA512.HashData(salt, Encoding.UTF8.GetBytes(password ?? fixturePassword));
    return Row("users", ("id", id.ToString(CultureInfo.InvariantCulture)), ("username", username ?? "qai-" + nonce + "-" + id),
        ("name", "Synthetic account " + id), ("email", "synthetic-" + id + "@example.invalid"), ("phone_number", null),
        ("user_role", role), ("is_active", "true"), ("deleted_at", null),
        ("hashed_password", "\\x" + Convert.ToHexString(hash).ToLowerInvariant()),
        ("password_salt", "\\x" + Convert.ToHexString(salt).ToLowerInvariant()));
}
long Id(SourceSyncRawRow user) => long.Parse(user.Values["id"]!, CultureInfo.InvariantCulture);
async Task<Employee> StaffProfile(SourceSyncRawRow user, string? name = null)
{
    var employee = new Employee { Id = Guid.NewGuid(), QuanLyId = Id(user), EmployeeCode = "QAI-" + nonce + "-" + Id(user),
        FullName = name ?? "Synthetic employee", Phone = "synthetic-preserved", CreatedDate = DateTime.UtcNow };
    db.Employees.Add(employee);
    db.LegacySyncMappings.Add(new LegacySyncMapping { Source = "plasma-bv", SourceSystem = "quanly.plasma.vn", Entity = "user",
        EntityId = Id(user), CoreId = employee.Id, UpdatedAt = DateTime.UtcNow });
    await db.SaveChangesAsync();
    return employee;
}
async Task<Customer> CustomerProfile(long sourceId, Customer? canonical = null)
{
    var customer = canonical ?? new Customer { Id = Guid.NewGuid(), QuanLyId = sourceId, CustomerCode = "QAI-" + nonce + "-" + sourceId,
        CustomerName = "Synthetic clinic", CreatedDate = DateTime.UtcNow };
    if (canonical is null) db.Customers.Add(customer);
    db.LegacySyncMappings.Add(new LegacySyncMapping { Source = "plasma-bv", SourceSystem = "quanly.plasma.vn", Entity = "customer",
        EntityId = sourceId, CoreId = customer.Id, PreserveCanonical = canonical is not null, UpdatedAt = DateTime.UtcNow });
    await db.SaveChangesAsync();
    return customer;
}
SourceSyncRawRow Link(SourceSyncRawRow user, long sourceCustomer) => Row("customer_users", ("id", (++nextId).ToString(CultureInfo.InvariantCulture)),
    ("user_id", user.Values["id"]), ("customer_id", sourceCustomer.ToString(CultureInfo.InvariantCulture)));
async Task<QuanlyAccountImportOutcome> Import(SourceSyncRawRow user, params SourceSyncRawRow[] links)
{
    raw.Users.RemoveAll(existing => Id(existing) == Id(user));
    raw.Users.Add(user);
    raw.Links.RemoveAll(existing => existing.Values["user_id"] == user.Values["id"]);
    raw.Links.AddRange(links);
    var result = await importer.ImportAsync(installation, user, links, default);
    db.ChangeTracker.Clear();
    if (result.State == "created" && result.AccountId is { } id) createdAccounts.Add(id);
    return result;
}
async Task<EmployeeUser> EmployeeAccount(Guid id) => await db.EmployeeUsers.IgnoreQueryFilters().SingleAsync(account => account.Id == id);
async Task<bool> NoAccountForUsername(string username) =>
    !await db.EmployeeUsers.IgnoreQueryFilters().AnyAsync(x => x.Username == username)
    && !await db.CustomerUsers.IgnoreQueryFilters().AnyAsync(x => x.Username == username);

try
{
    var staff = User("sysadmin");
    var employee = await StaffProfile(staff);
    var first = await Import(staff);
    Check(first.State == "created" && first.AccountId is not null, "Verified Quanly staff creates one employee login");
    var staffAccount = await EmployeeAccount(first.AccountId!.Value);
    Check(staffAccount.EmployeeId == employee.Id && !staffAccount.IsSuperAdmin, "Quanly sysadmin never becomes a Core super administrator");
    Check(staffAccount.CreatedDate > DateTime.UtcNow.AddMinutes(-5), "Actual audit interceptor timestamps the imported account");
    var sourceTaggedCredential = staffAccount.PasswordHash;
    var creationLog = await db.DataChangeLogs.SingleAsync(x => x.EntityName == "EmployeeUsers" && x.EntityId == first.AccountId.Value.ToString()
        && x.Action == DataChangeLogInterceptor.Created);
    using (var changes = JsonDocument.Parse(creationLog.Changes))
        Check(changes.RootElement.GetProperty("PasswordHash").GetProperty("new").GetString() == "***"
            && !creationLog.Changes.Contains(sourceTaggedCredential, StringComparison.Ordinal), "Production audit log masks the imported HMAC credential");
    Check(PasswordHasher.Verify(fixturePassword, staffAccount.PasswordHash) && !PasswordHasher.Verify(fixturePassword + "wrong", staffAccount.PasswordHash),
        "Imported actual Quanly HMAC format accepts the same password and rejects a wrong password");
    var guard = new UserAccountGuard(Options.Create(new LoginSecuritySettings()));
    Check(guard.CheckPassword(staffAccount, fixturePassword) == SignInResult.Success && !PasswordHasher.IsLegacyPlmHmacSha512Hash(staffAccount.PasswordHash),
        "Successful imported login upgrades its credential through the production account guard");
    await db.SaveChangesAsync();
    var upgraded = staffAccount.PasswordHash;
    var upgradeLog = await db.DataChangeLogs.Where(x => x.EntityName == "EmployeeUsers" && x.EntityId == first.AccountId.Value.ToString()
        && x.Action == DataChangeLogInterceptor.Updated).SingleAsync();
    using (var changes = JsonDocument.Parse(upgradeLog.Changes))
        Check(changes.RootElement.GetProperty("PasswordHash").GetProperty("old").GetString() == "***"
            && changes.RootElement.GetProperty("PasswordHash").GetProperty("new").GetString() == "***"
            && !upgradeLog.Changes.Contains(upgraded, StringComparison.Ordinal), "Production audit log masks old and upgraded password hashes");
    var replay = await Import(staff);
    Check(replay.AccountId == first.AccountId && (await EmployeeAccount(first.AccountId.Value)).PasswordHash == upgraded,
        "Lost acknowledgement replay preserves the account ID and upgraded PBKDF2 credential");

    staffAccount = await EmployeeAccount(first.AccountId.Value);
    UserAccountGuard.SetPassword(staffAccount, "Synthetic manual password " + nonce);
    UserAccountGuard.Lock(staffAccount);
    var manual = staffAccount.PasswordHash;
    await db.SaveChangesAsync();
    await Import(staff);
    staffAccount = await EmployeeAccount(first.AccountId.Value);
    Check(staffAccount.PasswordHash == manual && staffAccount.Status == UserStatus.Locked,
        "Unchanged source replay preserves a manual Core password change and administrative lock");
    var changedCredential = User("sysadmin", staff.Values["username"], "Synthetic new source password " + nonce);
    changedCredential = Replace(changedCredential, ("id", staff.Values["id"]));
    var changed = await Import(changedCredential);
    staffAccount = await EmployeeAccount(first.AccountId.Value);
    Check(changed.State == "blocked" && staffAccount.PasswordHash == manual && staffAccount.Status == UserStatus.Locked,
        "Changed source credential is reported for reconciliation without overwriting the Core credential or lock");

    var activeChanged = User("manager");
    await StaffProfile(activeChanged);
    var activeChangedCreated = await Import(activeChanged);
    var activeChangedAccount = await EmployeeAccount(activeChangedCreated.AccountId!.Value);
    var activeOriginalCredential = activeChangedAccount.PasswordHash;
    var activeNewMaterial = User("manager", activeChanged.Values["username"], "Synthetic changed active source password " + nonce);
    activeNewMaterial = Replace(activeNewMaterial, ("id", activeChanged.Values["id"]));
    var activeConflict = await Import(activeNewMaterial);
    activeChangedAccount = await EmployeeAccount(activeChangedCreated.AccountId.Value);
    Check(activeConflict.State == "blocked" && activeChangedAccount.Status == UserStatus.Inactive && activeChangedAccount.PasswordHash == activeOriginalCredential,
        "Changed source credentials disable an owned active account while preserving its original credential for reconciliation");

    var customer = User("customer");
    var sourceCustomer = ++nextId;
    var canonical = await CustomerProfile(sourceCustomer);
    var customerResult = await Import(customer, Link(customer, sourceCustomer));
    Check(customerResult.State == "created" && customerResult.AccountId is not null, "Verified customer membership creates a customer portal account");
    var customerAccount = await db.CustomerUsers.IgnoreQueryFilters().SingleAsync(x => x.Id == customerResult.AccountId);
    Check(customerAccount.CustomerId == canonical.Id && PasswordHasher.Verify(fixturePassword, customerAccount.PasswordHash)
        && !PasswordHasher.Verify(fixturePassword + "wrong", customerAccount.PasswordHash), "Customer login retains the original password and its verified customer boundary");
    Check(!await db.EmployeeUsers.IgnoreQueryFilters().AnyAsync(x => x.Username == customer.Values["username"]), "Customer source role cannot create an employee login");

    var merged = User("customer");
    var aliasId = ++nextId;
    await CustomerProfile(aliasId, canonical);
    var mergedResult = await Import(merged, Link(merged, sourceCustomer), Link(merged, aliasId));
    Check(mergedResult.State == "created" && await db.CustomerUsers.AnyAsync(x => x.Id == mergedResult.AccountId && x.CustomerId == canonical.Id),
        "Multiple source memberships resolving to the same canonical customer remain valid");
    var distinct = User("customer");
    var otherSourceId = ++nextId;
    await CustomerProfile(otherSourceId);
    var distinctResult = await Import(distinct, Link(distinct, sourceCustomer), Link(distinct, otherSourceId));
    Check(distinctResult.State == "blocked" && await NoAccountForUsername(distinct.Values["username"]!),
        "Memberships resolving to distinct Core customers cannot choose an arbitrary customer");

    foreach (var inactive in new[] { true, false })
    {
        var disabled = User("manager");
        await StaffProfile(disabled);
        disabled = inactive ? Replace(disabled, ("is_active", "false")) : Replace(disabled, ("deleted_at", "2026-11-09T00:00:00Z"));
        var outcome = await Import(disabled);
        var account = outcome.AccountId is { } accountId ? await EmployeeAccount(accountId) : null;
        Check(account is null || account.Status != UserStatus.Active || account.IsDeleted,
            inactive ? "Inactive source user never gains active Core access" : "Deleted source user never gains active Core access");
        if (account is not null) Check(guard.CheckPassword(account, fixturePassword) != SignInResult.Success, "Production account guard rejects disabled imported credentials");
    }

    var laterDisabled = User("manager");
    await StaffProfile(laterDisabled);
    var initiallyActive = await Import(laterDisabled);
    Check(initiallyActive.State == "created", "Lifecycle fixture begins as an active source account");
    await Import(Replace(laterDisabled, ("is_active", "false")));
    var disabledExisting = await EmployeeAccount(initiallyActive.AccountId!.Value);
    Check(disabledExisting.Status != UserStatus.Active && guard.CheckPassword(disabledExisting, fixturePassword) != SignInResult.Success,
        "Source deactivation disables its previously imported active account");
    await Import(laterDisabled);
    Check((await EmployeeAccount(initiallyActive.AccountId.Value)).Status != UserStatus.Active,
        "A source replay cannot silently reactivate an account disabled in Core");

    var existingSource = User("manager");
    var existingEmployee = await StaffProfile(existingSource, "Existing Core profile must remain");
    var existingHash = PasswordHasher.Hash("Synthetic existing Core password " + nonce);
    var existingAccount = new EmployeeUser { Id = Guid.NewGuid(), EmployeeId = existingEmployee.Id,
        Username = "qai-" + nonce + "-existing", PasswordHash = existingHash, Status = UserStatus.Locked, CreatedDate = DateTime.UtcNow };
    db.EmployeeUsers.Add(existingAccount);
    await db.SaveChangesAsync();
    var existingOutcome = await Import(existingSource);
    var preserved = await EmployeeAccount(existingAccount.Id);
    var preservedEmployee = await db.Employees.SingleAsync(x => x.Id == existingEmployee.Id);
    Check(existingOutcome.State == "blocked" && preserved.PasswordHash == existingHash && preserved.Status == UserStatus.Locked
        && preservedEmployee.FullName == "Existing Core profile must remain" && preservedEmployee.Phone == "synthetic-preserved",
        "An existing Core employee account and profile are preserved without an automatic identity takeover");

    var collationFirst = User("shipper", "qai-" + nonce + "-Case");
    await StaffProfile(collationFirst);
    var collationCreated = await Import(collationFirst);
    Check(collationCreated.State == "created", "Shipper is an importable staff login");
    var collationSecond = User("shipper", "qai-" + nonce + "-case");
    await StaffProfile(collationSecond);
    var collidesUnderSql = await db.EmployeeUsers.AnyAsync(x => x.Username == collationSecond.Values["username"]);
    var collationOutcome = await Import(collationSecond);
    Check(collidesUnderSql ? collationOutcome.State == "blocked" : collationOutcome.State == "created",
        "Username conflict handling follows the actual SQL column collation");

    foreach (var malformed in new[] { "\\x00", "\\x" + new string('z', 128), "not-a-source-hash" })
    {
        var invalid = Replace(User("manager"), ("hashed_password", malformed));
        await StaffProfile(invalid);
        var result = await Import(invalid);
        Check(result.State == "blocked" && await NoAccountForUsername(invalid.Values["username"]!), "Malformed HMAC material cannot create an account");
    }
    var malformedSalt = Replace(User("manager"), ("password_salt", "\\x00"));
    await StaffProfile(malformedSalt);
    Check((await Import(malformedSalt)).State == "blocked" && await NoAccountForUsername(malformedSalt.Values["username"]!),
        "Malformed HMAC salt cannot create an account");
    var unknown = User("unknown-role");
    await StaffProfile(unknown);
    Check((await Import(unknown)).State == "blocked" && await NoAccountForUsername(unknown.Values["username"]!), "Unknown source roles fail closed");

    raw.Users.Clear();
    raw.Links.Clear();
    var sourceCollisionFirst = User("manager", "qai-" + nonce + "-snapshot");
    var sourceCollisionSecond = User("manager", sourceCollisionFirst.Values["username"] + " ");
    await StaffProfile(sourceCollisionFirst);
    await StaffProfile(sourceCollisionSecond);
    raw.Users.AddRange([sourceCollisionFirst, sourceCollisionSecond]);
    var sourceCollisionScan = await importer.RunOnceAsync(0, default);
    db.ChangeTracker.Clear();
    Check(sourceCollisionScan.Processed >= 2 && sourceCollisionScan.NextSourceUserId == 0
        && await NoAccountForUsername(sourceCollisionFirst.Values["username"]!),
        "Scanner blocks all SQL-equal source usernames before creating either account, including trailing spaces");
    var blockedSourceColliders = await db.Database.SqlQuery<int>($"""
        SELECT COUNT(*) AS Value FROM source_sync.QuanlyAccounts WHERE InstallationId={installation}
          AND SourceUserId IN ({Id(sourceCollisionFirst)},{Id(sourceCollisionSecond)})
          AND State='blocked' AND Reason='source_username_collision' AND AccountId IS NULL
        """).SingleAsync();
    Check(blockedSourceColliders == 2, "Scanner records both source username conflicts without choosing a first writer");

    foreach (var username in new[] { "帳", "用戶", "qai-" + nonce + "-người" })
    {
        Check(await NoAccountForUsername(username), "Short and Unicode username fixtures have no existing Core identity");
        var supported = User("manager", username);
        await StaffProfile(supported);
        var supportedResult = await Import(supported);
        Check(supportedResult.State == "created" && supportedResult.AccountId is not null
            && (await EmployeeAccount(supportedResult.AccountId.Value)).Username == username,
            "One-character, two-character and Unicode source usernames remain exact and compatible with Core login");
    }
    Check(await db.EmployeePermissions.IgnoreQueryFilters().CountAsync() == permissionCountBefore,
        "Import and replay create no Core permission grants");
    Check(!await db.EmployeeUsers.IgnoreQueryFilters().AnyAsync(x => createdAccounts.Contains(x.Id) && x.IsSuperAdmin),
        "No imported account receives super administrator privileges");
    Check(await db.EmployeeUsers.IgnoreQueryFilters().CountAsync() >= employeeCountBefore
        && await db.CustomerUsers.IgnoreQueryFilters().CountAsync() >= customerCountBefore, "Existing Core login accounts are never deleted");

    var malformedOwned = User("manager");
    await StaffProfile(malformedOwned);
    var malformedOwnedCreated = await Import(malformedOwned);
    var malformedOwnedOriginal = (await EmployeeAccount(malformedOwnedCreated.AccountId!.Value)).PasswordHash;
    var malformedOwnedOutcome = await Import(Replace(malformedOwned, ("is_active", null)));
    var malformedOwnedAccount = await EmployeeAccount(malformedOwnedCreated.AccountId.Value);
    Check(malformedOwnedOutcome.State == "blocked" && malformedOwnedOutcome.Reason == "invalid_source_account_status"
        && malformedOwnedAccount.Status == UserStatus.Inactive && malformedOwnedAccount.PasswordHash == malformedOwnedOriginal,
        "Malformed source account status quarantines an owned active login without replacing its credential");

    var absentStaff = User("manager");
    var absentStaffProfile = await StaffProfile(absentStaff);
    var absentStaffResult = await Import(absentStaff);
    var absentCustomer = User("customer");
    var absentCustomerSourceId = ++nextId;
    var absentCustomerProfile = await CustomerProfile(absentCustomerSourceId);
    var absentCustomerResult = await Import(absentCustomer, Link(absentCustomer, absentCustomerSourceId));

    async Task<Guid> OwnedEmployeeWithManualState(UserStatus status, bool deleted = false, bool superAdmin = false)
    {
        var user = User("manager");
        await StaffProfile(user);
        var imported = await Import(user);
        var account = await EmployeeAccount(imported.AccountId!.Value);
        account.Status = status;
        account.IsDeleted = deleted;
        account.IsSuperAdmin = superAdmin;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return account.Id;
    }
    var absentLockedId = await OwnedEmployeeWithManualState(UserStatus.Locked);
    var absentDeletedId = await OwnedEmployeeWithManualState(UserStatus.Deleted, deleted: true);
    var absentSuperAdminId = await OwnedEmployeeWithManualState(UserStatus.Active, superAdmin: true);
    var otherInstallation = Guid.NewGuid();
    var otherInstallationUser = User("manager");
    await StaffProfile(otherInstallationUser);
    var otherInstallationResult = await importer.ImportAsync(otherInstallation, otherInstallationUser, [], default);
    Check(otherInstallationResult.State == "created" && otherInstallationResult.AccountId is not null,
        "Physical-deletion scope fixture owns an account under another installation");
    createdAccounts.Add(otherInstallationResult.AccountId!.Value);
    db.ChangeTracker.Clear();
    var unownedUser = User("manager");
    var unownedProfile = await StaffProfile(unownedUser);
    var unownedHash = PasswordHasher.Hash("Synthetic unowned Core password " + nonce);
    var unownedAccount = new EmployeeUser { Id = Guid.NewGuid(), EmployeeId = unownedProfile.Id, Username = unownedUser.Values["username"]!,
        PasswordHash = unownedHash, Status = UserStatus.Active };
    db.EmployeeUsers.Add(unownedAccount);
    await db.SaveChangesAsync();
    db.ChangeTracker.Clear();

    // A complete empty image models DELETE/TRUNCATE of source users, not a soft-delete flag.
    raw.Users.Clear();
    raw.Links.Clear();
    raw.StateStatus = "seeding";
    await importer.RunOnceAsync(0, default);
    Check((await EmployeeAccount(absentStaffResult.AccountId!.Value)).Status == UserStatus.Active
        && (await db.CustomerUsers.SingleAsync(x => x.Id == absentCustomerResult.AccountId)).Status == UserStatus.Active,
        "Seeding or not-ready raw state cannot authorize missing-user quarantine");
    db.ChangeTracker.Clear();
    raw.StateStatus = "ready";
    raw.StateReads = 0;
    raw.ChangeGenerationAfterRead = 1;
    await importer.RunOnceAsync(0, default);
    Check((await EmployeeAccount(absentStaffResult.AccountId.Value)).Status == UserStatus.Active
        && (await db.CustomerUsers.SingleAsync(x => x.Id == absentCustomerResult.AccountId)).Status == UserStatus.Active,
        "Generation change during the coherent snapshot check cannot disable owned accounts");
    db.ChangeTracker.Clear();
    raw.StateReads = 0;
    raw.ChangeGenerationAfterRead = 2;
    await importer.RunOnceAsync(0, default);
    Check((await EmployeeAccount(absentStaffResult.AccountId.Value)).Status == UserStatus.Active
        && (await db.CustomerUsers.SingleAsync(x => x.Id == absentCustomerResult.AccountId)).Status == UserStatus.Active,
        "Generation change before an absence mutation prevents stale-snapshot quarantine");
    db.ChangeTracker.Clear();
    raw.ChangeGenerationAfterRead = null;
    raw.StateReads = 0;
    await importer.RunOnceAsync(0, default);
    db.ChangeTracker.Clear();
    Check((await EmployeeAccount(absentStaffResult.AccountId.Value)).Status == UserStatus.Inactive,
        "Physical source user deletion disables the ledger-owned active employee login");
    var deletedSourceCustomerAccount = await db.CustomerUsers.IgnoreQueryFilters().SingleAsync(x => x.Id == absentCustomerResult.AccountId);
    Check(deletedSourceCustomerAccount.Status == UserStatus.Inactive
        && guard.CheckPassword(deletedSourceCustomerAccount, fixturePassword) != SignInResult.Success
        && await db.Customers.AnyAsync(x => x.Id == absentCustomerProfile.Id && x.Status == CustomerStatus.Active && !x.IsDeleted),
        "Physical source user deletion blocks its customer login even when the parent customer remains active");
    Check(await db.Employees.AnyAsync(x => x.Id == absentStaffProfile.Id && !x.IsDeleted && x.Status == EmployeeStatus.Active),
        "Missing login reconciliation never deletes or deactivates its Core employee profile");
    var lockedAfterAbsence = await EmployeeAccount(absentLockedId);
    var deletedAfterAbsence = await EmployeeAccount(absentDeletedId);
    var administratorAfterAbsence = await EmployeeAccount(absentSuperAdminId);
    Check(lockedAfterAbsence.Status == UserStatus.Locked && deletedAfterAbsence.Status == UserStatus.Deleted && deletedAfterAbsence.IsDeleted,
        "Missing source users preserve manual Core account locks and deletions");
    Check(administratorAfterAbsence.IsSuperAdmin && administratorAfterAbsence.Status == UserStatus.Active,
        "Missing source users cannot alter a Core administrator identity");
    Check((await EmployeeAccount(otherInstallationResult.AccountId.Value)).Status == UserStatus.Active,
        "Missing-user quarantine is restricted to the current source installation");
    var unownedAfterAbsence = await EmployeeAccount(unownedAccount.Id);
    Check(unownedAfterAbsence.Status == UserStatus.Active && unownedAfterAbsence.PasswordHash == unownedHash,
        "A missing source row cannot quarantine an unowned Core account");
    var missingLedgerCount = await db.Database.SqlQuery<int>($"""
        SELECT COUNT(*) AS Value FROM source_sync.QuanlyAccounts WHERE InstallationId={installation}
          AND SourceUserId IN ({Id(absentStaff)},{Id(absentCustomer)}) AND State='disabled' AND Reason='source_user_absent'
        """).SingleAsync();
    Check(missingLedgerCount == 2, "Physical-deletion quarantine records its fixed reason for both account kinds");
    await Import(absentCustomer, Link(absentCustomer, absentCustomerSourceId));
    Check((await db.CustomerUsers.SingleAsync(x => x.Id == absentCustomerResult.AccountId)).Status == UserStatus.Inactive,
        "Reappearing source identity never silently reactivates a quarantined Core account");
}
finally
{
    await transaction.RollbackAsync();
}

await using (var verifyRollback = new SqlCommand("SELECT COUNT(*) FROM dbo.EmployeeUsers WHERE Username LIKE @prefix", connection))
{
    verifyRollback.Parameters.AddWithValue("@prefix", "qai-" + nonce + "%");
    Check(Convert.ToInt32(await verifyRollback.ExecuteScalarAsync()) == 0, "Outer rollback removes all synthetic employee login changes");
}
await using (var verifyRollback = new SqlCommand("SELECT COUNT(*) FROM dbo.CustomerUsers WHERE Username LIKE @prefix", connection))
{
    verifyRollback.Parameters.AddWithValue("@prefix", "qai-" + nonce + "%");
    Check(Convert.ToInt32(await verifyRollback.ExecuteScalarAsync()) == 0, "Outer rollback removes all synthetic customer login changes");
}
await using (var verifyRollback = new SqlCommand("SELECT (SELECT COUNT(*) FROM dbo.Employees WHERE EmployeeCode LIKE @prefix) + (SELECT COUNT(*) FROM dbo.Customers WHERE CustomerCode LIKE @prefix)", connection))
{
    verifyRollback.Parameters.AddWithValue("@prefix", "QAI-" + nonce + "%");
    Check(Convert.ToInt32(await verifyRollback.ExecuteScalarAsync()) == 0, "Outer rollback removes synthetic employee and customer profiles");
}
await using (var verifyRollback = new SqlCommand("SELECT COUNT(*) FROM source_sync.QuanlyAccounts WHERE InstallationId=@installation", connection))
{
    verifyRollback.Parameters.AddWithValue("@installation", installation);
    Check(Convert.ToInt32(await verifyRollback.ExecuteScalarAsync()) == 0, "Outer rollback removes all synthetic import ledger entries");
}
await using (var verifyRollback = new SqlCommand("SELECT COUNT(*) FROM dbo.DataChangeLogs WHERE CorrelationId=@correlation", connection))
{
    verifyRollback.Parameters.AddWithValue("@correlation", auditActor.CorrelationId);
    Check(Convert.ToInt32(await verifyRollback.ExecuteScalarAsync()) == 0, "Outer rollback also removes every synthetic audit log");
}
foreach (var id in createdAccounts)
{
    await using var verifyRollback = new SqlCommand("SELECT (SELECT COUNT(*) FROM dbo.EmployeeUsers WHERE Id=@id) + (SELECT COUNT(*) FROM dbo.CustomerUsers WHERE Id=@id)", connection);
    verifyRollback.Parameters.AddWithValue("@id", id);
    Check(Convert.ToInt32(await verifyRollback.ExecuteScalarAsync()) == 0, "Outer rollback removes synthetic account IDs including short Unicode usernames");
}
Console.WriteLine($"PASS {checks} Quanly account import checks; all SQL test mutations rolled back.");

public sealed class FixedOptions<T>(T value) : IOptionsMonitor<T> where T : class
{
    public T CurrentValue => value;
    public T Get(string? name) => value;
    public IDisposable? OnChange(Action<T, string?> listener) => null;
}

public class PermissionFixture : DispatchProxy
{
    protected override object? Invoke(MethodInfo? method, object?[]? args) => method?.Name == nameof(IPermissionService.InvalidateCacheAsync)
        ? Task.CompletedTask : throw new InvalidOperationException("Account import must not infer or read Core authorization grants.");
}

public sealed class SystemActorFixture : ICurrentUserService
{
    public Guid? UserId => null;
    public UserType? UserType => null;
    public string? Username => null;
    public Guid? EmployeeId => null;
    public Guid? CustomerId => null;
    public Guid? ActorId => null;
    public bool IsAuthenticated => false;
    public string? IpAddress => null;
    public string CorrelationId { get; } = "qai-synthetic-" + Guid.NewGuid().ToString("N");
}

public sealed class AccountRawFixture : ISourceSyncRawReader
{
    public Guid Installation { get; } = Guid.NewGuid();
    public Guid Generation { get; } = Guid.NewGuid();
    public List<SourceSyncRawRow> Users { get; } = [];
    public List<SourceSyncRawRow> Links { get; } = [];
    public string StateStatus { get; set; } = "ready";
    public int StateReads { get; set; }
    public int? ChangeGenerationAfterRead { get; set; }
    private Guid ChangedGeneration { get; } = Guid.NewGuid();
    public Task<SourceSyncState?> GetStateAsync(string source, CancellationToken ct)
    {
        var generation = ++StateReads > ChangeGenerationAfterRead ? ChangedGeneration : Generation;
        return Task.FromResult<SourceSyncState?>(new(source, Installation, generation, StateStatus, 2, 0, "applied"));
    }
    public Task<IReadOnlyList<SourceSyncTable>> GetTablesAsync(string source, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<SourceSyncTable>>([new("users", ["id", "username", "hashed_password", "password_salt", "user_role", "is_active", "deleted_at"], ["id"], "synthetic-users"),
            new("customer_users", ["id", "user_id", "customer_id"], ["id"], "synthetic-customer-links")]);
    public Task<IReadOnlyDictionary<string, IReadOnlyList<SourceSyncRawRow>>> ReadProjectionSnapshotAsync(string source, string[] tables, CancellationToken ct) =>
        Task.FromResult<IReadOnlyDictionary<string, IReadOnlyList<SourceSyncRawRow>>>(tables.ToDictionary(table => table,
            table => (IReadOnlyList<SourceSyncRawRow>)(table switch { "users" => Users.ToArray(), "customer_users" => Links.ToArray(), _ => throw new InvalidOperationException("Unexpected account snapshot table.") }), StringComparer.Ordinal));
    public Task<SourceSyncRawRow?> ReadRowAsync(string source, string table, IReadOnlyDictionary<string, string?> key, CancellationToken ct) =>
        Task.FromResult((table == "users" ? Users : Links).SingleOrDefault(row => row.Values["id"] == key["id"]));
    public async IAsyncEnumerable<SourceSyncRawRow> ReadRowsAsync(string source, string table, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    { await Task.CompletedTask; foreach (var row in table == "users" ? Users : Links) yield return row; }
    public Task<SourceSyncRawPage> ReadPageAsync(string source, string table, string? after, int limit, IReadOnlyDictionary<string, string?>? filters, string? search, string[]? columns, CancellationToken ct) =>
        throw new InvalidOperationException("Direct account tests require no raw paging.");
    public Task<IReadOnlyList<SourceSyncPendingEvent>> ReadPendingAsync(string source, int limit, CancellationToken ct) => throw new NotSupportedException();
    public Task SetProjectionStateAsync(string source, Guid installation, string eventId, string state, string? error, CancellationToken ct) => throw new NotSupportedException();
    public Task SetBaselineProjectionStateAsync(string source, Guid generation, string state, string? error, CancellationToken ct) => throw new NotSupportedException();
}
