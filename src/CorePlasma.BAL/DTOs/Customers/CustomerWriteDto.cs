using CorePlasma.Common.Enums;

namespace CorePlasma.BAL.DTOs.Customers;

/// <summary>
/// Fields shared by create and update requests.
/// </summary>
public abstract class CustomerWriteDto
{
    /// <summary>
    /// Leave empty on create to have the system generate it (DocumentSequences).
    /// </summary>
    public string CustomerCode { get; set; } = string.Empty;

    public string CustomerName { get; set; } = string.Empty;

    public string? ShortName { get; set; }

    public string? TaxCode { get; set; }

    public string? Address { get; set; }

    public string? Phone { get; set; }

    public string? Email { get; set; }

    public string? ContactPerson { get; set; }

    public string? ContactPhone { get; set; }

    public string? ContactEmail { get; set; }

    public string? Website { get; set; }

    public string? Description { get; set; }

    public CustomerStatus Status { get; set; } = CustomerStatus.Active;
}

public sealed class CreateCustomerDto : CustomerWriteDto;

public sealed class UpdateCustomerDto : CustomerWriteDto;
