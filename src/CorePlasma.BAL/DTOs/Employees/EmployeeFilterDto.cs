using CorePlasma.Common.Enums;
using CorePlasma.Common.Models;

namespace CorePlasma.BAL.DTOs.Employees;

public sealed class EmployeeFilterDto : PagedRequest
{
    public string? Department { get; set; }

    public string? Position { get; set; }

    public EmployeeStatus? Status { get; set; }

    public Gender? Gender { get; set; }
}
