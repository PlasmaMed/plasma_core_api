using AutoMapper;
using CorePlasma.BAL.DTOs.DocumentSequences;
using CorePlasma.BAL.Interfaces;
using CorePlasma.BAL.Services.Common;
using CorePlasma.Common.Exceptions;
using CorePlasma.Common.Helpers;
using CorePlasma.DAL.Entities;
using CorePlasma.DAL.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CorePlasma.BAL.Services;

public sealed class DocumentSequenceService(IUnitOfWork unitOfWork, IMapper mapper) : IDocumentSequenceService
{
    public async Task<IReadOnlyList<DocumentSequenceDto>> GetListAsync(CancellationToken cancellationToken)
    {
        var sequences = await unitOfWork.DocumentSequences.QueryNoTracking()
            .OrderBy(x => x.SequenceCode)
            .ToListAsync(cancellationToken);

        return sequences.Select(ToDto).ToList();
    }

    public async Task<DocumentSequenceDto> UpdateAsync(Guid id, UpdateDocumentSequenceDto dto, CancellationToken cancellationToken)
    {
        var entity = await unitOfWork.DocumentSequences.GetByIdAsync(id, cancellationToken)
                     ?? throw new NotFoundException(nameof(DocumentSequence), id);

        // Changing the reset policy changes the period key, so the counter restarts at 1 on next use;
        // generated numbers are still checked against existing codes before use.
        mapper.Map(dto, entity);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return ToDto(entity);
    }

    private DocumentSequenceDto ToDto(DocumentSequence entity)
    {
        var result = mapper.Map<DocumentSequenceDto>(entity);
        var today = DateTimeHelper.VietnamNow;
        var period = DocumentNumberService.GetPeriod(entity.ResetPolicy, today);
        var next = entity.CurrentPeriod == period ? entity.CurrentValue + 1 : 1;
        result.NextNumber = DocumentNumberService.Format(entity, today, next);
        return result;
    }
}
