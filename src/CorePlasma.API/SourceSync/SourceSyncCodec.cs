using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CorePlasma.Common.Exceptions;

namespace CorePlasma.API.SourceSync;

/// <summary>Lossless wire validation and language-independent vector hashing.</summary>
public static class SourceSyncCodec
{
    public const int MaximumItems = 1000;
    public const int SnapshotMaximumItems = 5000;
    public const int MaximumBodyBytes = 16 * 1024 * 1024;
    public const string KeyColumn = "__ss_key_hash";
    public const string RowColumn = "__ss_row_hash";
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly UTF8Encoding Utf8 = new(false, true);

    public static string Schema(string source) => source switch
    {
        "quanly" => "source_quanly", "newapp" => "source_newapp",
        _ => throw new BusinessException("Unknown source sync source.")
    };

    public static void Identifier(string? name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > 128 || Utf8.GetByteCount(name) > 63
            || name.Any(char.IsControl) || name.StartsWith("__ss_", StringComparison.OrdinalIgnoreCase))
            throw new BusinessException("Invalid or reserved source identifier.");
    }

    public static void Tables(SourceSyncTable[]? tables)
    {
        if (tables is null || tables.Length is < 1 or > 256)
            throw new BusinessException("Source catalog must contain 1 to 256 tables.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var table in tables)
        {
            if (table is null) throw new BusinessException("Invalid source catalog.");
            Identifier(table.Name);
            if (!names.Add(table.Name) || table.Columns is null || table.Columns.Length is < 1 or > 512
                || table.PrimaryKey is null || table.CatalogSignature is null || table.CatalogSignature.Length is < 1 or > 256)
                throw new BusinessException("Invalid source catalog.");
            var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var column in table.Columns)
            {
                Identifier(column);
                if (!columns.Add(column)) throw new BusinessException("Source columns must be distinct SQL identifiers.");
            }
            if (table.PrimaryKey.Distinct(StringComparer.Ordinal).Count() != table.PrimaryKey.Length
                || table.PrimaryKey.Any(key => !table.Columns.Contains(key, StringComparer.Ordinal)))
                throw new BusinessException("Invalid source primary key.");
        }
    }

    public static long DecimalId(string? value, bool allowZero = false)
    {
        if (value is null || value.Length is < 1 or > 19 || value.Any(c => c is < '0' or > '9')
            || value.Length > 1 && value[0] == '0'
            || !long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var result)
            || result < (allowZero ? 0 : 1))
            throw new BusinessException("Expected a canonical positive decimal int64 string.");
        return result;
    }

    public static string?[] Row(JsonElement row, SourceSyncTable table)
    {
        if (row.ValueKind != JsonValueKind.Object) throw new BusinessException("A source row must be an object.");
        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var property in row.EnumerateObject())
        {
            if (property.Value.ValueKind is not (JsonValueKind.Null or JsonValueKind.String)
                || !values.TryAdd(property.Name, property.Value.ValueKind == JsonValueKind.Null ? null : property.Value.GetString()))
                throw new BusinessException("Source row fields must be distinct strings or null.");
        }
        if (values.Count != table.Columns.Length || table.Columns.Any(column => !values.ContainsKey(column)))
            throw new BusinessException("Source row fields differ from the registered catalog.");
        if (table.PrimaryKey.Any(key => values[key] is null)) throw new BusinessException("Source primary key cannot contain null.");
        return table.Columns.Select(column => values[column]).ToArray();
    }

    public static byte[] Hash(IEnumerable<string?> values)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> length = stackalloc byte[8];
        foreach (var value in values)
        {
            hash.AppendData(new byte[] { value is null ? (byte)0 : (byte)1 });
            if (value is null) continue;
            var bytes = Utf8.GetBytes(value);
            BinaryPrimitives.WriteUInt64BigEndian(length, (ulong)bytes.Length);
            hash.AppendData(length);
            hash.AppendData(bytes);
        }
        return hash.GetHashAndReset();
    }

    public static byte[] Key(SourceSyncTable table, string?[] values) => Hash(table.PrimaryKey.Length == 0
        ? values : table.PrimaryKey.Select(key => values[Array.IndexOf(table.Columns, key)]));

    public static byte[] EventHash(SourceSyncEvent item, SourceSyncTable table)
    {
        DecimalId(item.Id); DecimalId(item.TransactionId);
        if (item.OccurredAt is null || !item.OccurredAt.EndsWith('Z')
            || !DateTimeOffset.TryParse(item.OccurredAt, CultureInfo.InvariantCulture, DateTimeStyles.None, out var at)
            || at.Offset != TimeSpan.Zero)
            throw new BusinessException("Source occurredAt must be an explicit UTC timestamp ending in Z.");
        var fields = new List<string?> { item.Id, item.TransactionId, item.Table, item.Operation, item.OccurredAt };
        switch (item.Operation)
        {
            case "INSERT":
                RequireNull(item.OldRow); fields.Add(null); fields.AddRange(Row(item.NewRow, table)); break;
            case "UPDATE":
                fields.AddRange(Row(item.OldRow, table)); fields.AddRange(Row(item.NewRow, table)); break;
            case "DELETE":
                fields.AddRange(Row(item.OldRow, table)); RequireNull(item.NewRow); fields.Add(null); break;
            case "TRUNCATE":
                RequireNull(item.OldRow); RequireNull(item.NewRow); fields.Add(null); fields.Add(null); break;
            default: throw new BusinessException("Invalid source operation.");
        }
        return Hash(fields);
    }

    private static void RequireNull(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Null) throw new BusinessException("Invalid source before/after image.");
    }

    public static string Quote(string identifier) => "[" + identifier.Replace("]", "]]", StringComparison.Ordinal) + "]";
}
