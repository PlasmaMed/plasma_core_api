namespace CorePlasma.DAL.Entities;

/// <summary>
/// Immutable history of every insert/update/delete, written automatically by
/// <see cref="Interceptors.DataChangeLogInterceptor"/> in the same transaction as the change.
/// No FK so the history survives changes to the logged rows.
/// </summary>
public class DataChangeLog
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>Table name, e.g. "Customers".</summary>
    public string EntityName { get; set; } = string.Empty;

    /// <summary>Primary key of the changed row (comma-joined for composite keys).</summary>
    public string EntityId { get; set; } = string.Empty;

    /// <summary>Created / Updated / Deleted.</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>JSON object: { "Field": { "old": ..., "new": ... } }. Secrets are masked.</summary>
    public string Changes { get; set; } = "{}";

    /// <summary>Same id as CreatedBy/UpdatedBy (EmployeeId or CustomerUsers.Id); null for system changes.</summary>
    public Guid? ChangedBy { get; set; }

    public string? ChangedByUsername { get; set; }

    public string? ChangedByUserType { get; set; }

    public DateTime ChangedDate { get; set; }

    public string? IpAddress { get; set; }

    public string? CorrelationId { get; set; }
}
