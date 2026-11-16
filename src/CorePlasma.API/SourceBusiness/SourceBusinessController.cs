using System.Globalization;
using System.Text.Json;
using CorePlasma.API.Authorization;
using CorePlasma.API.Controllers;
using CorePlasma.API.SourceSync;
using CorePlasma.Common.Constants;
using CorePlasma.Common.Exceptions;
using CorePlasma.DAL.Context;
using CorePlasma.DAL.Entities;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CorePlasma.API.SourceBusiness;

[Route("api/source-business")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
[HasPermission(PermissionCodes.SourceBusiness.View)]
public sealed class SourceBusinessController(ApplicationDbContext context, ISourceSyncRawReader raw) : ApiControllerBase
{
    [HttpGet("catalog")]
    public async Task<IActionResult> Catalog([FromQuery] string source, CancellationToken cancellationToken)
    {
        KnownSource(source);
        var state = await raw.GetStateAsync(source, cancellationToken);
        if (state?.Status != "ready") return Success(Array.Empty<SourceBusinessTableDto>());
        var manifests = (await raw.GetTablesAsync(source, cancellationToken)).ToDictionary(x => x.Name, StringComparer.Ordinal);
        return Success(SourceBusinessCatalog.All.Where(x => x.Source == source && manifests.ContainsKey(x.Table)
                && (!x.Native || manifests[x.Table].PrimaryKey.Length > 0))
            .Select(x => new SourceBusinessTableDto(source, x.Table, x.Label, x.Domain, x.Native ? "core" : "operational",
                SourceBusinessCatalog.SearchColumns(x.Table, manifests[x.Table]).Length > 0,
                SourceBusinessCatalog.StatusColumn(x.Table, manifests[x.Table]))).ToArray());
    }

    [HttpGet("{source}/{table}")]
    public async Task<IActionResult> Page(string source, string table, [FromQuery] string? cursor,
        [FromQuery] string? keyword, [FromQuery] string? status, [FromQuery] bool includeDeleted = false,
        [FromQuery] int pageSize = 50, CancellationToken cancellationToken = default)
    {
        KnownSource(source);
        var descriptor = SourceBusinessCatalog.Find(source, table) ?? throw new BusinessException("Nhóm dữ liệu không hợp lệ.");
        if (pageSize is < 1 or > 200 || keyword?.Length > 200 || status?.Length > 200 || cursor?.Length > 200)
            throw new BusinessException("Bộ lọc dữ liệu không hợp lệ.");
        var state = await raw.GetStateAsync(source, cancellationToken);
        if (state?.Status != "ready") throw new BusinessException("Dữ liệu đang được đồng bộ. Vui lòng thử lại sau.");
        var manifest = (await raw.GetTablesAsync(source, cancellationToken)).SingleOrDefault(x => x.Name == table)
            ?? throw new BusinessException("Nhóm dữ liệu chưa được đồng bộ.");
        if (descriptor.Native)
        {
            if (cursor is not null && (cursor.Length != 64 || cursor.Any(c => !Uri.IsHexDigit(c))))
                throw new BusinessException("Trang dữ liệu không hợp lệ.");
            var query = context.CoreBusinessRecords.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.Source == source && x.SourceTable == table && x.InstallationId == state.InstallationId);
            if (!includeDeleted) query = query.Where(x => !x.IsDeleted);
            if (!string.IsNullOrWhiteSpace(keyword)) query = query.Where(x => x.Name.Contains(keyword));
            if (!string.IsNullOrWhiteSpace(status)) query = query.Where(x => x.SourceStatus == status);
            if (cursor is not null) query = query.Where(x => string.Compare(x.SourceKeyHash, cursor) > 0);
            var rows = await query.OrderBy(x => x.SourceKeyHash).Take(pageSize + 1).ToListAsync(cancellationToken);
            return Success(new SourceBusinessPageDto(rows.Take(pageSize).Select(ToDto).ToArray(), rows.Count > pageSize ? rows[pageSize - 1].SourceKeyHash : null));
        }
        var filters = new Dictionary<string, string?>(StringComparer.Ordinal);
        var statusColumn = SourceBusinessCatalog.StatusColumn(table, manifest);
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (statusColumn is null) throw new BusinessException("Nhóm dữ liệu này không có trạng thái.");
            filters[statusColumn] = status;
        }
        // A bounded query only searches descriptive business columns selected here, never caller-selected SQL fields.
        var columns = SourceBusinessCatalog.SearchColumns(table, manifest);
        if (!string.IsNullOrWhiteSpace(keyword) && columns.Length == 0) throw new BusinessException("Nhóm dữ liệu này không hỗ trợ tìm theo tên.");
        var page = await raw.ReadPageAsync(source, table, cursor, pageSize, filters, keyword, columns, cancellationToken);
        var items = page.Rows.Select(row =>
        {
            var safe = SourceBusinessCatalog.Sanitize(table, row.Values);
            var keyColumns = manifest.PrimaryKey.Length > 0 ? manifest.PrimaryKey : manifest.Columns;
            var key = Convert.ToHexString(SourceSyncCodec.Hash(keyColumns.Select(column => row.Values[column])));
            return new SourceBusinessRecordDto(key, SourceBusinessCatalog.Name(safe, descriptor.Label), SourceBusinessCatalog.Status(safe),
                SourceBusinessCatalog.Deleted(safe), UpdatedAt(safe), null, Fields(safe), row.Occurrences);
        }).ToArray();
        // Operational history preserves source deletion markers for an accurate audit view.
        return Success(new SourceBusinessPageDto(items, page.NextCursor));
    }

    private static void KnownSource(string source)
    {
        if (source is not ("quanly" or "newapp")) throw new BusinessException("Nguồn dữ liệu không hợp lệ.");
    }
    private static SourceBusinessRecordDto ToDto(CoreBusinessRecord record)
    {
        // Defense in depth when old projection rows predate the current redaction policy.
        var stored = JsonSerializer.Deserialize<Dictionary<string, string?>>(record.SnapshotJson) ?? [];
        var safe = SourceBusinessCatalog.Sanitize(record.SourceTable, stored);
        return new(record.SourceKeyHash, SourceBusinessCatalog.Name(safe, record.Name), SourceBusinessCatalog.Status(safe), record.IsDeleted,
            record.UpdatedDate, record.CanonicalEntityId, Fields(safe));
    }
    private static SourceBusinessFieldDto[] Fields(IReadOnlyDictionary<string, string?> row) => row.Select(x => new SourceBusinessFieldDto(x.Key, Label(x.Key), x.Value)).ToArray();
    private static DateTime? UpdatedAt(IReadOnlyDictionary<string, string?> row) => DateTime.TryParse(SourceBusinessCatalog.Pick(row,
        "updatedat", "updatedtime", "updateddate", "createdat", "createdtime"), CultureInfo.InvariantCulture,
        DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var value) ? value : null;
    private static string Label(string key) => SourceBusinessCatalog.Normalize(key) switch
    {
        "id" => "Mã nguồn", "name" or "fullname" => "Tên", "status" or "state" => "Trạng thái",
        "serial" => "Số seri", "description" => "Mô tả", "note" => "Ghi chú", "title" => "Tiêu đề",
        "quantity" => "Số lượng", "address" => "Địa chỉ", "phonenumber" or "telephone" or "tel" => "Điện thoại",
        "createdat" or "createdtime" or "createddate" => "Ngày tạo", "updatedat" or "updatedtime" or "updateddate" => "Cập nhật",
        "isdeleted" => "Đã xóa", "isactive" => "Đang hoạt động", "message" => "Nội dung", _ => key
    };
}
