using AutoMapper;
using CorePlasma.BAL.DTOs.Common;
using CorePlasma.BAL.Interfaces;
using CorePlasma.Common.Enums;
using CorePlasma.Common.Exceptions;
using CorePlasma.Common.Extensions;
using CorePlasma.Common.Helpers;
using CorePlasma.Common.Interfaces;
using CorePlasma.DAL.Entities;
using CorePlasma.DAL.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CorePlasma.BAL.Services.Common;

/// <summary>
/// Reusable voucher (phiếu) service: auto voucher number, Draft/Submit/Approve/Reject/Cancel workflow,
/// edit/delete guards, common filters and transactions. A new voucher type declares its
/// <see cref="SequenceCode"/>, <see cref="CrudServiceBase{TEntity,TDto,TDetailDto,TCreateDto,TUpdateDto,TFilter}.Repository"/>
/// and filter, and implements <see cref="OnApprovingAsync"/> for its business effect.
/// </summary>
public abstract class VoucherServiceBase<TEntity, TDto, TDetailDto, TCreateDto, TUpdateDto, TFilter>(
    IUnitOfWork unitOfWork,
    IMapper mapper,
    IDocumentNumberService documentNumberService,
    ICurrentUserService currentUser)
    : CrudServiceBase<TEntity, TDto, TDetailDto, TCreateDto, TUpdateDto, TFilter>(unitOfWork, mapper, documentNumberService),
      IVoucherService<TDto, TDetailDto, TCreateDto, TUpdateDto, TFilter>
    where TEntity : VoucherBase
    where TCreateDto : VoucherWriteDto
    where TUpdateDto : VoucherWriteDto
    where TFilter : VoucherFilterDto
{
    protected ICurrentUserService CurrentUser { get; } = currentUser;

    /// <summary>Row in DocumentSequences used for VoucherNo.</summary>
    protected abstract string SequenceCode { get; }

    protected override AutoCodeOptions<TEntity> AutoCode =>
        new(SequenceCode, x => x.VoucherNo, x => x.VoucherDate);

    protected override bool UseTransaction => true;

    protected override string DefaultSortField => nameof(VoucherBase.VoucherDate);

    protected override IReadOnlyCollection<string> SortableFields =>
    [
        nameof(VoucherBase.VoucherNo), nameof(VoucherBase.VoucherDate), nameof(VoucherBase.Status),
        nameof(VoucherBase.CreatedDate), nameof(VoucherBase.ApprovedDate)
    ];

    /// <summary>Voucher-specific filters (common voucher filters are already applied).</summary>
    protected abstract IQueryable<TEntity> ApplyVoucherFilter(IQueryable<TEntity> query, TFilter filter);

    /// <summary>Validate the voucher before it goes to approval.</summary>
    protected virtual Task OnSubmittingAsync(TEntity entity, CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Apply the voucher's effect (stock, location, ...). Runs in the approval transaction.</summary>
    protected abstract Task OnApprovingAsync(TEntity entity, CancellationToken cancellationToken);

    protected virtual Task OnRejectingAsync(TEntity entity, CancellationToken cancellationToken) => Task.CompletedTask;

    protected virtual Task OnCancellingAsync(TEntity entity, CancellationToken cancellationToken) => Task.CompletedTask;

    protected sealed override IQueryable<TEntity> ApplyFilter(IQueryable<TEntity> query, TFilter filter)
    {
        var voucherNo = filter.VoucherNo.TrimToNull();
        var keyword = filter.Keyword.TrimToNull();
        var toDateExclusive = filter.ToDate?.Date.AddDays(1);

        query = query
            .WhereContainsAny(UnitOfWork.IsSqlServer, voucherNo, x => x.VoucherNo)
            .WhereContainsAny(UnitOfWork.IsSqlServer, keyword, x => x.VoucherNo, x => x.Note)
            .WhereIf(filter.Status.HasValue, x => x.Status == filter.Status)
            .WhereIf(filter.FromDate.HasValue, x => x.VoucherDate >= filter.FromDate!.Value.Date)
            .WhereIf(toDateExclusive.HasValue, x => x.VoucherDate < toDateExclusive);

        return ApplyVoucherFilter(query, filter);
    }

    /// <summary>Call base first when overriding: sets Draft status and default date.</summary>
    protected override Task BeforeCreateAsync(TEntity entity, TCreateDto dto, CancellationToken cancellationToken)
    {
        entity.Status = VoucherStatus.Draft;
        entity.VoucherNo = string.Empty;
        entity.VoucherDate = (dto.VoucherDate ?? DateTimeHelper.VietnamToday).Date;
        return Task.CompletedTask;
    }

    /// <summary>Call base first when overriding: blocks edits after submission.</summary>
    protected override Task BeforeUpdateAsync(TEntity entity, TUpdateDto dto, CancellationToken cancellationToken)
    {
        VoucherWorkflow.EnsureCanEdit(entity.Status);
        dto.VoucherDate = (dto.VoucherDate ?? entity.VoucherDate).Date;
        return Task.CompletedTask;
    }

    protected override Task BeforeDeleteAsync(TEntity entity, CancellationToken cancellationToken)
    {
        VoucherWorkflow.EnsureCanDelete(entity.Status);
        return Task.CompletedTask;
    }

    public Task<TDetailDto> SubmitAsync(Guid id, CancellationToken cancellationToken) =>
        TransitionAsync(id, VoucherStatus.Submitted, OnSubmittingAsync, (entity, actor, now) =>
        {
            entity.SubmittedBy = actor;
            entity.SubmittedDate = now;
        }, cancellationToken);

    public Task<TDetailDto> ApproveAsync(Guid id, CancellationToken cancellationToken) =>
        TransitionAsync(id, VoucherStatus.Approved, OnApprovingAsync, (entity, actor, now) =>
        {
            entity.ApprovedBy = actor;
            entity.ApprovedDate = now;
        }, cancellationToken);

    public Task<TDetailDto> RejectAsync(Guid id, VoucherReasonDto dto, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(dto.Reason))
        {
            throw new BusinessException("A reason is required to reject a voucher.");
        }

        return TransitionAsync(id, VoucherStatus.Rejected, OnRejectingAsync, (entity, actor, now) =>
        {
            entity.RejectedBy = actor;
            entity.RejectedDate = now;
            entity.RejectReason = dto.Reason.Trim();
        }, cancellationToken);
    }

    public Task<TDetailDto> CancelAsync(Guid id, VoucherReasonDto dto, CancellationToken cancellationToken) =>
        TransitionAsync(id, VoucherStatus.Cancelled, OnCancellingAsync, (entity, actor, now) =>
        {
            entity.CancelledBy = actor;
            entity.CancelledDate = now;
            entity.CancelReason = dto.Reason.TrimToNull();
        }, cancellationToken);

    private async Task<TDetailDto> TransitionAsync(
        Guid id,
        VoucherStatus target,
        Func<TEntity, CancellationToken, Task> hook,
        Action<TEntity, Guid?, DateTime> stamp,
        CancellationToken cancellationToken)
    {
        await ExecuteAsync(async () =>
        {
            var entity = await GetEntityAsync(id, cancellationToken);
            VoucherWorkflow.EnsureCanTransition(entity.Status, target);

            await hook(entity, cancellationToken);

            entity.Status = target;
            stamp(entity, CurrentUser.ActorId, DateTime.UtcNow);
            await UnitOfWork.SaveChangesAsync(cancellationToken);
        }, cancellationToken);

        return await GetByIdAsync(id, cancellationToken);
    }
}
