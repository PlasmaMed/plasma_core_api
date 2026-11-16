using CorePlasma.BAL.DTOs.Common;

namespace CorePlasma.BAL.Interfaces;

/// <summary>
/// CRUD + approval workflow shared by every voucher (phiếu).
/// Implement by inheriting <see cref="Services.Common.VoucherServiceBase{TEntity,TDto,TDetailDto,TCreateDto,TUpdateDto,TFilter}"/>.
/// </summary>
public interface IVoucherService<TDto, TDetailDto, in TCreateDto, in TUpdateDto, in TFilter>
    : ICrudService<TDto, TDetailDto, TCreateDto, TUpdateDto, TFilter>
{
    /// <summary>Draft/Rejected → Submitted.</summary>
    Task<TDetailDto> SubmitAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Submitted → Approved; applies the voucher's business effect in the same transaction.</summary>
    Task<TDetailDto> ApproveAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Submitted → Rejected (reason required); the voucher becomes editable again.</summary>
    Task<TDetailDto> RejectAsync(Guid id, VoucherReasonDto dto, CancellationToken cancellationToken);

    /// <summary>Draft/Submitted/Rejected → Cancelled.</summary>
    Task<TDetailDto> CancelAsync(Guid id, VoucherReasonDto dto, CancellationToken cancellationToken);
}
