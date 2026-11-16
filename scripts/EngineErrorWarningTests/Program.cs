using CorePlasma.BAL.Services;
using CorePlasma.BAL.DTOs.Engines;
using System.Globalization;

// Synthetic usage records only. The analyzer has no database or configuration dependency.
var nowUtc = new DateTime(2026, 10, 13, 10, 0, 0, DateTimeKind.Utc);
var dayStartUtc = new DateTime(2026, 10, 12, 17, 0, 0, DateTimeKind.Utc);
var checks = 0;
void Check(bool condition, string label)
{
    if (!condition) throw new InvalidOperationException(label);
    checks++;
    Console.WriteLine("PASS: " + label);
}

EngineErrorSession Usage(int sequence, string? code, DateTime? start = null, DateTime? end = null,
    bool completed = true, Guid? id = null, long? legacyId = null)
{
    var startUtc = start ?? dayStartUtc.AddMinutes(sequence * 10);
    return new EngineErrorSession(id ?? Guid.NewGuid(), legacyId, startUtc,
        completed ? end ?? startUtc.AddMinutes(1) : null, code);
}

IReadOnlyList<EngineErrorWarningDto> Analyze(params EngineErrorSession[] sessions) =>
    EngineErrorWarnings.Analyze(sessions, nowUtc);

Check(EngineErrorWarnings.GetVietnamDayStartUtc(nowUtc) == dayStartUtc,
    "Vietnam day starts at 17:00 UTC on the preceding date");
Check(Analyze().Count == 0, "Empty history has no recurring warning");
Check(Analyze(Usage(1, "1"), Usage(2, "1")).Count == 0,
    "Two completed occurrences remain below the default threshold");
var three = Analyze(Usage(1, "1"), Usage(2, "1"), Usage(3, "1"));
Check(three is [{ Code: 1, Label: "Hết khí", OccurrenceCount: 3 }],
    "Three faults trigger the correct warning");
Check(Analyze(Usage(1, "1"), Usage(2, "0"), Usage(3, "1"), Usage(4, "2"), Usage(5, "1"))
    is [{ Code: 1, OccurrenceCount: 3 }], "Three nonconsecutive occurrences today trigger a warning");
var four = Analyze(Usage(1, "1"), Usage(2, "1"), Usage(3, "1"), Usage(4, "1"));
Check(four is [{ OccurrenceCount: 4 }], "Fourth fault updates the displayed occurrence count");
Check(EngineErrorWarnings.Analyze([Usage(1, "1"), Usage(2, "1"), Usage(3, "1")], nowUtc, 4).Count == 0,
    "Configurable four-occurrence threshold does not warn at three");
Check(EngineErrorWarnings.Analyze([Usage(1, "1"), Usage(2, "1"), Usage(3, "1"), Usage(4, "1")], nowUtc, 4)
    is [{ OccurrenceCount: 4 }], "Configurable four-occurrence threshold warns at four");

foreach (var interruption in new string?[] { "0", null, "", "fault", "1.0", "-1", "-2147483648", "4294967296", "512" })
{
    Check(Analyze(Usage(1, "1"), Usage(2, "1"), Usage(3, interruption), Usage(4, "1"))
        is [{ Code: 1, OccurrenceCount: 3 }],
        $"Normal, missing, invalid or unknown-only code '{interruption ?? "null"}' does not reset today's count");
    Check(Analyze(Usage(1, "1"), Usage(2, "1"), Usage(3, interruption)).Count == 0,
        $"Normal, missing, invalid or unknown-only code '{interruption ?? "null"}' does not count as a fault");
}
Check(Analyze(Usage(1, "1"), Usage(2, "2"), Usage(3, "1"), Usage(4, "2")).Count == 0,
    "Different faults do not accumulate into a shared recurrence threshold");
Check(Analyze(Usage(1, "1"), Usage(2, "3"), Usage(3, "5")) is [{ Code: 1, OccurrenceCount: 3 }],
    "Known bits are counted independently when a composite mask changes");
Check(Analyze(Usage(1, "3"), Usage(2, "3"), Usage(3, "3"))
    is [{ Code: 1 }, { Code: 2 }], "Each repeated bit in a composite mask produces its own warning");
Check(Analyze(Usage(1, "513"), Usage(2, "513"), Usage(3, "513")) is [{ Code: 1 }],
    "Unknown bits do not invent fault descriptions or hide a known bit");
Check(Analyze(Usage(1, "2147483648"), Usage(2, "2147483648"), Usage(3, "2147483648"))
    is [{ Code: 2147483648L, Label: "Bình 2 không xác thực/Không có internet" }],
    "Bit 31 works beyond signed Int32 without negative-code conversion");
Check(Analyze(Usage(1, "2147483649"), Usage(2, "2147483649"), Usage(3, "2147483649"))
    is [{ Code: 1 }, { Code: 2147483648L }], "High-bit composite masks decode safely");
Check(Analyze(Usage(1, " 1 "), Usage(2, "1"), Usage(3, "1")) is [{ Code: 1 }],
    "Whitespace around a decimal source code is accepted");
Check(Analyze(Usage(1, "4294967295"), Usage(2, "4294967295"), Usage(3, "4294967295")).Count == 23,
    "Maximum unsigned 32-bit mask decodes all 23 supplied known faults");

var preceding = Usage(1, "1", start: dayStartUtc.AddMinutes(-10));
Check(Analyze(preceding, Usage(2, "1"), Usage(3, "1")).Count == 0,
    "Previous-day faults cannot combine with today's faults");
Check(Analyze(Usage(1, "1", start: dayStartUtc.AddMinutes(-30)),
    Usage(2, "1", start: dayStartUtc.AddMinutes(-20)),
    Usage(3, "1", start: dayStartUtc.AddMinutes(-10))).Count == 0,
    "Qualifying previous-day occurrences do not leave a stale warning today");
Check(Analyze(Usage(1, "1", start: dayStartUtc), Usage(2, "1"), Usage(3, "1")) is [{ Code: 1 }],
    "Session starting exactly at Vietnam midnight belongs to the current day");
var nextDayUtc = new DateTime(2026, 10, 13, 17, 0, 0, DateTimeKind.Utc);
Check(EngineErrorWarnings.Analyze([Usage(1, "1"), Usage(2, "1"), Usage(3, "1")], nextDayUtc).Count == 0,
    "Warnings expire exactly at the next Vietnam midnight");
Check(Analyze(Usage(1, "1"), Usage(2, "1"), Usage(3, "1", start: nowUtc.AddMinutes(1))).Count == 0,
    "A future usage does not supply the third occurrence");
Check(Analyze(Usage(1, "1"), Usage(2, "1"), Usage(3, "1", end: nowUtc.AddMinutes(1))).Count == 0,
    "A usage ending in the future is not completed for this evaluation");
Check(Analyze(Usage(1, "1"), Usage(2, "1"), Usage(3, "1", end: dayStartUtc)).Count == 0,
    "An end-before-start record does not supply the third occurrence");
Check(Analyze(Usage(1, "1"), Usage(2, "1"), Usage(3, "1", completed: false)).Count == 0,
    "An unfinished usage does not supply the third occurrence");
Check(Analyze(Usage(1, "1"), Usage(2, "0", completed: false), Usage(3, "1"), Usage(4, "1")) is [{ Code: 1 }],
    "Unfinished usage does not affect fault counts from completed usages");
Check(Analyze(Usage(1, "1"), Usage(2, "1"), Usage(3, "1"), Usage(4, "0"))
    is [{ OccurrenceCount: 3 }], "A warning stays visible after subsequent normal usages today");
var latest = Usage(7, "1");
Check(Analyze(Usage(1, "1"), Usage(2, "1"), Usage(3, "1"), Usage(4, "0"),
    Usage(5, "1"), Usage(6, "1"), latest)
    is [{ OccurrenceCount: 6 } latestWarning] && latestWarning.LastOccurredAt == latest.EndTime,
    "All six occurrences today count despite a normal usage between them");
Check(Analyze(Usage(3, "1"), Usage(1, "1"), Usage(2, "0"), Usage(4, "1"))
    is [{ OccurrenceCount: 3 }], "Unordered usage history produces the same daily occurrence count");
var tiedStart = dayStartUtc.AddMinutes(10);
Check(Analyze(Usage(3, "1", tiedStart, legacyId: 3), Usage(1, "1", tiedStart, legacyId: 1),
    Usage(2, "0", tiedStart, legacyId: 2), Usage(4, "1", legacyId: 4))
    is [{ OccurrenceCount: 3 }], "Distinct usages with equal timestamps each count once");
var overlapping = Usage(1, "1", end: nowUtc);
Check(Analyze(overlapping, Usage(2, "1"), Usage(3, "1"))
    is [{ OccurrenceCount: 3 } overlappingWarning] && overlappingWarning.LastOccurredAt == nowUtc,
    "Latest occurrence uses the maximum end time even when start and end ordering differ");
Check(Analyze(Usage(1, "3"), Usage(2, "1"), Usage(3, "2"), Usage(4, "3"))
    is [{ Code: 1, OccurrenceCount: 3 }, { Code: 2, OccurrenceCount: 3 }],
    "Each bit reaches three occurrences independently across composite and individual errors");

var repeated = Usage(1, "1");
Check(Analyze(repeated, repeated, repeated).Count == 0,
    "Repeating a snapshot of one session cannot create a recurring warning");
Check(Analyze(Usage(1, "1", id: Guid.Empty), Usage(2, "1", id: Guid.Empty), Usage(3, "1", id: Guid.Empty))
    is [{ OccurrenceCount: 3 }], "Unassigned IDs do not collapse distinct synthetic usages");

// Independent expectations from the device's protocol, rather than from the production catalogue.
var expectedFaults = new (uint Code, string Label)[]
{
    (1, "Hết khí"),
    (2, "Nhiệt độ cao"),
    (4, "Không có 220VAC"),
    (8, "Van tay 1 đóng/không mở khi đang chạy"),
    (16, "Van tay 2 đóng/không mở khi đang chạy"),
    (32, "Cảm biến lưu lượng bị hỏng hoặc ngắt kết nối"),
    (64, "Cảm biến áp suất cao 1 bị hỏng hoặc ngắt kết nối"),
    (128, "Cảm biến áp suất cao 2 bị hỏng hoặc ngắt kết nối"),
    (256, "Cảm biến nhiệt độ bị hỏng hoặc ngắt kết nối"),
    (2048, "Cảm biến môi trường bị hỏng hoặc ngắt kết nối"),
    (4096, "Ống dẫn khí bị kẹt"),
    (8192, "Cảnh báo ống dẫn khí bị kẹt"),
    (16384, "Van giảm áp bị hỏng"),
    (32768, "Cảnh báo nhiệt độ NTC"),
    (65536, "Cảnh báo pin yếu"),
    (131072, "Lỗi pin yếu"),
    (262144, "Nhiệt độ môi trường cao"),
    (524288, "Bình số 1 không được kết nối"),
    (1048576, "Bình số 2 không được kết nối"),
    (268435456, "Bình 1 rò khí"),
    (536870912, "Bình 2 rò khí"),
    (1073741824, "Bình 1 không xác thực/Không có internet"),
    (2147483648, "Bình 2 không xác thực/Không có internet")
};
foreach (var (code, label) in expectedFaults)
{
    var value = code.ToString(CultureInfo.InvariantCulture);
    Check(Analyze(Usage(1, value), Usage(2, value), Usage(3, value))
        is [var warning] && warning.Code == code && warning.Label == label,
        $"Catalogue maps fault {code} independently");
}
Check(EngineErrorWarnings.Catalogue.Select(fault => fault.Code).Distinct().Count() == 23 &&
    EngineErrorWarnings.Catalogue.All(fault => fault.Code != 0 && (fault.Code & (fault.Code - 1)) == 0),
    "Catalogue contains exactly 23 unique single-bit fault codes");

Console.WriteLine($"PASS: {checks} recurring engine fault checks.");
