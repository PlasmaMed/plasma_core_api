namespace CorePlasma.Common.Helpers;

/// <summary>
/// The database stores UTC; business dates (voucher date, document numbers, reports) use Vietnam time (UTC+7).
/// </summary>
public static class DateTimeHelper
{
    public static readonly TimeZoneInfo VietnamTimeZone = ResolveVietnamTimeZone();

    /// <summary>Current time in Vietnam.</summary>
    public static DateTime VietnamNow => ToVietnamTime(DateTime.UtcNow);

    /// <summary>Today's date in Vietnam (time 00:00).</summary>
    public static DateTime VietnamToday => VietnamNow.Date;

    public static DateTime ToVietnamTime(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), VietnamTimeZone);

    public static DateTime VietnamToUtc(DateTime vietnamTime) =>
        TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(vietnamTime, DateTimeKind.Unspecified), VietnamTimeZone);

    /// <summary>
    /// UTC range [from, to) covering a whole Vietnam calendar day — use for "created on date X" filters.
    /// </summary>
    public static (DateTime FromUtc, DateTime ToUtcExclusive) VietnamDayRangeUtc(DateTime vietnamDate)
    {
        var start = VietnamToUtc(vietnamDate.Date);
        return (start, start.AddDays(1));
    }

    public static DateTime StartOfMonth(DateTime date) => new(date.Year, date.Month, 1, 0, 0, 0, date.Kind);

    public static DateTime EndOfMonth(DateTime date) => StartOfMonth(date).AddMonths(1).AddTicks(-1);

    public static DateTime StartOfYear(DateTime date) => new(date.Year, 1, 1, 0, 0, 0, date.Kind);

    private static TimeZoneInfo ResolveVietnamTimeZone()
    {
        foreach (var id in new[] { "Asia/Ho_Chi_Minh", "SE Asia Standard Time" })
        {
            if (TimeZoneInfo.TryFindSystemTimeZoneById(id, out var zone))
            {
                return zone;
            }
        }

        return TimeZoneInfo.CreateCustomTimeZone("ICT", TimeSpan.FromHours(7), "Indochina Time", "Indochina Time");
    }
}
