using CorePlasma.Common.Enums;

namespace CorePlasma.DAL.Entities;

/// <summary>
/// Common header of every voucher (phiếu): number, business date, status and workflow stamps.
/// New voucher types inherit this and only add their own fields.
/// </summary>
public abstract class VoucherBase : BaseEntity
{
    /// <summary>
    /// Generated from DocumentSequences on create.
    /// </summary>
    public string VoucherNo { get; set; } = string.Empty;

    /// <summary>
    /// Business date in Vietnam time.
    /// </summary>
    public DateTime VoucherDate { get; set; }

    public VoucherStatus Status { get; set; } = VoucherStatus.Draft;

    public string? Note { get; set; }

    public Guid? SubmittedBy { get; set; }

    public DateTime? SubmittedDate { get; set; }

    public Guid? ApprovedBy { get; set; }

    public DateTime? ApprovedDate { get; set; }

    public Guid? RejectedBy { get; set; }

    public DateTime? RejectedDate { get; set; }

    public string? RejectReason { get; set; }

    public Guid? CancelledBy { get; set; }

    public DateTime? CancelledDate { get; set; }

    public string? CancelReason { get; set; }
}
