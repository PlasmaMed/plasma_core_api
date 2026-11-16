using CorePlasma.API.Authorization;
using CorePlasma.BAL.DTOs.Common;
using CorePlasma.BAL.DTOs.TankTransfers;
using CorePlasma.BAL.Interfaces;
using CorePlasma.Common.Constants;
using CorePlasma.Common.Responses;
using Microsoft.AspNetCore.Mvc;

namespace CorePlasma.API.Controllers;

/// <summary>
/// Phiếu điều chuyển bình khí. Workflow: create (Draft) → submit → approve / reject; cancel before approval.
/// Approval moves the tanks to the destination.
/// </summary>
[Route("api/tank-transfers")]
public sealed class TankTransfersController(ITankTransferService tankTransferService) : ApiControllerBase
{
    [HttpGet]
    [HasPermission(PermissionCodes.Tank.View)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<TankTransferDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PagedResult<TankTransferDto>>>> GetTankTransfers(
        [FromQuery] TankTransferFilterDto filter,
        CancellationToken cancellationToken) =>
        Success(await tankTransferService.GetPagedAsync(filter, cancellationToken));

    [HttpGet("{id:guid}")]
    [HasPermission(PermissionCodes.Tank.View)]
    [ProducesResponseType(typeof(ApiResponse<TankTransferDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<TankTransferDetailDto>>> GetTankTransfer(Guid id, CancellationToken cancellationToken) =>
        Success(await tankTransferService.GetByIdAsync(id, cancellationToken));

    /// <summary>Create a draft voucher; VoucherNo is generated (e.g. PDC-202610-0001).</summary>
    [HttpPost]
    [HasPermission(PermissionCodes.Tank.Transfer)]
    [ProducesResponseType(typeof(ApiResponse<TankTransferDetailDto>), StatusCodes.Status201Created)]
    public async Task<ActionResult<ApiResponse<TankTransferDetailDto>>> CreateTankTransfer(
        [FromBody] CreateTankTransferDto dto,
        CancellationToken cancellationToken)
    {
        var result = await tankTransferService.CreateAsync(dto, cancellationToken);
        return Created(nameof(GetTankTransfer), new { id = result.Id }, result);
    }

    /// <summary>Edit a Draft or Rejected voucher.</summary>
    [HttpPut("{id:guid}")]
    [HasPermission(PermissionCodes.Tank.Transfer)]
    [ProducesResponseType(typeof(ApiResponse<TankTransferDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<TankTransferDetailDto>>> UpdateTankTransfer(
        Guid id,
        [FromBody] UpdateTankTransferDto dto,
        CancellationToken cancellationToken) =>
        Success(await tankTransferService.UpdateAsync(id, dto, cancellationToken));

    /// <summary>Delete a Draft voucher.</summary>
    [HttpDelete("{id:guid}")]
    [HasPermission(PermissionCodes.Tank.Transfer)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse>> DeleteTankTransfer(Guid id, CancellationToken cancellationToken)
    {
        await tankTransferService.DeleteAsync(id, cancellationToken);
        return Success("Deleted");
    }

    [HttpPost("{id:guid}/submit")]
    [HasPermission(PermissionCodes.Tank.Transfer)]
    [ProducesResponseType(typeof(ApiResponse<TankTransferDetailDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<TankTransferDetailDto>>> Submit(Guid id, CancellationToken cancellationToken) =>
        Success(await tankTransferService.SubmitAsync(id, cancellationToken));

    [HttpPost("{id:guid}/approve")]
    [HasPermission(PermissionCodes.Tank.TransferApprove)]
    [ProducesResponseType(typeof(ApiResponse<TankTransferDetailDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<TankTransferDetailDto>>> Approve(Guid id, CancellationToken cancellationToken) =>
        Success(await tankTransferService.ApproveAsync(id, cancellationToken));

    [HttpPost("{id:guid}/reject")]
    [HasPermission(PermissionCodes.Tank.TransferApprove)]
    [ProducesResponseType(typeof(ApiResponse<TankTransferDetailDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<TankTransferDetailDto>>> Reject(
        Guid id,
        [FromBody] VoucherReasonDto dto,
        CancellationToken cancellationToken) =>
        Success(await tankTransferService.RejectAsync(id, dto, cancellationToken));

    [HttpPost("{id:guid}/cancel")]
    [HasPermission(PermissionCodes.Tank.Transfer)]
    [ProducesResponseType(typeof(ApiResponse<TankTransferDetailDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<TankTransferDetailDto>>> Cancel(
        Guid id,
        [FromBody] VoucherReasonDto dto,
        CancellationToken cancellationToken) =>
        Success(await tankTransferService.CancelAsync(id, dto, cancellationToken));
}
