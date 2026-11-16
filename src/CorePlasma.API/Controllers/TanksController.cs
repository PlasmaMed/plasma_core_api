using CorePlasma.API.Authorization;
using CorePlasma.BAL.DTOs.Tanks;
using CorePlasma.BAL.DTOs.Imports;
using CorePlasma.BAL.Interfaces;
using CorePlasma.Common.Constants;
using CorePlasma.Common.Responses;
using Microsoft.AspNetCore.Mvc;

namespace CorePlasma.API.Controllers;

/// <summary>Bình khí.</summary>
[Route("api/tanks")]
public sealed class TanksController(ITankService tankService, IEquipmentImportService importService) : ApiControllerBase
{
    /// <summary>Paged list; filter by TankCode, SerialNumber, GasType, Customer, Employee, CurrentLocation, Status.</summary>
    [HttpGet]
    [HasPermission(PermissionCodes.Tank.View)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<TankDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PagedResult<TankDto>>>> GetTanks(
        [FromQuery] TankFilterDto filter,
        CancellationToken cancellationToken) =>
        Success(await tankService.GetPagedAsync(filter, cancellationToken));

    [HttpGet("{id:guid}")]
    [HasPermission(PermissionCodes.Tank.View)]
    [ProducesResponseType(typeof(ApiResponse<TankDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<TankDetailDto>>> GetTank(Guid id, CancellationToken cancellationToken) =>
        Success(await tankService.GetByIdAsync(id, cancellationToken));

    [HttpPost]
    [HasPermission(PermissionCodes.Tank.Create)]
    [ProducesResponseType(typeof(ApiResponse<TankDetailDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<TankDetailDto>>> CreateTank(
        [FromBody] CreateTankDto dto,
        CancellationToken cancellationToken)
    {
        var result = await tankService.CreateAsync(dto, cancellationToken);
        return Created(nameof(GetTank), new { id = result.Id }, result);
    }

    [HttpPut("{id:guid}")]
    [HasPermission(PermissionCodes.Tank.Update)]
    [ProducesResponseType(typeof(ApiResponse<TankDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<TankDetailDto>>> UpdateTank(
        Guid id,
        [FromBody] UpdateTankDto dto,
        CancellationToken cancellationToken) =>
        Success(await tankService.UpdateAsync(id, dto, cancellationToken));

    [HttpDelete("{id:guid}")]
    [HasPermission(PermissionCodes.Tank.Delete)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse>> DeleteTank(Guid id, CancellationToken cancellationToken)
    {
        await tankService.DeleteAsync(id, cancellationToken);
        return Success("Deleted");
    }

    /// <summary>
    /// Import from the PlasmaMED Excel export (.xlsx): the sheet whose header has serial, tankIdx, _id.
    /// All-or-nothing; re-importing updates existing records (matched by _id, then serial). Every change is logged.
    /// </summary>
    [HttpPost("import")]
    [HasPermission(PermissionCodes.Tank.Import)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(50_000_000)]
    [ProducesResponseType(typeof(ApiResponse<ImportResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<ImportResultDto>>> ImportTanks(IFormFile file, CancellationToken cancellationToken)
    {
        await using var excel = await ExcelUpload.ToMemoryStreamAsync(file, cancellationToken);
        return Success(await importService.ImportTanksAsync(excel, cancellationToken), "Imported");
    }
}
