using CorePlasma.Common.Enums;

namespace CorePlasma.Common.Constants;

public sealed record SequenceDefinition(
    string SequenceCode,
    string SequenceName,
    string Prefix,
    string Separator,
    string? DateFormat,
    int Padding,
    SequenceResetPolicy ResetPolicy);

/// <summary>
/// Default numbering seeded into DocumentSequences (only when missing; admins can change it afterwards).
/// Examples: KH00001, NV00001, TK000001, EN00001, PDC-202610-0001.
/// </summary>
public static class SequenceDefinitions
{
    public static readonly IReadOnlyList<SequenceDefinition> All =
    [
        new(SequenceCodes.Customer, "Mã khách hàng", "KH", "", null, 5, SequenceResetPolicy.Never),
        new(SequenceCodes.Employee, "Mã nhân viên", "NV", "", null, 5, SequenceResetPolicy.Never),
        new(SequenceCodes.Tank, "Mã bình khí", "TK", "", null, 6, SequenceResetPolicy.Never),
        new(SequenceCodes.Engine, "Mã máy", "EN", "", null, 5, SequenceResetPolicy.Never),
        new(SequenceCodes.TankTransfer, "Phiếu điều chuyển bình khí", "PDC", "-", "yyyyMM", 4, SequenceResetPolicy.Monthly)
    ];
}
