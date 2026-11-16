using System.Globalization;
using System.Text.Json;

namespace CorePlasma.BAL.Services.Common;

/// <summary>Read-only per-alias workflow facts, separate from the canonical Core tank status.</summary>
public static class SourceTankStatusPresentation
{
    public static (string? Code, string? Label) FromSnapshot(string sourceSystem, string snapshot)
    {
        try
        {
            using var document = JsonDocument.Parse(snapshot);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return (null, null);
            var status = root.TryGetProperty("status", out var normalized) && normalized.ValueKind == JsonValueKind.String
                ? normalized.GetString() : null;
            string? code = null;
            if (root.TryGetProperty("_source_status", out var original)) code = original.ValueKind switch
            {
                JsonValueKind.String => original.GetString(),
                JsonValueKind.Number when original.TryGetInt64(out var number) => number.ToString(CultureInfo.InvariantCulture),
                _ => null
            };
            // Quanly already uses the exact text code. Older NEWAPP snapshots kept only a
            // normalized status: show its label, without pretending it is the original numeric code.
            code ??= sourceSystem == "quanly.plasma.vn" ? status : null;
            if (code is not null && (code.Length > 80 || code.Any(character =>
                character is not (>= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '-')))) code = null;
            var label = status switch
            {
                "ready" => "Sẵn sàng", "occupied" => "Tại khách hàng", "empty" => "Hết khí", "to_be_refill" => "Chờ nạp",
                "broken" => "Hư hỏng", "loading" => "Đang nạp", "locked" => "Đang khóa", "disabled" => "Đã vô hiệu",
                "belong_to_order" => "Đã gán đơn", "shipping" => "Đang vận chuyển", "attached" => "Đã gắn máy", "detached" => "Đã tháo máy",
                "waiting_for_verification" => "Chờ xác minh", "importing" => "Đang nhập kho", "exporting" => "Đang xuất kho",
                "moving" => "Đang điều chuyển", "revoking" => "Đang thu hồi", "undefined" => "Chưa xác định", _ => null
            };
            return (code, label);
        }
        catch (JsonException) { return (null, null); }
    }
}
