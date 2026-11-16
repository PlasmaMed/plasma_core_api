using System.Text.RegularExpressions;

namespace CorePlasma.BAL.Services.Common;

internal static class CodeAliases
{
    // A code such as KC5160 also identifies KC05160. The database columns are at most 100 characters.
    public static string[] For(string? value, int maximumLength)
    {
        if (value is null) return [];
        var normalized = value.Trim().ToUpperInvariant();
        var match = Regex.Match(normalized, @"\A([A-Z]+)([0-9]+)\z", RegexOptions.CultureInvariant);
        if (!match.Success) return [normalized];

        var prefix = match.Groups[1].Value;
        var digits = match.Groups[2].Value.TrimStart('0');
        if (digits.Length == 0) digits = "0";
        var shortest = prefix + digits;
        return Enumerable.Range(0, Math.Max(0, maximumLength - shortest.Length + 1))
            .Select(zeros => prefix + new string('0', zeros) + digits).ToArray();
    }
}
