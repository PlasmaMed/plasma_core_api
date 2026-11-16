using System.Text.Json;
using System.Text.Json.Serialization;
using CorePlasma.Common.Models;

namespace CorePlasma.BAL.DTOs.DataChangeLogs;

public sealed class DataChangeLogDto
{
    public Guid Id { get; set; }

    public string EntityName { get; set; } = string.Empty;

    public string EntityId { get; set; } = string.Empty;

    public string Action { get; set; } = string.Empty;

    /// <summary>{ "Field": { "old": ..., "new": ... } }</summary>
    public JsonElement Changes => JsonDocument.Parse(RawChanges).RootElement;

    [JsonIgnore]
    public string RawChanges { get; set; } = "{}";

    public Guid? ChangedBy { get; set; }

    public string? ChangedByUsername { get; set; }

    public string? ChangedByUserType { get; set; }

    public DateTime ChangedDate { get; set; }

    public string? IpAddress { get; set; }

    public string? CorrelationId { get; set; }
}

public sealed class DataChangeLogFilterDto : PagedRequest
{
    /// <summary>Table name, e.g. Customers, Tanks, Engines, EmployeeUsers.</summary>
    public string? EntityName { get; set; }

    public string? EntityId { get; set; }

    /// <summary>Created / Updated / Deleted.</summary>
    public string? Action { get; set; }

    public Guid? ChangedBy { get; set; }

    public DateTime? FromDate { get; set; }

    public DateTime? ToDate { get; set; }
}
