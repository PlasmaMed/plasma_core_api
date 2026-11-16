using CorePlasma.API.SourceSync;

namespace CorePlasma.API.SourceBusiness;

public sealed record BusinessProjectionResult(string State, string? ErrorCode = null);

public interface ISourceBusinessProjector
{
    Task<BusinessProjectionResult> ProjectBaselineAsync(string source, Guid installationId, Guid generation, CancellationToken cancellationToken);
    Task<BusinessProjectionResult> ProjectEventAsync(SourceSyncPendingEvent sourceEvent, CancellationToken cancellationToken);
}

public sealed record SourceBusinessTableDto(string Source, string Table, string Label, string Domain, string Storage, bool Searchable, string? StatusColumn);
public sealed record SourceBusinessFieldDto(string Key, string Label, string? Value);
public sealed record SourceBusinessRecordDto(string Key, string Name, string? Status, bool IsDeleted,
    DateTime? UpdatedAt, Guid? CanonicalEntityId, IReadOnlyList<SourceBusinessFieldDto> Fields, long Occurrences = 1);
public sealed record SourceBusinessPageDto(IReadOnlyList<SourceBusinessRecordDto> Items, string? NextCursor);
