using CorePlasma.API.Authorization;
using CorePlasma.BAL.DTOs.Employees;
using CorePlasma.BAL.Interfaces;
using CorePlasma.Common.Constants;
using CorePlasma.Common.Responses;
using Microsoft.AspNetCore.Mvc;

namespace CorePlasma.API.Controllers;

[Route("api/employees")]
public sealed class EmployeesController(IEmployeeService employeeService) : ApiControllerBase
{
    /// <summary>Paged list; filter by Department, Position, Status, Gender.</summary>
    [HttpGet]
    [HasPermission(PermissionCodes.Employee.View)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<EmployeeDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PagedResult<EmployeeDto>>>> GetEmployees(
        [FromQuery] EmployeeFilterDto filter,
        CancellationToken cancellationToken) =>
        Success(await employeeService.GetPagedAsync(filter, cancellationToken));

    [HttpGet("{id:guid}")]
    [HasPermission(PermissionCodes.Employee.View)]
    [ProducesResponseType(typeof(ApiResponse<EmployeeDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<EmployeeDetailDto>>> GetEmployee(Guid id, CancellationToken cancellationToken) =>
        Success(await employeeService.GetByIdAsync(id, cancellationToken));

    [HttpPost]
    [HasPermission(PermissionCodes.Employee.Create)]
    [ProducesResponseType(typeof(ApiResponse<EmployeeDetailDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<EmployeeDetailDto>>> CreateEmployee(
        [FromBody] CreateEmployeeDto dto,
        CancellationToken cancellationToken)
    {
        var result = await employeeService.CreateAsync(dto, cancellationToken);
        return Created(nameof(GetEmployee), new { id = result.Id }, result);
    }

    [HttpPut("{id:guid}")]
    [HasPermission(PermissionCodes.Employee.Update)]
    [ProducesResponseType(typeof(ApiResponse<EmployeeDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<EmployeeDetailDto>>> UpdateEmployee(
        Guid id,
        [FromBody] UpdateEmployeeDto dto,
        CancellationToken cancellationToken) =>
        Success(await employeeService.UpdateAsync(id, dto, cancellationToken));

    [HttpDelete("{id:guid}")]
    [HasPermission(PermissionCodes.Employee.Delete)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse>> DeleteEmployee(Guid id, CancellationToken cancellationToken)
    {
        await employeeService.DeleteAsync(id, cancellationToken);
        return Success("Deleted");
    }
}
