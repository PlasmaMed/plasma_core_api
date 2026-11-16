using CorePlasma.Common.Enums;
using CorePlasma.Common.Exceptions;

namespace CorePlasma.BAL.Services.Common;

/// <summary>
/// Allowed voucher status transitions:
/// Draft → Submitted | Cancelled; Submitted → Approved | Rejected | Cancelled; Rejected → Submitted | Cancelled.
/// Approved and Cancelled are final (an approved voucher is reversed by a new voucher, not edited).
/// </summary>
public static class VoucherWorkflow
{
    private static readonly IReadOnlyDictionary<VoucherStatus, VoucherStatus[]> Transitions =
        new Dictionary<VoucherStatus, VoucherStatus[]>
        {
            [VoucherStatus.Draft] = [VoucherStatus.Submitted, VoucherStatus.Cancelled],
            [VoucherStatus.Submitted] = [VoucherStatus.Approved, VoucherStatus.Rejected, VoucherStatus.Cancelled],
            [VoucherStatus.Rejected] = [VoucherStatus.Submitted, VoucherStatus.Cancelled],
            [VoucherStatus.Approved] = [],
            [VoucherStatus.Cancelled] = []
        };

    public static bool CanTransition(VoucherStatus from, VoucherStatus to) =>
        Transitions.TryGetValue(from, out var targets) && targets.Contains(to);

    public static void EnsureCanTransition(VoucherStatus from, VoucherStatus to)
    {
        if (!CanTransition(from, to))
        {
            throw new BusinessException($"A voucher in status {from} cannot be changed to {to}.");
        }
    }

    public static bool CanEdit(VoucherStatus status) => status is VoucherStatus.Draft or VoucherStatus.Rejected;

    public static void EnsureCanEdit(VoucherStatus status)
    {
        if (!CanEdit(status))
        {
            throw new BusinessException($"A voucher in status {status} can no longer be edited.");
        }
    }

    public static void EnsureCanDelete(VoucherStatus status)
    {
        if (status != VoucherStatus.Draft)
        {
            throw new BusinessException("Only draft vouchers can be deleted; cancel it instead.");
        }
    }
}
