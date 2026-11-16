namespace CorePlasma.Common.Helpers;

/// <summary>
/// LIKE patterns for keyword search. Use with
/// <c>EF.Functions.ILike(EF.Functions.Unaccent(column), SearchPattern.Contains(term))</c>
/// so search ignores case and Vietnamese diacritics ("nguyen" matches "Nguyễn").
/// </summary>
public static class SearchPattern
{
    /// <summary>Whole-value match ignoring case and diacritics (e.g. an exact serial or RFID code).</summary>
    public static string Exact(string? term) => Escape(VietnameseTextHelper.RemoveDiacritics(term));

    public static string Contains(string? term) => $"%{Escape(VietnameseTextHelper.RemoveDiacritics(term))}%";

    // Backslash is PostgreSQL's default LIKE escape character.
    private static string Escape(string value) =>
        value.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_");
}
