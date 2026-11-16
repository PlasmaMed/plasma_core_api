using CorePlasma.BAL.DTOs.DocumentSequences;

namespace CorePlasma.BAL.Interfaces;

/// <summary>Admin configuration of code / voucher numbering.</summary>
public interface IDocumentSequenceService
{
    Task<IReadOnlyList<DocumentSequenceDto>> GetListAsync(CancellationToken cancellationToken);

    Task<DocumentSequenceDto> UpdateAsync(Guid id, UpdateDocumentSequenceDto dto, CancellationToken cancellationToken);
}
