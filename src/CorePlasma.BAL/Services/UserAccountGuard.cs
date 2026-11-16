using CorePlasma.Common.Enums;
using CorePlasma.Common.Helpers;
using CorePlasma.Common.Models;
using CorePlasma.DAL.Entities;
using Microsoft.Extensions.Options;

namespace CorePlasma.BAL.Services;

public enum SignInResult
{
    Success,
    InvalidCredentials,
    LockedOut,
    NotActive
}

/// <summary>
/// Password verification and failed-attempt lockout shared by EmployeeUsers and CustomerUsers.
/// Mutates the tracked account; the caller saves.
/// </summary>
public sealed class UserAccountGuard(IOptions<LoginSecuritySettings> options)
{
    public SignInResult CheckPassword(IUserAccount account, string password)
    {
        var now = DateTime.UtcNow;

        if (account.Status != UserStatus.Active)
        {
            return SignInResult.NotActive;
        }

        if (account.LockoutEnd > now)
        {
            return SignInResult.LockedOut;
        }

        if (!PasswordHasher.Verify(password, account.PasswordHash))
        {
            var settings = options.Value;
            account.AccessFailedCount++;
            if (account.AccessFailedCount >= Math.Max(1, settings.MaxFailedAttempts))
            {
                account.LockoutEnd = now.AddMinutes(Math.Max(1, settings.LockoutMinutes));
                account.AccessFailedCount = 0;
            }

            return SignInResult.InvalidCredentials;
        }

        if (PasswordHasher.IsLegacyBcryptHash(account.PasswordHash)
            || PasswordHasher.IsLegacyPlmHmacSha512Hash(account.PasswordHash))
        {
            // Keep the legacy credential until verified successfully; the caller saves the upgrade atomically.
            account.PasswordHash = PasswordHasher.Hash(password);
        }

        account.AccessFailedCount = 0;
        account.LockoutEnd = null;
        account.LastLoginDate = now;
        return SignInResult.Success;
    }

    public static void SetPassword(IUserAccount account, string newPassword)
    {
        account.PasswordHash = PasswordHasher.Hash(newPassword);
        account.AccessFailedCount = 0;
        account.LockoutEnd = null;
    }

    public static void Lock(IUserAccount account) => account.Status = UserStatus.Locked;

    public static void Unlock(IUserAccount account)
    {
        account.Status = UserStatus.Active;
        account.AccessFailedCount = 0;
        account.LockoutEnd = null;
    }
}
