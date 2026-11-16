using CorePlasma.Common.Enums;

namespace CorePlasma.BAL.DTOs.Customers;

public class CustomerDto
{
    /// <summary>Read-only source identifiers. Core Id is independent.</summary>
    public long? QuanLyId { get; set; }

    public long? NewAppId { get; set; }

    public Guid Id { get; set; }

    public string CustomerCode { get; set; } = string.Empty;

    public string CustomerName { get; set; } = string.Empty;

    public string? ShortName { get; set; }

    public string? TaxCode { get; set; }

    public string? Phone { get; set; }

    public string? Email { get; set; }

    public string? ContactPerson { get; set; }

    public CustomerStatus Status { get; set; }

    public DateTime CreatedDate { get; set; }
}
