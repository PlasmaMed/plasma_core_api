using System.Text;

namespace CorePlasma.Common.Helpers;

/// <summary>
/// Reads numbers in Vietnamese for printed vouchers ("Số tiền bằng chữ").
/// 1250000 → "Một triệu hai trăm năm mươi nghìn đồng".
/// </summary>
public static class NumberToWordsHelper
{
    private static readonly string[] Digits =
        ["không", "một", "hai", "ba", "bốn", "năm", "sáu", "bảy", "tám", "chín"];

    private static readonly string[] GroupNames =
        ["", "nghìn", "triệu", "tỷ", "nghìn tỷ", "triệu tỷ", "tỷ tỷ"];

    /// <param name="amount">Rounded to a whole number.</param>
    /// <param name="unit">Appended unit, e.g. "đồng". Pass empty for none.</param>
    public static string ToVietnameseWords(decimal amount, string unit = "đồng")
    {
        var value = (long)Math.Round(Math.Abs(amount), MidpointRounding.AwayFromZero);
        var words = value == 0 ? "không" : ReadNumber(value);
        if (amount < 0 && value != 0)
        {
            words = "âm " + words;
        }

        var result = string.IsNullOrEmpty(unit) ? words : $"{words} {unit}";
        return char.ToUpperInvariant(result[0]) + result[1..];
    }

    private static string ReadNumber(long value)
    {
        var groups = new List<int>();
        while (value > 0)
        {
            groups.Add((int)(value % 1000));
            value /= 1000;
        }

        var builder = new StringBuilder();
        var hasHigherGroup = false;
        for (var i = groups.Count - 1; i >= 0; i--)
        {
            if (groups[i] == 0)
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.Append(' ');
            }

            builder.Append(ReadGroup(groups[i], hasHigherGroup));
            if (GroupNames[i].Length > 0)
            {
                builder.Append(' ').Append(GroupNames[i]);
            }

            hasHigherGroup = true;
        }

        return builder.ToString();
    }

    /// <param name="full">A higher group exists, so "không trăm" / "lẻ" must be spoken.</param>
    private static string ReadGroup(int number, bool full)
    {
        var hundreds = number / 100;
        var tens = number % 100 / 10;
        var ones = number % 10;
        var parts = new List<string>();

        if (full || hundreds > 0)
        {
            parts.Add($"{Digits[hundreds]} trăm");
        }

        switch (tens)
        {
            case 0 when ones > 0 && parts.Count > 0:
                parts.Add("lẻ");
                break;
            case 1:
                parts.Add("mười");
                break;
            case > 1:
                parts.Add($"{Digits[tens]} mươi");
                break;
        }

        if (ones > 0)
        {
            parts.Add(ones switch
            {
                1 when tens > 1 => "mốt",
                4 when tens > 1 => "tư",
                5 when tens > 0 => "lăm",
                _ => Digits[ones]
            });
        }

        return string.Join(' ', parts);
    }
}
