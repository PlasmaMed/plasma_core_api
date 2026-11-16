namespace CorePlasma.Common.Enums;

/// <summary>
/// Lifecycle of a voucher (phiếu): Draft → Submitted → Approved / Rejected; Cancelled before approval.
/// </summary>
public enum VoucherStatus
{
    Draft = 1,
    Submitted = 2,
    Approved = 3,
    Rejected = 4,
    Cancelled = 5,
    /// <summary>Source-owned workflow, displayed from SourceStatusCode/SourceStatusLabel.</summary>
    ExternalWorkflow = 100
}
