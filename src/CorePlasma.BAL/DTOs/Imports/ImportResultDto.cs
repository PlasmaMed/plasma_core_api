namespace CorePlasma.BAL.DTOs.Imports;

public sealed class ImportResultDto
{
    public string SheetName { get; set; } = string.Empty;

    public int TotalRows { get; set; }

    public int Created { get; set; }

    /// <summary>Rows matched to an existing record (by LegacyId, then by code). Only real changes are logged.</summary>
    public int Updated { get; set; }
}
