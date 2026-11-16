using CorePlasma.API.Authorization;
using CorePlasma.BAL.DTOs.DocumentSequences;
using CorePlasma.BAL.Interfaces;
using CorePlasma.Common.Constants;
using CorePlasma.Common.Responses;
using Microsoft.AspNetCore.Mvc;

namespace CorePlasma.API.Controllers;

/// <summary>Numbering of auto codes (KH00001...) and vouchers (PDC-202610-0001...).</summary>
[Route("api/document-sequences")]
public sealed class DocumentSequencesController(IDocumentSequenceService documentSequenceService) : ApiControllerBase
{
    /// <summary>All sequences with the next number that would be issued.</summary>
    [HttpGet]
    [HasPermission(PermissionCodes.DocumentSequence.View)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<DocumentSequenceDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<DocumentSequenceDto>>>> GetDocumentSequences(
        CancellationToken cancellationToken) =>
        Success(await documentSequenceService.GetListAsync(cancellationToken));

    /// <summary>Change prefix, separator, date part, padding, reset policy or active flag.</summary>
    [HttpPut("{id:guid}")]
    [HasPermission(PermissionCodes.DocumentSequence.Update)]
    [ProducesResponseType(typeof(ApiResponse<DocumentSequenceDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<DocumentSequenceDto>>> UpdateDocumentSequence(
        Guid id,
        [FromBody] UpdateDocumentSequenceDto dto,
        CancellationToken cancellationToken) =>
        Success(await documentSequenceService.UpdateAsync(id, dto, cancellationToken));
}
