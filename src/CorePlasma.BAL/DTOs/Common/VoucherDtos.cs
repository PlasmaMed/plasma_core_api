using CorePlasma.Common.Enums;
using CorePlasma.Common.Models;

namespace CorePlasma.BAL.DTOs.Common;

/// <summary>
/// Header fields shown in voucher lists. Voucher list DTOs inherit this.
/// </summary>
public class VoucherDto
{
    public Guid Id { get; set; }

    public string VoucherNo { get; set; } = string.Empty;

    public DateTime VoucherDate { get; set; }

    public VoucherStatus Status { get; set; }

    public string? Note { get; set; }

    public DateTime CreatedDate { get; set; }

    public Guid? CreatedBy { get; set; }
}

/// <summary>
/// Header + workflow stamps. Voucher detail DTOs inherit this.
/// </summary>
public class VoucherDetailDto : VoucherDto
{
    public DateTime? UpdatedDate { get; set; }

    public Guid? UpdatedBy { get; set; }

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

/// <summary>
/// Editable header fields. Create/Update voucher DTOs inherit this. VoucherNo is always generated.
/// </summary>
public abstract class VoucherWriteDto
{
    /// <summary>
    /// Business date (Vietnam time). Defaults to today.
    /// </summary>
    public DateTime? VoucherDate { get; set; }

    public string? Note { get; set; }
}

/// <summary>
/// Common voucher list filters. Voucher filter DTOs inherit this.
/// </summary>
public class VoucherFilterDto : PagedRequest
{
    public string? VoucherNo { get; set; }

    public VoucherStatus? Status { get; set; }

    public DateTime? FromDate { get; set; }

    public DateTime? ToDate { get; set; }
}

public sealed class VoucherReasonDto
{
    public string? Reason { get; set; }
}
