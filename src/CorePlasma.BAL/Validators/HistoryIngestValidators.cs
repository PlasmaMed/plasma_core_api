using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;
using CorePlasma.BAL.DTOs.Integration;
using FluentValidation;

namespace CorePlasma.BAL.Validators;

public sealed class HistorySessionIngestDtoValidator : AbstractValidator<HistorySessionIngestDto>
{
    public const int MaxTankSessions = 100;

    public HistorySessionIngestDtoValidator()
    {
        RuleFor(x => x.Source).Equal("NEWAPP");
        RuleFor(x => x.EventId).NotEmpty();
        RuleFor(x => x.SourceRevision).GreaterThan(0);
        RuleFor(x => x.SourceSessionId).GreaterThan(0);
        RuleFor(x => x.EngineSerial).NotEmpty().MaximumLength(100);
        // Device clock errors remain visible in the history; do not reject old/future dates or negative durations.
        RuleFor(x => x.StartTime).Must(x => x.Offset == TimeSpan.Zero).WithMessage("StartTime must use UTC.");
        RuleFor(x => x.EndTime).Must(x => !x.HasValue || x.Value.Offset == TimeSpan.Zero).WithMessage("EndTime must use UTC.");
        RuleFor(x => x.Mode).MaximumLength(100);
        RuleFor(x => x.AverageSpeed).MaximumLength(50);
        RuleFor(x => x.PlasmaOnTime).MaximumLength(50);
        RuleFor(x => x.EnvironmentTemperatureBefore).MaximumLength(50);
        RuleFor(x => x.EnvironmentTemperatureAfter).MaximumLength(50);
        RuleFor(x => x.HandleTemperatureBefore).MaximumLength(50);
        RuleFor(x => x.HandleTemperatureAfter).MaximumLength(50);
        RuleFor(x => x.ErrorCode).MaximumLength(100);
        RuleFor(x => x.UserCode).MaximumLength(200);
        RuleFor(x => x.TankSessions).Cascade(CascadeMode.Stop).NotNull()
            .Must(x => x.Count <= MaxTankSessions).WithMessage($"At most {MaxTankSessions} tank sessions are allowed.")
            .Must(x => x.All(t => t is not null)).WithMessage("Tank sessions cannot contain null entries.")
            .Must(x => x.Select(t => t.SourceTankSessionId).Distinct().Count() == x.Count)
            .WithMessage("SourceTankSessionId must be unique within the event.");
        RuleForEach(x => x.TankSessions).SetValidator(new HistoryTankSessionIngestDtoValidator());
    }
}

public sealed class HistoryTankSessionIngestDtoValidator : AbstractValidator<HistoryTankSessionIngestDto>
{
    public HistoryTankSessionIngestDtoValidator()
    {
        RuleFor(x => x.SourceTankSessionId).GreaterThan(0);
        RuleFor(x => x.SourceTankId).Must(x => x is null || x > 0)
            .WithMessage("SourceTankId must be positive when supplied.");
        RuleFor(x => x.TankRfid).MaximumLength(50);
        RuleFor(x => x.TankSerial).MaximumLength(50);
        RuleFor(x => x.TankLegacyId).MaximumLength(50);
        RuleFor(x => x.HighPressureBefore).Must(HistoryMeasurement.TryParse).WithMessage("HighPressureBefore must be a plain invariant decimal or null.");
        RuleFor(x => x.HighPressureAfter).Must(HistoryMeasurement.TryParse).WithMessage("HighPressureAfter must be a plain invariant decimal or null.");
        RuleFor(x => x.LoadCellBefore).Must(HistoryMeasurement.TryParse).WithMessage("LoadCellBefore must be a plain invariant decimal or null.");
        RuleFor(x => x.LoadCellAfter).Must(HistoryMeasurement.TryParse).WithMessage("LoadCellAfter must be a plain invariant decimal or null.");
    }
}

public static class HistoryMeasurement
{
    public static bool TryParse(string? value) => TryParse(value, out _);

    public static bool TryParse(string? value, out decimal? number)
    {
        number = null;
        if (string.IsNullOrWhiteSpace(value)) return true;
        var text = value.Trim();
        if (text.Length > 100 || !Regex.IsMatch(text, @"\A-?[0-9]+(?:\.[0-9]+)?\z", RegexOptions.CultureInvariant) ||
            !decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed))
            return false;
        // Decimal.TryParse can silently round excess fractional precision. Reject that loss of source data.
        var dot = text.IndexOf('.');
        var sourceScale = dot < 0 ? 0 : text.Length - dot - 1;
        var sourceInteger = BigInteger.Parse(text.Replace(".", string.Empty), CultureInfo.InvariantCulture);
        var bits = decimal.GetBits(parsed);
        var parsedInteger = (BigInteger)(uint)bits[0] | ((BigInteger)(uint)bits[1] << 32) | ((BigInteger)(uint)bits[2] << 64);
        if (bits[3] < 0) parsedInteger = -parsedInteger;
        var parsedScale = (bits[3] >> 16) & 0xff;
        if (sourceInteger * BigInteger.Pow(10, parsedScale) != parsedInteger * BigInteger.Pow(10, sourceScale)) return false;
        number = parsed;
        return true;
    }
}
