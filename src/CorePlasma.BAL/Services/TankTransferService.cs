using AutoMapper;
using CorePlasma.BAL.DTOs.TankTransfers;
using CorePlasma.BAL.Interfaces;
using CorePlasma.BAL.Services.Common;
using CorePlasma.Common.Constants;
using CorePlasma.Common.Enums;
using CorePlasma.Common.Exceptions;
using CorePlasma.Common.Interfaces;
using CorePlasma.DAL.Entities;
using CorePlasma.DAL.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CorePlasma.BAL.Services;

/// <summary>
/// Phiếu điều chuyển bình khí — reference implementation of <see cref="VoucherServiceBase{TEntity,TDto,TDetailDto,TCreateDto,TUpdateDto,TFilter}"/>.
/// On approval every tank on the voucher moves to the destination customer / employee / location / status.
/// </summary>
public sealed class TankTransferService(
    IUnitOfWork unitOfWork,
    IMapper mapper,
    IDocumentNumberService documentNumberService,
    ICurrentUserService currentUser)
    : VoucherServiceBase<TankTransfer, TankTransferDto, TankTransferDetailDto, CreateTankTransferDto, UpdateTankTransferDto, TankTransferFilterDto>(
        unitOfWork, mapper, documentNumberService, currentUser),
      ITankTransferService
{
    protected override IRepository<TankTransfer> Repository => UnitOfWork.TankTransfers;

    protected override string SequenceCode => SequenceCodes.TankTransfer;

    protected override string EntityName => "Tank transfer";

    protected override IQueryable<TankTransfer> ApplyVoucherFilter(IQueryable<TankTransfer> query, TankTransferFilterDto filter)
    {
        if (filter.ToCustomerId.HasValue)
        {
            query = query.Where(x => x.ToCustomerId == filter.ToCustomerId);
        }

        if (filter.ToEmployeeId.HasValue)
        {
            query = query.Where(x => x.ToEmployeeId == filter.ToEmployeeId);
        }

        if (filter.TankId.HasValue)
        {
            query = query.Where(x => x.Items.Any(i => i.TankId == filter.TankId));
        }

        return query;
    }

    protected override async Task<TankTransfer> GetEntityAsync(Guid id, CancellationToken cancellationToken)
    {
        var entity = await (await ScopeAsync(Repository.Query(), cancellationToken))
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
        ?? throw new NotFoundException(EntityName, id);
        if (entity.SourceManaged)
        {
            throw new BusinessException("Phiếu này được cập nhật từ hệ thống nguồn. Vui lòng xử lý trên hệ thống nguồn.");
        }
        return entity;
    }

    protected override async Task BeforeCreateAsync(TankTransfer entity, CreateTankTransferDto dto, CancellationToken cancellationToken)
    {
        await base.BeforeCreateAsync(entity, dto, cancellationToken);
        await SetItemsAsync(entity, dto.Items, cancellationToken);
    }

    protected override async Task BeforeUpdateAsync(TankTransfer entity, UpdateTankTransferDto dto, CancellationToken cancellationToken)
    {
        await base.BeforeUpdateAsync(entity, dto, cancellationToken);

        UnitOfWork.TankTransferItems.DeleteRange(entity.Items.ToList());
        entity.Items.Clear();
        await SetItemsAsync(entity, dto.Items, cancellationToken);
    }

    /// <summary>A tank cannot be on two vouchers waiting for approval at the same time.</summary>
    protected override async Task OnSubmittingAsync(TankTransfer entity, CancellationToken cancellationToken)
    {
        var tankIds = entity.Items.Select(x => x.TankId).ToList();

        var busy = await UnitOfWork.TankTransferItems.QueryNoTracking()
            .Where(x => tankIds.Contains(x.TankId)
                        && x.TankTransferId != entity.Id
                        && x.TankTransfer.Status == VoucherStatus.Submitted)
            .Select(x => $"{x.TankCode} ({x.TankTransfer.VoucherNo})")
            .ToListAsync(cancellationToken);

        if (busy.Count > 0)
        {
            throw new BusinessException("Some tanks are already on another voucher awaiting approval.", busy);
        }
    }

    protected override async Task OnApprovingAsync(TankTransfer entity, CancellationToken cancellationToken)
    {
        var tankIds = entity.Items.Select(x => x.TankId).ToList();
        var tanks = await UnitOfWork.Tanks.Query()
            .Where(x => tankIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        var missing = entity.Items.Where(x => !tanks.ContainsKey(x.TankId)).Select(x => x.TankCode).ToList();
        if (missing.Count > 0)
        {
            throw new BusinessException("Some tanks no longer exist.", missing);
        }

        foreach (var item in entity.Items)
        {
            var tank = tanks[item.TankId];

            item.FromCustomerId = tank.CustomerId;
            item.FromEmployeeId = tank.AssignedEmployeeId;
            item.FromLocation = tank.CurrentLocation;
            item.FromStatus = tank.Status;

            if (entity.ToCustomerId.HasValue)
            {
                tank.CustomerId = entity.ToCustomerId;
            }

            if (entity.ToEmployeeId.HasValue)
            {
                tank.AssignedEmployeeId = entity.ToEmployeeId;
            }

            if (!string.IsNullOrWhiteSpace(entity.ToLocation))
            {
                tank.CurrentLocation = entity.ToLocation;
            }

            if (entity.ToStatus.HasValue)
            {
                tank.Status = entity.ToStatus.Value;
            }
        }
    }

    private async Task SetItemsAsync(TankTransfer entity, IReadOnlyCollection<TankTransferItemInputDto> items, CancellationToken cancellationToken)
    {
        var tankIds = items.Select(x => x.TankId).Distinct().ToList();
        var tankCodes = await UnitOfWork.Tanks.QueryNoTracking()
            .Where(x => tankIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.TankCode, cancellationToken);

        var missing = tankIds.Where(id => !tankCodes.ContainsKey(id)).ToList();
        if (missing.Count > 0)
        {
            throw new BusinessException("Some tanks do not exist.", missing.Select(x => x.ToString()).ToList());
        }

        foreach (var item in items.DistinctBy(x => x.TankId))
        {
            entity.Items.Add(new TankTransferItem
            {
                TankTransferId = entity.Id,
                TankId = item.TankId,
                TankCode = tankCodes[item.TankId],
                Note = item.Note
            });
        }
    }
}
