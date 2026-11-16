using CorePlasma.Common.Constants;
using CorePlasma.Common.Helpers;
using CorePlasma.Common.Models;
using CorePlasma.DAL.Context;
using CorePlasma.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CorePlasma.DAL.Seed;

/// <summary>
/// Idempotent seeder: inserts missing permissions from <see cref="PermissionDefinitions"/>
/// and bootstraps the Super Admin account from configuration.
/// </summary>
public sealed class DatabaseSeeder(
    ApplicationDbContext context,
    IOptions<SuperAdminSettings> superAdminOptions,
    ILogger<DatabaseSeeder> logger)
{
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        await SeedPermissionsAsync(cancellationToken);
        await SeedDocumentSequencesAsync(cancellationToken);
        await SeedSuperAdminAsync(cancellationToken);
    }

    private async Task SeedPermissionsAsync(CancellationToken cancellationToken)
    {
        var existingCodes = await context.Permissions
            .IgnoreQueryFilters()
            .Select(x => x.PermissionCode)
            .ToListAsync(cancellationToken);

        var existing = existingCodes.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missing = PermissionDefinitions.All
            .Where(d => !existing.Contains(d.PermissionCode))
            .Select(d => new Permission
            {
                PermissionCode = d.PermissionCode,
                PermissionName = d.PermissionName,
                ModuleCode = d.ModuleCode,
                FunctionCode = d.FunctionCode,
                ActionCode = d.ActionCode,
                SortOrder = d.SortOrder,
                IsActive = true
            })
            .ToList();

        if (missing.Count == 0)
        {
            return;
        }

        context.Permissions.AddRange(missing);
        await context.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Seeded {Count} permissions", missing.Count);
    }

    private async Task SeedDocumentSequencesAsync(CancellationToken cancellationToken)
    {
        var existingCodes = await context.DocumentSequences
            .IgnoreQueryFilters()
            .Select(x => x.SequenceCode)
            .ToListAsync(cancellationToken);

        var existing = existingCodes.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missing = SequenceDefinitions.All
            .Where(d => !existing.Contains(d.SequenceCode))
            .Select(d => new DocumentSequence
            {
                SequenceCode = d.SequenceCode,
                SequenceName = d.SequenceName,
                Prefix = d.Prefix,
                Separator = d.Separator,
                DateFormat = d.DateFormat,
                Padding = d.Padding,
                ResetPolicy = d.ResetPolicy
            })
            .ToList();

        if (missing.Count == 0)
        {
            return;
        }

        context.DocumentSequences.AddRange(missing);
        await context.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Seeded {Count} document sequences", missing.Count);
    }

    private async Task SeedSuperAdminAsync(CancellationToken cancellationToken)
    {
        var settings = superAdminOptions.Value;
        if (string.IsNullOrWhiteSpace(settings.EmployeeCode) || string.IsNullOrWhiteSpace(settings.Username))
        {
            logger.LogWarning("SuperAdmin:EmployeeCode/Username is not configured; skipping Super Admin seed");
            return;
        }

        var employee = await context.Employees
            .Include(x => x.User)
            .FirstOrDefaultAsync(x => x.EmployeeCode == settings.EmployeeCode, cancellationToken);

        if (employee is null)
        {
            employee = new Employee
            {
                EmployeeCode = settings.EmployeeCode,
                FullName = settings.FullName,
                Email = settings.Email
            };
            context.Employees.Add(employee);
            logger.LogInformation("Created Super Admin employee {EmployeeCode}", settings.EmployeeCode);
        }

        if (employee.User is null)
        {
            if (string.IsNullOrWhiteSpace(settings.Password))
            {
                await context.SaveChangesAsync(cancellationToken);
                logger.LogWarning("SuperAdmin:Password is not configured; Super Admin account not created");
                return;
            }

            context.EmployeeUsers.Add(new EmployeeUser
            {
                EmployeeId = employee.Id,
                Username = settings.Username,
                PasswordHash = PasswordHasher.Hash(settings.Password),
                IsSuperAdmin = true
            });
            logger.LogInformation("Created Super Admin account {Username}", settings.Username);
        }
        else if (!employee.User.IsSuperAdmin)
        {
            employee.User.IsSuperAdmin = true;
            logger.LogInformation("Promoted account {Username} to Super Admin", employee.User.Username);
        }

        await context.SaveChangesAsync(cancellationToken);
    }
}
