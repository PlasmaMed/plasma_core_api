using CorePlasma.API.Authorization;
using CorePlasma.BAL.DTOs.Engines;
using CorePlasma.BAL.DTOs.Imports;
using CorePlasma.BAL.Interfaces;
using CorePlasma.Common.Constants;
using CorePlasma.Common.Responses;
using Microsoft.AspNetCore.Mvc;

namespace CorePlasma.API.Controllers;

/// <summary>Máy / Máy Plasma.</summary>
[Route("api/engines")]
public sealed class EnginesController(IEngineService engineService, IEquipmentImportService importService) : ApiControllerBase
{
    [HttpGet]
    [HasPermission(PermissionCodes.Engine.View)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<EngineDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PagedResult<EngineDto>>>> GetEngines(
        [FromQuery] EngineFilterDto filter,
        CancellationToken cancellationToken) =>
        Success(await engineService.GetPagedAsync(filter, cancellationToken));

    [HttpGet("{id:guid}")]
    [HasPermission(PermissionCodes.Engine.View)]
    [ProducesResponseType(typeof(ApiResponse<EngineDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<EngineDetailDto>>> GetEngine(Guid id, CancellationToken cancellationToken) =>
        Success(await engineService.GetByIdAsync(id, cancellationToken));

    [HttpPost]
    [HasPermission(PermissionCodes.Engine.Create)]
    [ProducesResponseType(typeof(ApiResponse<EngineDetailDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<EngineDetailDto>>> CreateEngine(
        [FromBody] CreateEngineDto dto,
        CancellationToken cancellationToken)
    {
        var result = await engineService.CreateAsync(dto, cancellationToken);
        return Created(nameof(GetEngine), new { id = result.Id }, result);
    }

    [HttpPut("{id:guid}")]
    [HasPermission(PermissionCodes.Engine.Update)]
    [ProducesResponseType(typeof(ApiResponse<EngineDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<EngineDetailDto>>> UpdateEngine(
        Guid id,
        [FromBody] UpdateEngineDto dto,
        CancellationToken cancellationToken) =>
        Success(await engineService.UpdateAsync(id, dto, cancellationToken));

    [HttpDelete("{id:guid}")]
    [HasPermission(PermissionCodes.Engine.Delete)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse>> DeleteEngine(Guid id, CancellationToken cancellationToken)
    {
        await engineService.DeleteAsync(id, cancellationToken);
        return Success("Deleted");
    }

    /// <summary>
    /// Import from the PlasmaMED Excel export (.xlsx): the sheet whose header has serial, equipmentIdx, _id.
    /// All-or-nothing; re-importing updates existing records (matched by _id, then serial). Every change is logged.
    /// </summary>
    [HttpPost("import")]
    [HasPermission(PermissionCodes.Engine.Import)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(50_000_000)]
    [ProducesResponseType(typeof(ApiResponse<ImportResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<ImportResultDto>>> ImportEngines(IFormFile file, CancellationToken cancellationToken)
    {
        await using var excel = await ExcelUpload.ToMemoryStreamAsync(file, cancellationToken);
        return Success(await importService.ImportEnginesAsync(excel, cancellationToken), "Imported");
    }
}
