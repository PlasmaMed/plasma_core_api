using CorePlasma.BAL.DTOs.Common;
using CorePlasma.BAL.DTOs.DocumentSequences;
using CorePlasma.BAL.DTOs.TankTransfers;
using CorePlasma.Common.Enums;
using CorePlasma.DAL.Interfaces;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace CorePlasma.BAL.Validators;

/// <summary>
/// Shared header rules for every voucher write DTO. Concrete voucher validators inherit this.
/// </summary>
public abstract class VoucherWriteValidator<T> : AbstractValidator<T> where T : VoucherWriteDto
{
    protected VoucherWriteValidator()
    {
        RuleFor(x => x.Note).MaximumLength(1000);
        RuleFor(x => x.VoucherDate)
            .InclusiveBetween(new DateTime(2000, 1, 1), DateTime.UtcNow.AddYears(1))
            .When(x => x.VoucherDate.HasValue);
    }
}

public sealed class VoucherReasonDtoValidator : AbstractValidator<VoucherReasonDto>
{
    public VoucherReasonDtoValidator()
    {
        RuleFor(x => x.Reason).MaximumLength(500);
    }
}

public abstract class TankTransferWriteValidator<T> : VoucherWriteValidator<T> where T : TankTransferWriteDto
{
    public const int MaxItems = 500;

    protected TankTransferWriteValidator(IUnitOfWork unitOfWork)
    {
        RuleFor(x => x)
            .Must(x => x.ToCustomerId.HasValue || x.ToEmployeeId.HasValue || !string.IsNullOrWhiteSpace(x.ToLocation) || x.ToStatus.HasValue)
            .WithName("Destination")
            .WithMessage("Specify at least one of ToCustomerId, ToEmployeeId, ToLocation or ToStatus.");

        RuleFor(x => x.ToCustomerId).MustExistIfProvided(unitOfWork.Customers, "Customer");
        RuleFor(x => x.ToEmployeeId).MustExistIfProvided(unitOfWork.Employees, "Employee");
        RuleFor(x => x.ToLocation).MaximumLength(250);
        RuleFor(x => x.ToStatus).IsInEnum().NotEqual(TankStatus.Deleted).WithMessage("Status 'Deleted' is set only by deleting the record.").When(x => x.ToStatus.HasValue);

        RuleFor(x => x.Items)
            .NotEmpty()
            .Must(items => items.Count <= MaxItems).WithMessage($"A voucher can contain at most {MaxItems} tanks.")
            .Must(items => items.Select(i => i.TankId).Distinct().Count() == items.Count).WithMessage("Each tank may appear only once.");

        RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.TankId).NotEmpty();
            item.RuleFor(i => i.Note).MaximumLength(500);
        });

        RuleFor(x => x.Items)
            .MustAsync(async (items, ct) =>
            {
                var ids = items.Select(i => i.TankId).Distinct().ToList();
                var found = await unitOfWork.Tanks.QueryNoTracking().CountAsync(t => ids.Contains(t.Id), ct);
                return found == ids.Count;
            })
            .When(x => x.Items.Count is > 0 and <= MaxItems)
            .WithMessage("Some tanks do not exist.");
    }
}

public sealed class CreateTankTransferDtoValidator(IUnitOfWork unitOfWork) : TankTransferWriteValidator<CreateTankTransferDto>(unitOfWork);

public sealed class UpdateTankTransferDtoValidator(IUnitOfWork unitOfWork) : TankTransferWriteValidator<UpdateTankTransferDto>(unitOfWork);

public sealed class UpdateDocumentSequenceDtoValidator : AbstractValidator<UpdateDocumentSequenceDto>
{
    private static readonly string[] AllowedSeparators = ["", "-", "/", ".", "_"];
    private static readonly string[] AllowedDateFormats = ["yyyy", "yy", "yyyyMM", "yyMM", "yyyyMMdd", "yyMMdd"];

    public UpdateDocumentSequenceDtoValidator()
    {
        RuleFor(x => x.SequenceName).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Prefix).MaximumLength(20).Matches("^[A-Za-z0-9]*$");
        RuleFor(x => x.Separator)
            .Must(s => AllowedSeparators.Contains(s ?? string.Empty))
            .WithMessage("Separator must be one of: (empty) - / . _");
        RuleFor(x => x.DateFormat)
            .Must(f => AllowedDateFormats.Contains(f))
            .When(x => !string.IsNullOrEmpty(x.DateFormat))
            .WithMessage($"DateFormat must be one of: {string.Join(", ", AllowedDateFormats)}.");
        RuleFor(x => x.Padding).InclusiveBetween(1, 12);
        RuleFor(x => x.ResetPolicy).IsInEnum();

        // Resetting the counter without the matching date part in the number would repeat numbers.
        RuleFor(x => x)
            .Must(x => CoversResetPeriod(x.DateFormat, x.ResetPolicy))
            .WithName("DateFormat")
            .WithMessage("DateFormat must include the reset period (year for Yearly, year+month for Monthly, year+month+day for Daily).");
    }

    private static bool CoversResetPeriod(string? format, SequenceResetPolicy policy)
    {
        format ??= string.Empty;
        return policy switch
        {
            SequenceResetPolicy.Yearly => format.Contains("yy"),
            SequenceResetPolicy.Monthly => format.Contains("yy") && format.Contains("MM"),
            SequenceResetPolicy.Daily => format.Contains("yy") && format.Contains("MM") && format.Contains("dd"),
            _ => true
        };
    }
}
