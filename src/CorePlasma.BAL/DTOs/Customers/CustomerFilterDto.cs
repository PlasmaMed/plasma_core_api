using CorePlasma.Common.Enums;
using CorePlasma.Common.Models;

namespace CorePlasma.BAL.DTOs.Customers;

public sealed class CustomerFilterDto : PagedRequest
{
    public CustomerStatus? Status { get; set; }

    public string? TaxCode { get; set; }
}
