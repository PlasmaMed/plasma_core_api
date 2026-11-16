using System.Text.Json;
using System.Text.Json.Serialization;
using CorePlasma.Common.Interfaces;
using CorePlasma.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CorePlasma.DAL.Interceptors;

/// <summary>
/// Writes a <see cref="DataChangeLog"/> row for every added, modified or deleted entity, inside the same
/// SaveChanges (so the log and the data commit or roll back together). Must be registered after
/// <see cref="AuditableEntityInterceptor"/> so soft deletes are already converted.
/// </summary>
public sealed class DataChangeLogInterceptor(ICurrentUserService currentUser) : SaveChangesInterceptor
{
    public const string Created = "Created";
    public const string Updated = "Updated";
    public const string Deleted = "Deleted";

    private const string Masked = "***";

    // Covered by the log row itself (who/when), so not repeated inside Changes.
    private static readonly HashSet<string> IgnoredProperties =
    [
        nameof(BaseEntity.CreatedDate), nameof(BaseEntity.CreatedBy),
        nameof(BaseEntity.UpdatedDate), nameof(BaseEntity.UpdatedBy)
    ];

    private static readonly HashSet<string> SecretProperties = ["PasswordHash"];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        AddLogs(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        AddLogs(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void AddLogs(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        context.ChangeTracker.DetectChanges();

        var now = DateTime.UtcNow;
        var logs = new List<DataChangeLog>();

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.Entity is DataChangeLog or PermissionAuditLog
                || entry.State is EntityState.Unchanged or EntityState.Detached)
            {
                continue;
            }

            var action = GetAction(entry);
            var changes = GetChanges(entry, action);
            if (action == Updated && changes.Count == 0)
            {
                continue;
            }

            logs.Add(new DataChangeLog
            {
                EntityName = entry.Metadata.GetTableName() ?? entry.Metadata.ClrType.Name,
                EntityId = string.Join(",", entry.Metadata.FindPrimaryKey()!.Properties.Select(p => entry.Property(p.Name).CurrentValue)),
                Action = action,
                Changes = JsonSerializer.Serialize(changes, JsonOptions),
                ChangedBy = currentUser.ActorId,
                ChangedByUsername = currentUser.Username,
                ChangedByUserType = currentUser.UserType?.ToString(),
                ChangedDate = now,
                IpAddress = currentUser.IpAddress,
                CorrelationId = currentUser.CorrelationId
            });
        }

        if (logs.Count > 0)
        {
            context.Set<DataChangeLog>().AddRange(logs);
        }
    }

    private static string GetAction(EntityEntry entry)
    {
        if (entry.State == EntityState.Added)
        {
            return Created;
        }

        if (entry.State == EntityState.Deleted)
        {
            return Deleted;
        }

        // Soft delete: AuditableEntityInterceptor turned the delete into IsDeleted false -> true.
        if (entry.Entity is BaseEntity)
        {
            var isDeleted = entry.Property(nameof(BaseEntity.IsDeleted));
            if (isDeleted.OriginalValue is false && isDeleted.CurrentValue is true)
            {
                return Deleted;
            }
        }

        return Updated;
    }

    private static Dictionary<string, ValueChange> GetChanges(EntityEntry entry, string action)
    {
        var changes = new Dictionary<string, ValueChange>();

        foreach (var property in entry.Properties)
        {
            var name = property.Metadata.Name;
            if (IgnoredProperties.Contains(name))
            {
                continue;
            }

            object? oldValue = entry.State == EntityState.Added ? null : property.OriginalValue;
            object? newValue = entry.State == EntityState.Deleted ? null : property.CurrentValue;

            if (entry.State == EntityState.Modified && (!property.IsModified || Equals(oldValue, newValue)))
            {
                continue;
            }

            if (action == Created && newValue is null)
            {
                continue;
            }

            if (SecretProperties.Contains(name))
            {
                oldValue = oldValue is null ? null : Masked;
                newValue = newValue is null ? null : Masked;
            }

            changes[name] = new ValueChange(oldValue, newValue);
        }

        return changes;
    }

    private sealed record ValueChange(
        [property: JsonPropertyName("old")] object? Old,
        [property: JsonPropertyName("new")] object? New);
}
