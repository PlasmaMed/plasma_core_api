using System.Globalization;
using CorePlasma.Common.Exceptions;
using CorePlasma.Common.Helpers;
using ExcelDataReader;

namespace CorePlasma.BAL.Services.Imports;

/// <summary>One data row of a sheet exported from the PlasmaMED source system.</summary>
public sealed class LegacyRow(int rowNumber, IReadOnlyDictionary<string, object?> values)
{
    // Export conventions: "[null]" = null, "[không có trường]" = field missing, empty cell = empty string.
    private static readonly HashSet<string> NullMarkers = ["[null]", "[không có trường]"];

    public int RowNumber { get; } = rowNumber;

    public string? Text(string column)
    {
        if (!values.TryGetValue(column, out var value) || value is null)
        {
            return null;
        }

        var text = Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim();
        return string.IsNullOrEmpty(text) || NullMarkers.Contains(text) ? null : text;
    }

    public int? Integer(string column)
    {
        var text = Text(column);
        if (text is null)
        {
            return null;
        }

        return values[column] is double number
            ? (int)number
            : int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : throw new FormatException($"'{column}' is not an integer: {text}");
    }

    public bool? YesNo(string column) => Text(column)?.ToUpperInvariant() switch
    {
        null => null,
        "YES" => true,
        "NO" => false,
        var other => throw new FormatException($"'{column}' must be YES or NO: {other}")
    };

    /// <summary>Date cells are Vietnam time (UTC+7); ISO strings carry their own offset. Returns UTC.</summary>
    public DateTime? UtcDate(string column)
    {
        switch (values.GetValueOrDefault(column))
        {
            case DateTime local:
                return DateTimeHelper.VietnamToUtc(local);
            // ISO text in a date-formatted cell (e.g. "2019-12-10T12:06:33.108Z") is returned already parsed.
            case DateTimeOffset withOffset:
                return withOffset.UtcDateTime;
        }

        var text = Text(column);
        if (text is null)
        {
            return null;
        }

        if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            && (text.EndsWith('Z') || text.Contains('+')))
        {
            return parsed.UtcDateTime;
        }

        return DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var plain)
            ? DateTimeHelper.VietnamToUtc(plain)
            : throw new FormatException($"'{column}' is not a date: {text}");
    }
}

public static class LegacySheetReader
{
    private const int HeaderSearchRows = 20;

    // ExcelDataReader needs legacy code pages (e.g. 1252), which .NET does not register by default.
    static LegacySheetReader() => System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

    /// <summary>
    /// Finds the first sheet whose header row (within the first rows) contains all <paramref name="requiredColumns"/>
    /// and returns its data rows. Title lines above the header are ignored.
    /// </summary>
    public static (string SheetName, List<LegacyRow> Rows) Read(Stream stream, params string[] requiredColumns)
    {
        using var reader = ExcelReaderFactory.CreateReader(stream);
        do
        {
            string[]? header = null;
            var rows = new List<LegacyRow>();
            var rowNumber = 0;

            while (reader.Read())
            {
                rowNumber++;
                var cells = Enumerable.Range(0, reader.FieldCount).Select(reader.GetValue).ToArray();

                if (header is null)
                {
                    var names = cells.Select(c => Convert.ToString(c, CultureInfo.InvariantCulture)?.Trim() ?? string.Empty).ToArray();
                    if (requiredColumns.All(names.Contains))
                    {
                        header = names;
                    }
                    else if (rowNumber >= HeaderSearchRows)
                    {
                        break;
                    }

                    continue;
                }

                if (cells.All(c => c is null || (c is string s && string.IsNullOrWhiteSpace(s))))
                {
                    continue;
                }

                var values = new Dictionary<string, object?>(StringComparer.Ordinal);
                for (var i = 0; i < header.Length && i < cells.Length; i++)
                {
                    if (header[i].Length > 0)
                    {
                        values[header[i]] = cells[i];
                    }
                }

                rows.Add(new LegacyRow(rowNumber, values));
            }

            if (header is not null)
            {
                return (reader.Name, rows);
            }
        }
        while (reader.NextResult());

        throw new BusinessException($"No sheet has the columns: {string.Join(", ", requiredColumns)}.");
    }
}
