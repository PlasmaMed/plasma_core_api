using CorePlasma.BAL.DTOs.Imports;

namespace CorePlasma.BAL.Interfaces;

/// <summary>
/// Imports machines and gas tanks from the PlasmaMED Excel export. All-or-nothing: any invalid row
/// rejects the whole file. Re-importing the same file updates records (matched by LegacyId, then code).
/// </summary>
public interface IEquipmentImportService
{
    Task<ImportResultDto> ImportEnginesAsync(Stream excel, CancellationToken cancellationToken);

    Task<ImportResultDto> ImportTanksAsync(Stream excel, CancellationToken cancellationToken);
}
