using System.Text.Json;
using CorePlasma.API.SourceSync;

namespace CorePlasma.API.SourceBusiness;

public sealed record SourceBusinessTable(string Source, string Table, string Label, string Domain, bool Native);

/// <summary>Business-only catalog. Authentication, ACL, device tokens and transport tables never enter this catalog.</summary>
public static class SourceBusinessCatalog
{
    public static IReadOnlyList<SourceBusinessTable> All { get; } = Build();
    public static SourceBusinessTable? Find(string source, string table) => All.FirstOrDefault(x => x.Source == source && x.Table == table);

    private static IReadOnlyList<SourceBusinessTable> Build()
    {
        var rows = new List<SourceBusinessTable>();
        void Add(string source, string domain, bool native, params (string Table, string Label)[] tables) =>
            rows.AddRange(tables.Select(x => new SourceBusinessTable(source, x.Table, x.Label, domain, native)));
        Add("quanly", "catalog", true, ("capacities", "Dung tích"), ("engine_crankshafts", "Trục máy"),
            ("engine_parts", "Linh kiện"), ("engine_types", "Loại máy"), ("providers", "Nhà cung cấp"),
            ("shippers", "Nhân viên giao hàng"), ("shipping_partners", "Đối tác vận chuyển"));
        Add("quanly", "warehouse", true, ("factories", "Nhà máy"), ("factory_managers", "Quản lý nhà máy"),
            ("warehouses", "Kho"), ("warehouse_keepers", "Nhân viên kho"), ("batch_add_tanks", "Đợt nhập bình"));
        Add("quanly", "relationships", true, ("customer_engines", "Máy của khách hàng"),
            ("customer_users", "Liên kết khách hàng"), ("reseller_customers", "Khách hàng đại lý"));
        Add("quanly", "inventory", true, ("export_tanks", "Phiếu xuất bình"), ("import_tanks", "Phiếu nhập bình"),
            ("revoke_tanks", "Phiếu thu hồi bình"));
        Add("quanly", "profiles", false, ("customers", "Khách hàng"), ("users", "Hồ sơ nhân viên"),
            ("engines", "Máy Plasma"), ("tanks", "Bình khí"));
        Add("quanly", "orders", false, ("orders", "Đơn hàng"), ("order_items", "Chi tiết đơn hàng"),
            ("order_tank_items", "Bình trong đơn hàng"), ("order_shippings", "Giao đơn hàng"), ("order_logs", "Lịch sử đơn hàng"));
        Add("quanly", "inventory", false, ("batch_add_tank_items", "Bình theo đợt nhập"),
            ("export_tank_items", "Chi tiết xuất bình"), ("export_tank_logs", "Lịch sử xuất bình"), ("export_tank_shippings", "Giao phiếu xuất"),
            ("import_tank_items", "Chi tiết nhập bình"), ("import_tank_logs", "Lịch sử nhập bình"), ("import_tank_shippings", "Giao phiếu nhập"),
            ("revoke_tank_items", "Chi tiết thu hồi"), ("revoke_tank_logs", "Lịch sử thu hồi"),
            ("move_tanks", "Phiếu điều chuyển"), ("move_items", "Chi tiết điều chuyển"), ("move_tank_items", "Bình điều chuyển"),
            ("move_tank_logs", "Lịch sử điều chuyển"), ("move_tank_shippings", "Giao điều chuyển"));
        Add("quanly", "history", false, ("engine_sessions", "Phiên máy"), ("tank_sessions", "Phiên sử dụng bình"),
            ("tank_logs", "Nhật ký bình"), ("notifications", "Thông báo nghiệp vụ"));
        Add("newapp", "catalog", true, ("Belt", "Dây đai"), ("Capacity", "Dung tích"),
            ("CrankshaftGenerators", "Bộ phát trục"), ("EnginePart", "Linh kiện"), ("EngineType", "Loại máy"),
            ("Gas", "Loại khí"), ("Provider", "Nhà cung cấp"), ("Shipper", "Nhân viên giao hàng"),
            ("Shipping", "Đối tác vận chuyển"), ("Valve", "Van bình"));
        Add("newapp", "warehouse", true, ("Warehouse", "Kho"), ("WarehouseGas", "Khí trong kho"),
            ("WarehouseKeeper", "Nhân viên kho"), ("WarehouseTank", "Bình trong kho"),
            ("ProviderOrder", "Đơn nhà cung cấp"), ("ProviderTank", "Bình nhà cung cấp"));
        Add("newapp", "maintenance", true, ("EngineError", "Lỗi máy"), ("EngineReport", "Báo cáo máy"),
            ("Ticket", "Yêu cầu hỗ trợ"), ("TicketProgress", "Tiến độ hỗ trợ"));
        Add("newapp", "relationships", true, ("CustomerEngine", "Máy của khách hàng"),
            ("CustomerTank", "Bình của khách hàng"), ("CustomerUser", "Liên kết khách hàng"),
            ("EngineTank", "Bình trên máy"), ("ResellerCustomer", "Khách hàng đại lý"),
            ("TankCycle", "Chu kỳ bình"), ("TankHistory", "Lịch sử trạng thái bình"));
        Add("newapp", "profiles", false, ("Customer", "Khách hàng"), ("User", "Hồ sơ nhân viên"),
            ("Engine", "Máy Plasma"), ("Tank", "Bình khí"));
        Add("newapp", "orders", false, ("Order", "Đơn hàng"), ("OrderDetail", "Chi tiết đơn hàng"),
            ("OrderShipping", "Giao đơn hàng"), ("OrderWarehouse", "Kho của đơn hàng"));
        Add("newapp", "inventory", false, ("MoveWarehouseOrder", "Phiếu điều chuyển"), ("MoveWarehouseOrderTank", "Bình điều chuyển"));
        Add("newapp", "history", false, ("Session", "Phiên máy"), ("TankSession", "Phiên sử dụng bình"),
            ("TankEngineHistory", "Lịch sử bình trên máy"), ("ImportTrace", "Lịch sử nhập dữ liệu"));
        return rows.AsReadOnly();
    }

    public static string Normalize(string key) => string.Concat(key.Where(char.IsLetterOrDigit)).ToLowerInvariant();
    private static readonly string[] Restricted = ["password", "passwd", "pwd", "salt", "token", "secret", "apikey", "credential",
        "auth", "accesskey", "hash", "securitystamp", "permission", "privilege", "canapprove", "userrole", "usertype", "roleid", "roles", "privatekey", "connectionstring", "cookie"];
    private static readonly HashSet<string> ProfileFields = ["id", "name", "fullname", "email", "phonenumber", "phone", "tel", "address",
        "isactive", "isdeleted", "deletedat", "createdat", "updatedat", "createdtime", "updatedtime", "createddate", "updateddate", "oldid"];

    public static bool SafeField(string table, string key)
    {
        var normalized = Normalize(key);
        return normalized.Length > 0 && !Restricted.Any(normalized.Contains)
            && (table is not ("users" or "User") || ProfileFields.Contains(normalized));
    }

    public static SortedDictionary<string, string?> Sanitize(string table, IReadOnlyDictionary<string, string?> row)
    {
        var result = new SortedDictionary<string, string?>(StringComparer.Ordinal);
        foreach (var (key, value) in row)
        {
            if (!SafeField(table, key)) continue;
            result[key] = Normalize(key) is "images" or "deliveredimages" or "files"
                && value is { Length: >= 2 } && value[0] == '{' && value[^1] == '}'
                ? SanitizePostgresArray(value) : SanitizeValue(value);
        }
        return result;
    }

    private static string SanitizePostgresArray(string value)
    {
        // The known attachment fields are PostgreSQL text[], not JSON objects.
        // Decode their quoted/escaped wire values before recursively applying the same policy.
        var items = new List<string?>();
        var text = new System.Text.StringBuilder();
        var quoted = false; var escaped = false; var wasQuoted = false;
        for (var index = 1; index < value.Length - 1; index++)
        {
            var character = value[index];
            if (escaped) { text.Append(character); escaped = false; continue; }
            if (character == '\\') { escaped = true; continue; }
            if (character == '"') { quoted = !quoted; wasQuoted = true; continue; }
            if (character == ',' && !quoted) { Add(); continue; }
            if (character is '{' or '}' && !quoted) return "[Dữ liệu có cấu trúc]";
            text.Append(character);
        }
        if (quoted || escaped) return "[Dữ liệu có cấu trúc]";
        if (text.Length > 0 || wasQuoted) Add();
        return JsonSerializer.Serialize(items);
        void Add() { var item = text.ToString(); items.Add(!wasQuoted && item == "NULL" ? null : SanitizeValue(item)); text.Clear(); wasQuoted = false; }
    }

    private static string? SanitizeValue(string? value)
    {
        if (value is null) return null;
        var trimmed = value.AsSpan().TrimStart();
        if (trimmed.Length == 0 || trimmed[0] is not ('{' or '[')) return value;
        try
        {
            using var json = JsonDocument.Parse(value, new JsonDocumentOptions { MaxDepth = 32 });
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream)) WriteSafeJson(writer, json.RootElement);
            return System.Text.Encoding.UTF8.GetString(stream.ToArray());
        }
        catch (JsonException) { return "[Dữ liệu có cấu trúc]"; } // Never forward an unparsed structured payload.
    }

    private static void WriteSafeJson(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject())
                    if (SafeField("", property.Name)) { writer.WritePropertyName(property.Name); WriteSafeJson(writer, property.Value); }
                writer.WriteEndObject(); break;
            case JsonValueKind.Array:
                writer.WriteStartArray(); foreach (var item in element.EnumerateArray()) WriteSafeJson(writer, item); writer.WriteEndArray(); break;
            case JsonValueKind.String: writer.WriteStringValue(SanitizeValue(element.GetString())); break;
            default: element.WriteTo(writer); break;
        }
    }

    public static string? Pick(IReadOnlyDictionary<string, string?> row, params string[] names) => row.FirstOrDefault(x => names.Contains(Normalize(x.Key), StringComparer.Ordinal) && !string.IsNullOrWhiteSpace(x.Value)).Value;
    public static string Name(IReadOnlyDictionary<string, string?> row, string fallback) => Cut(Pick(row, "name", "fullname", "title", "code", "serial", "message", "id") ?? fallback, 500);
    public static string? Status(IReadOnlyDictionary<string, string?> row) => Pick(row, "status", "state", "isactive") is { } value ? Cut(value, 100) : null;
    public static bool Deleted(IReadOnlyDictionary<string, string?> row) =>
        row.Any(x => Normalize(x.Key) == "deletedat" && x.Value is not null)
        || row.Any(x => Normalize(x.Key) == "isdeleted" && x.Value is "true" or "t" or "1" or "True");
    public static string Cut(string value, int length) => value.Length <= length ? value : value[..length];
    public static string[] SearchColumns(string table, SourceSyncTable manifest) => manifest.Columns.Where(column => SafeField(table, column)
        && new[] { "name", "fullname", "title", "code", "serial", "description", "message", "note" }.Contains(Normalize(column), StringComparer.Ordinal)).Take(8).ToArray();
    public static string? StatusColumn(string table, SourceSyncTable manifest) => manifest.Columns.FirstOrDefault(column => SafeField(table, column) && Normalize(column) is "status" or "state" or "isactive");
}
