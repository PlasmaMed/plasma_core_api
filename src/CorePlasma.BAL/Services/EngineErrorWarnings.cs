using System.Globalization;
using CorePlasma.BAL.DTOs.Engines;

namespace CorePlasma.BAL.Services;

/// <summary>A machine usage, with timestamps stored in UTC.</summary>
public sealed record EngineErrorSession(
    Guid Id,
    long? LegacyId,
    DateTime StartTime,
    DateTime? EndTime,
    string? ErrorCode);

/// <summary>
/// Counts each fault independently across completed usages in the current Vietnam calendar day.
/// The usages do not have to be consecutive; normal usages do not reset the day's count.
/// </summary>
public static class EngineErrorWarnings
{
    public const int DefaultMinimumOccurrences = 3;
    private static readonly TimeSpan VietnamUtcOffset = TimeSpan.FromHours(7);

    public static IReadOnlyList<(uint Code, string Message)> Catalogue { get; } =
        Array.AsReadOnly<(uint Code, string Message)>([
            (1u, "Hết khí"),
            (2u, "Nhiệt độ cao"),
            (4u, "Không có 220VAC"),
            (8u, "Van tay 1 đóng/không mở khi đang chạy"),
            (16u, "Van tay 2 đóng/không mở khi đang chạy"),
            (32u, "Cảm biến lưu lượng bị hỏng hoặc ngắt kết nối"),
            (64u, "Cảm biến áp suất cao 1 bị hỏng hoặc ngắt kết nối"),
            (128u, "Cảm biến áp suất cao 2 bị hỏng hoặc ngắt kết nối"),
            (256u, "Cảm biến nhiệt độ bị hỏng hoặc ngắt kết nối"),
            (2048u, "Cảm biến môi trường bị hỏng hoặc ngắt kết nối"),
            (4096u, "Ống dẫn khí bị kẹt"),
            (8192u, "Cảnh báo ống dẫn khí bị kẹt"),
            (16384u, "Van giảm áp bị hỏng"),
            (32768u, "Cảnh báo nhiệt độ NTC"),
            (65536u, "Cảnh báo pin yếu"),
            (131072u, "Lỗi pin yếu"),
            (262144u, "Nhiệt độ môi trường cao"),
            (524288u, "Bình số 1 không được kết nối"),
            (1048576u, "Bình số 2 không được kết nối"),
            (268435456u, "Bình 1 rò khí"),
            (536870912u, "Bình 2 rò khí"),
            (1073741824u, "Bình 1 không xác thực/Không có internet"),
            (2147483648u, "Bình 2 không xác thực/Không có internet")
        ]);

    public static DateTime GetVietnamDayStartUtc(DateTime nowUtc)
    {
        nowUtc = AsUtc(nowUtc);
        return DateTime.SpecifyKind((nowUtc + VietnamUtcOffset).Date - VietnamUtcOffset, DateTimeKind.Utc);
    }

    public static IReadOnlyList<EngineErrorWarningDto> Analyze(
        IEnumerable<EngineErrorSession> sessions,
        DateTime nowUtc,
        int minimumOccurrences = DefaultMinimumOccurrences)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentOutOfRangeException.ThrowIfLessThan(minimumOccurrences, 2);

        nowUtc = AsUtc(nowUtc);
        var dayStartUtc = GetVietnamDayStartUtc(nowUtc);
        var counts = new int[Catalogue.Count];
        var lastOccurredAt = new DateTime[Catalogue.Count];
        var seenIds = new HashSet<Guid>();

        // The query may return unordered rows. Stable identifiers make timestamp ties deterministic.
        foreach (var session in sessions
            .OrderBy(session => AsUtc(session.StartTime))
            .ThenBy(session => session.LegacyId)
            .ThenBy(session => session.Id))
        {
            var startedAtUtc = AsUtc(session.StartTime);
            var endedAtUtc = session.EndTime.HasValue ? AsUtc(session.EndTime.Value) : (DateTime?)null;
            if (startedAtUtc < dayStartUtc || startedAtUtc > nowUtc || !endedAtUtc.HasValue ||
                endedAtUtc < startedAtUtc || endedAtUtc > nowUtc)
            {
                continue;
            }

            // A retry/snapshot of one usage must not make a fault appear to recur.
            if (session.Id != Guid.Empty && !seenIds.Add(session.Id))
            {
                continue;
            }

            // ErrorCode is a decimal unsigned 32-bit mask, including bit 31 (2147483648).
            // Missing, malformed, negative, overflow and normal values do not count as known faults.
            var validMask = uint.TryParse(session.ErrorCode?.Trim(), NumberStyles.None,
                CultureInfo.InvariantCulture, out var mask);
            for (var index = 0; index < Catalogue.Count; index++)
            {
                var (code, _) = Catalogue[index];
                if (validMask && (mask & code) != 0)
                {
                    counts[index]++;
                    if (endedAtUtc.Value > lastOccurredAt[index]) lastOccurredAt[index] = endedAtUtc.Value;
                }
            }
        }

        return Catalogue
            .Select((fault, index) => new { fault.Code, fault.Message, Index = index })
            .Where(fault => counts[fault.Index] >= minimumOccurrences)
            .Select(fault => new EngineErrorWarningDto(fault.Code, fault.Message,
                counts[fault.Index], lastOccurredAt[fault.Index]))
            .ToArray();
    }

    private static DateTime AsUtc(DateTime timestamp) => timestamp.Kind switch
    {
        DateTimeKind.Local => timestamp.ToUniversalTime(),
        DateTimeKind.Unspecified => DateTime.SpecifyKind(timestamp, DateTimeKind.Utc),
        _ => timestamp
    };
}
