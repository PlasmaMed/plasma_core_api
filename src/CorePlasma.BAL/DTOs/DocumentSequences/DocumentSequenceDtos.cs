using CorePlasma.Common.Enums;

namespace CorePlasma.BAL.DTOs.DocumentSequences;

public sealed class DocumentSequenceDto
{
    public Guid Id { get; set; }

    public string SequenceCode { get; set; } = string.Empty;

    public string SequenceName { get; set; } = string.Empty;

    public string Prefix { get; set; } = string.Empty;

    public string Separator { get; set; } = string.Empty;

    public string? DateFormat { get; set; }

    public int Padding { get; set; }

    public SequenceResetPolicy ResetPolicy { get; set; }

    public string CurrentPeriod { get; set; } = string.Empty;

    public long CurrentValue { get; set; }

    public bool IsActive { get; set; }

    /// <summary>The number the next record would get (not reserved).</summary>
    public string NextNumber { get; set; } = string.Empty;
}

public sealed class UpdateDocumentSequenceDto
{
    public string SequenceName { get; set; } = string.Empty;

    public string Prefix { get; set; } = string.Empty;

    /// <summary>"", "-", "/", "." or "_".</summary>
    public string Separator { get; set; } = string.Empty;

    /// <summary>yyyy, yy, yyyyMM, yyMM, yyyyMMdd, yyMMdd or null.</summary>
    public string? DateFormat { get; set; }

    public int Padding { get; set; } = 5;

    public SequenceResetPolicy ResetPolicy { get; set; }

    public bool IsActive { get; set; } = true;
}
