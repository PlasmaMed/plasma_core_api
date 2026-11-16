using AutoMapper;
using AutoMapper.QueryableExtensions;
using CorePlasma.BAL.DTOs.Engines;
using CorePlasma.BAL.Mappings;
using CorePlasma.DAL.Context;
using CorePlasma.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.RegularExpressions;

// Synthetic records only; no appsettings, credentials or operational database are used.
var configuration = new MapperConfiguration(options => options.AddProfile<EngineProfile>(), NullLoggerFactory.Instance);
var mapper = configuration.CreateMapper();
var count = 0;

void Check(bool condition, string label)
{
    if (!condition) throw new InvalidOperationException(label);
    count++;
    Console.WriteLine("PASS: " + label);
}

Session Usage(decimal? before, decimal? after, bool completed = true) => new()
{
    StartTime = new DateTime(2026, 10, 13, 1, 0, 0, DateTimeKind.Utc),
    EndTime = completed ? new DateTime(2026, 10, 13, 1, 5, 0, DateTimeKind.Utc) : null,
    TankSessions = [new() { PressureBefore = before, PressureAfter = after }]
};

void Case(string label, bool expected, params Session[] sessions)
{
    var engine = new Engine { Sessions = sessions.ToList() };
    var list = new[] { engine }.AsQueryable().ProjectTo<EngineDto>(configuration).Single();
    var detail = new[] { engine }.AsQueryable().ProjectTo<EngineDetailDto>(configuration).Single();
    Check(list.HasPressureDropWarning == expected && detail.HasPressureDropWarning == expected &&
        mapper.Map<EngineDto>(engine).HasPressureDropWarning == expected, label);
}

Case("No usage history has no warning", false);
Case("Pressure drop below 6 percentage points has no warning", false, Usage(12500m, 11751m));
Case("Pressure drop exactly 6 percentage points has no warning", false, Usage(12500m, 11750m));
Case("Fractional pressure drop just above 6 percentage points warns before display rounding", true, Usage(12500m, 11749.999m));
Case("Pressure drop above 6 percentage points warns", true, Usage(12500m, 11749m));
Case("Unfinished session does not warn", false, Usage(12500m, 11000m, completed: false));
Case("Missing before reading does not warn", false, Usage(null, 100m));
Case("Missing after reading does not warn", false, Usage(12500m, null));
Case("Missing both readings does not warn", false, Usage(null, null));
Case("Negative before reading does not warn", false, Usage(-1m, -1000m));
Case("Negative after reading does not warn", false, Usage(12500m, -1m));
Case("Valid zero after reading can warn", true, Usage(751m, 0m));
Case("Pressure increase does not warn", false, Usage(11000m, 12500m));
Case("Drops from separate sessions are not accumulated", false, Usage(12500m, 12000m), Usage(12000m, 11500m));
Case("Historical fault remains visible after a normal session", true, Usage(12500m, 11000m), Usage(12500m, 12000m));

var twoTanks = Usage(12500m, 12200m);
twoTanks.TankSessions.Add(new() { PressureBefore = 12500m, PressureAfter = 12000m });
Case("Drops from separate tanks are not accumulated", false, twoTanks);
twoTanks.TankSessions.Add(new() { PressureBefore = 12500m, PressureAfter = 11000m });
Case("One faulty tank among multiple tanks warns", true, twoTanks);

// Translating with the real PostgreSQL provider does not open a connection.
using var context = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
    .UseNpgsql("Host=127.0.0.1;Database=unused_engine_warning_translation;Username=unused")
    .Options);
foreach (var sql in new[]
{
    context.Engines.AsNoTracking().ProjectTo<EngineDto>(configuration).Take(20).ToQueryString(),
    context.Engines.AsNoTracking().Where(engine => engine.Id == Guid.Empty)
        .ProjectTo<EngineDetailDto>(configuration).ToQueryString()
})
{
    Check(sql.Contains("EXISTS", StringComparison.Ordinal) && sql.Contains("750.0", StringComparison.Ordinal) &&
        sql.Contains("\"EndTime\" IS NOT NULL", StringComparison.Ordinal) &&
        sql.Contains("\"PressureBefore\" >= 0.0", StringComparison.Ordinal) &&
        sql.Contains("\"PressureAfter\" >= 0.0", StringComparison.Ordinal),
        "PostgreSQL list/detail projection evaluates the completed-session threshold in one query");
    Check(Regex.IsMatch(sql, @"FROM ""Sessions"" AS (?<alias>\w+)[\s\S]*?NOT\s+\(?\k<alias>\.""IsDeleted""") &&
        Regex.IsMatch(sql, @"FROM ""TankSessions"" AS (?<alias>\w+)[\s\S]*?NOT\s+\(?\k<alias>\.""IsDeleted"""),
        "PostgreSQL projection excludes deleted sessions and tank readings");
}

Console.WriteLine($"PASS: {count} engine pressure warning checks.");
