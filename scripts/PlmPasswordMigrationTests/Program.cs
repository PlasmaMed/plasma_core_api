using System.Security.Cryptography;
using System.Text;
using CorePlasma.BAL.Services;
using CorePlasma.Common.Enums;
using CorePlasma.Common.Helpers;
using CorePlasma.Common.Models;
using CorePlasma.DAL.Entities;
using KiteSoft.Common;
using Microsoft.Extensions.Options;

var checks = 0;
void Check(bool condition, string label)
{
    if (!condition) throw new InvalidOperationException(label);
    checks++;
    Console.WriteLine($"PASS: {label}");
}
const string prefix = "PLM-HMACSHA512$";
string Tag(byte[] salt, byte[] hash) => $"{prefix}{Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
string Fixture(string password, byte[]? salt = null)
{
    salt ??= Enumerable.Range(0, 128).Select(i => (byte)i).ToArray();
    return Tag(salt, HMACSHA512.HashData(salt, Encoding.UTF8.GetBytes(password)));
}
const string password = "SYNTHETIC-ONLY: mật khẩu thử nghiệm 🔐";
var tagged = Fixture(password);
Check(tagged.Length <= 500, "Tagged format fits both account columns");
Check(PasswordHasher.IsLegacyPlmHmacSha512Hash(tagged), "Well-formed PLM tag is recognized");
Check(PasswordHasher.Verify(password, tagged), "Synthetic UTF-8 PLM vector verifies");
Check(!PasswordHasher.Verify(password + "x", tagged), "Wrong PLM password is rejected");
Check(!PasswordHasher.Verify("", tagged), "Empty PLM password is rejected");
Check(!PasswordHasher.Verify("   ", Fixture("   ")), "PLM whitespace policy remains rejected even with matching material");
Check(!PasswordHasher.Verify("", Fixture("")), "Matching empty-password material cannot authenticate");
Check(!PasswordHasher.Verify(null!, tagged), "Null PLM password fails safely");
Check(!PasswordHasher.Verify(" ", Fixture("   ")), "Different whitespace bytes do not authenticate");
Check(PasswordHasher.Verify("  " + password + "  ", Fixture("  " + password + "  ")), "Leading and trailing password spaces are preserved");
Check(!PasswordHasher.Verify(password, Fixture("  " + password + "  ")), "Password spaces are never trimmed during HMAC verification");

EncryptionHelper.CreatePasswordHash(password, out var sourceHash, out var sourceSalt);
Check(sourceSalt.Length == 128 && sourceHash.Length == 64, "Actual source helper produces expected field sizes");
var fromSource = Tag(sourceSalt, sourceHash);
Check(PasswordHasher.Verify(password, fromSource), "Actual PLM source helper output verifies in Core");
Check(EncryptionHelper.VerifyPasswordHash(password, sourceHash, sourceSalt), "Source helper independently accepts same fixture");
Check(!PasswordHasher.Verify(password + "x", fromSource), "Actual source helper fixture rejects wrong password");

var longPassword = string.Concat(Enumerable.Repeat("SYNTHETIC-ONLY-đ🔒", 20));
var longTagged = Fixture(longPassword);
Check(PasswordHasher.Verify(longPassword, longTagged), "Long UTF-8 PLM passwords verify without bcrypt truncation");
Check(!PasswordHasher.Verify(longPassword + "x", longTagged), "PLM comparison includes suffix after the first 72 bytes");
var corruptSalt = Enumerable.Range(0, 128).Select(i => (byte)i).ToArray();
corruptSalt[127] ^= 1;
Check(!PasswordHasher.Verify(password, Tag(corruptSalt, HMACSHA512.HashData(Enumerable.Range(0, 128).Select(i => (byte)i).ToArray(), Encoding.UTF8.GetBytes(password)))), "Corrupted source salt fails authentication");

var saltText = Convert.ToBase64String(Enumerable.Range(0, 128).Select(i => (byte)i).ToArray());
var hashText = Convert.ToBase64String(HMACSHA512.HashData(Enumerable.Range(0, 128).Select(i => (byte)i).ToArray(), Encoding.UTF8.GetBytes(password)));
const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
var noncanonicalSalt = saltText[..170] + alphabet[alphabet.IndexOf(saltText[170]) + 1] + "=";
var noncanonicalHash = hashText[..85] + alphabet[alphabet.IndexOf(hashText[85]) + 1] + "==";
(string? Value, string Label)[] malformed =
[
    (null, "null"), ("", "empty"), (prefix, "tag without payload"),
    (tagged[..^1], "truncated tag"), (tagged + "x", "trailing content"),
    ("plm-hmacsha512$" + tagged[prefix.Length..], "wrong tag case"),
    (tagged.Replace(prefix, "OTHER-HMACSHA512$", StringComparison.Ordinal), "unknown tag"),
    (prefix + saltText + hashText, "missing separator"),
    (prefix + saltText + "$$" + hashText, "extra separator"),
    (prefix + saltText[..40] + "$" + saltText[41..] + "$" + hashText, "separator inside salt"),
    (prefix + "!" + saltText[1..] + "$" + hashText, "invalid salt base64"),
    (prefix + saltText + "$!" + hashText[1..], "invalid hash base64"),
    (prefix + saltText[..40] + " " + saltText[41..] + "$" + hashText, "whitespace inside salt"),
    (prefix + saltText + "$" + hashText[..40] + " " + hashText[41..], "whitespace inside hash"),
    (Tag(new byte[127], new byte[64]), "127-byte salt"),
    (Tag(new byte[129], new byte[64]), "129-byte salt"),
    (Tag(new byte[128], new byte[63]), "63-byte hash"),
    (Tag(new byte[128], new byte[65]), "65-byte hash"),
    (prefix + noncanonicalSalt + "$" + hashText, "noncanonical salt encoding"),
    (prefix + saltText + "$" + noncanonicalHash, "noncanonical hash encoding"),
    (prefix + new string('A', 1_000_000), "oversized tag")
];
foreach (var item in malformed)
{
    Check(!PasswordHasher.IsLegacyPlmHmacSha512Hash(item.Value), $"PLM format rejects {item.Label}");
    Check(!PasswordHasher.Verify(password, item.Value), $"Authentication rejects {item.Label}");
}

var pbkdf2 = PasswordHasher.Hash(password);
Check(PasswordHasher.Verify(password, pbkdf2), "Existing PBKDF2 correct password still verifies");
Check(!PasswordHasher.Verify(password + "x", pbkdf2), "Existing PBKDF2 wrong password remains rejected");
Check(!PasswordHasher.IsLegacyPlmHmacSha512Hash(pbkdf2), "PBKDF2 is not marked legacy PLM");
var bcrypt = BCrypt.Net.BCrypt.HashPassword(password, workFactor: 4);
Check(PasswordHasher.IsLegacyBcryptHash(bcrypt), "Existing bcrypt format detection remains available");
Check(PasswordHasher.Verify(password, bcrypt), "Existing imported bcrypt password still verifies");
Check(!PasswordHasher.Verify(password + "x", bcrypt), "Existing bcrypt wrong password remains rejected");
Check(!PasswordHasher.Verify(password, "$2b$99$" + new string('A', 53)), "Unbounded bcrypt cost remains rejected");

UserAccountGuard Guard(int attempts = 2) => new(Options.Create(new LoginSecuritySettings { MaxFailedAttempts = attempts, LockoutMinutes = 1 }));
TestAccount Account(string hash) => new() { PasswordHash = hash, Status = UserStatus.Active };
var success = Account(tagged);
success.AccessFailedCount = 1;
success.LockoutEnd = DateTime.UtcNow.AddMinutes(-1);
Check(Guard().CheckPassword(success, password) == SignInResult.Success, "PLM sign-in succeeds only after verification");
Check(success.PasswordHash != tagged && !PasswordHasher.IsLegacyPlmHmacSha512Hash(success.PasswordHash), "Successful PLM sign-in replaces legacy hash");
Check(success.PasswordHash.StartsWith("100000.", StringComparison.Ordinal) && PasswordHasher.Verify(password, success.PasswordHash), "PLM credential upgrades to current PBKDF2 format");
Check(success.AccessFailedCount == 0 && success.LockoutEnd is null && success.LastLoginDate is not null, "Successful migration keeps account bookkeeping");

var wrong = Account(tagged);
Check(Guard().CheckPassword(wrong, "SYNTHETIC-WRONG") == SignInResult.InvalidCredentials, "Wrong PLM password follows normal failure policy");
Check(wrong.PasswordHash == tagged && wrong.AccessFailedCount == 1 && wrong.LastLoginDate is null, "Failure preserves source credential and increments failures");
Check(Guard().CheckPassword(wrong, "SYNTHETIC-WRONG") == SignInResult.InvalidCredentials && wrong.LockoutEnd > DateTime.UtcNow, "Repeated PLM failures trigger normal lockout");
Check(Guard().CheckPassword(wrong, password) == SignInResult.LockedOut && wrong.PasswordHash == tagged, "Correct password cannot bypass temporary lockout");

foreach (var status in new[] { UserStatus.Inactive, UserStatus.Locked, UserStatus.Deleted })
{
    var disabled = Account(tagged); disabled.Status = status;
    Check(Guard().CheckPassword(disabled, password) == SignInResult.NotActive && disabled.PasswordHash == tagged && disabled.LastLoginDate is null,
        $"PLM import cannot bypass account status {status}");
}
var invalid = Account(prefix + "malformed");
Check(Guard().CheckPassword(invalid, password) == SignInResult.InvalidCredentials && invalid.PasswordHash == prefix + "malformed", "Malformed imported credential cannot be upgraded or authenticated");
var oldBcrypt = Account(bcrypt);
Check(Guard().CheckPassword(oldBcrypt, password) == SignInResult.Success && oldBcrypt.PasswordHash.StartsWith("100000.", StringComparison.Ordinal) && PasswordHasher.Verify(password, oldBcrypt.PasswordHash), "Existing bcrypt rehash-on-success remains intact");
var current = Account(pbkdf2);
Check(Guard().CheckPassword(current, password) == SignInResult.Success && current.PasswordHash == pbkdf2, "Existing PBKDF2 successful login does not change hash");
Console.WriteLine($"TOTAL: {checks} synthetic checks passed; no production database or credentials used.");

sealed class TestAccount : IUserAccount
{
    public Guid Id { get; } = Guid.NewGuid();
    public string Username { get; set; } = "SYNTHETIC-ONLY";
    public string PasswordHash { get; set; } = "";
    public UserStatus Status { get; set; }
    public int AccessFailedCount { get; set; }
    public DateTime? LockoutEnd { get; set; }
    public DateTime? LastLoginDate { get; set; }
}
