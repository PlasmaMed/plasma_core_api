using CorePlasma.BAL.DTOs.Permissions;
using FluentValidation;

namespace CorePlasma.BAL.Validators;

public sealed class UpdateEmployeePermissionsDtoValidator : AbstractValidator<UpdateEmployeePermissionsDto>
{
    public UpdateEmployeePermissionsDtoValidator()
    {
        RuleFor(x => x.PermissionIds).NotNull();
        RuleForEach(x => x.PermissionIds).NotEmpty().WithMessage("PermissionIds must contain valid ids.");
    }
}

public sealed class AddEmployeePermissionsDtoValidator : AbstractValidator<AddEmployeePermissionsDto>
{
    public AddEmployeePermissionsDtoValidator()
    {
        RuleFor(x => x.PermissionIds).NotEmpty();
        RuleForEach(x => x.PermissionIds).NotEmpty().WithMessage("PermissionIds must contain valid ids.");
        RuleFor(x => x.ExpiredDate)
            .GreaterThan(_ => DateTime.UtcNow)
            .When(x => x.ExpiredDate.HasValue)
            .WithMessage("ExpiredDate must be in the future.");
    }
}

public sealed class UpdatePermissionMatrixDtoValidator : AbstractValidator<UpdatePermissionMatrixDto>
{
    public const int MaxChanges = 5000;

    public UpdatePermissionMatrixDtoValidator()
    {
        RuleFor(x => x.Changes)
            .NotEmpty()
            .Must(c => c.Count <= MaxChanges)
            .WithMessage($"A batch can contain at most {MaxChanges} changes.");

        RuleForEach(x => x.Changes).ChildRules(change =>
        {
            change.RuleFor(c => c.EmployeeId).NotEmpty();
            change.RuleFor(c => c.PermissionId).NotEmpty();
        });
    }
}
