using CorePlasma.Common.Enums;

namespace CorePlasma.DAL.Entities;

/// <summary>
/// Numbering rule and running counter for one kind of code/voucher.
/// Number = Prefix + Separator + date(DateFormat) + Separator + value padded to Padding digits.
/// </summary>
public class DocumentSequence : BaseEntity
{
    public string SequenceCode { get; set; } = string.Empty;

    public string SequenceName { get; set; } = string.Empty;

    public string Prefix { get; set; } = string.Empty;

    public string Separator { get; set; } = string.Empty;

    /// <summary>
    /// .NET date format of the date part (e.g. yyyyMM). Null = no date part.
    /// </summary>
    public string? DateFormat { get; set; }

    public int Padding { get; set; } = 5;

    public SequenceResetPolicy ResetPolicy { get; set; } = SequenceResetPolicy.Never;

    /// <summary>
    /// Period the counter belongs to (yyyy / yyyyMM / yyyyMMdd, empty for Never).
    /// </summary>
    public string CurrentPeriod { get; set; } = string.Empty;

    public long CurrentValue { get; set; }

    public bool IsActive { get; set; } = true;
}
