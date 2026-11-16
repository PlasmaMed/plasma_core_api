using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace CorePlasma.Common.Helpers;

public static partial class VietnameseTextHelper
{
    /// <summary>
    /// "Bình khí Ô-xy Đà Nẵng" → "Binh khi O-xy Da Nang".
    /// </summary>
    public static string RemoveDiacritics(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var normalized = text.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(c switch
                {
                    'đ' => 'd',
                    'Đ' => 'D',
                    _ => c
                });
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>
    /// Lower-case, no diacritics, single spaces — for accent-insensitive comparison in memory.
    /// </summary>
    public static string NormalizeForSearch(string? text) =>
        MultipleSpaces().Replace(RemoveDiacritics(text).ToLowerInvariant().Trim(), " ");

    /// <summary>
    /// "Phiếu điều chuyển 10/2026" → "phieu-dieu-chuyen-10-2026" (file names, URLs).
    /// </summary>
    public static string ToSlug(string? text) =>
        NonAlphanumeric().Replace(RemoveDiacritics(text).ToLowerInvariant(), "-").Trim('-');

    [GeneratedRegex(@"\s+")]
    private static partial Regex MultipleSpaces();

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonAlphanumeric();
}
