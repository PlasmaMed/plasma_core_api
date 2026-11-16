using CorePlasma.BAL.DTOs.Employees;
using CorePlasma.Common.Enums;
using CorePlasma.DAL.Interfaces;
using FluentValidation;

namespace CorePlasma.BAL.Validators;

public abstract class EmployeeWriteValidator<T> : AbstractValidator<T> where T : EmployeeWriteDto
{
    protected EmployeeWriteValidator()
    {
        RuleFor(x => x.EmployeeCode).MaximumLength(50);
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(150);
        RuleFor(x => x.DateOfBirth).LessThan(_ => DateTime.UtcNow).When(x => x.DateOfBirth.HasValue);
        RuleFor(x => x.Gender).IsInEnum();
        RuleFor(x => x.Phone).ValidOptionalPhone(100);
        RuleFor(x => x.Email).ValidOptionalEmail();
        RuleFor(x => x.Address).MaximumLength(500);
        RuleFor(x => x.Department).MaximumLength(100);
        RuleFor(x => x.Position).MaximumLength(100);
        RuleFor(x => x.IdentityNumber).MaximumLength(50);
        RuleFor(x => x.ResignDate)
            .GreaterThanOrEqualTo(x => x.JoinDate)
            .When(x => x.JoinDate.HasValue && x.ResignDate.HasValue)
            .WithMessage("ResignDate must be on or after JoinDate.");
        RuleFor(x => x.Status).IsInEnum().NotEqual(EmployeeStatus.Deleted).WithMessage("Status 'Deleted' is set only by deleting the record.");
        RuleFor(x => x.Description).MaximumLength(1000);
    }
}

public sealed class CreateEmployeeDtoValidator : EmployeeWriteValidator<CreateEmployeeDto>
{
    public CreateEmployeeDtoValidator(IUnitOfWork unitOfWork)
    {
        RuleFor(x => x.EmployeeCode)
            .MustAsync(async (code, ct) => !await unitOfWork.Employees.ExistsAsync(e => e.EmployeeCode == code, ct))
            .When(x => !string.IsNullOrWhiteSpace(x.EmployeeCode))
            .WithMessage("EmployeeCode already exists.");

        // Optional login account: when a username is given the password is required, and vice versa.
        When(x => !string.IsNullOrWhiteSpace(x.Username) || !string.IsNullOrEmpty(x.Password), () =>
        {
            RuleFor(x => x.Username!)
                .ValidUsername()
                .MustAsync(async (username, ct) => !await unitOfWork.EmployeeUsers.ExistsAsync(u => u.Username == username.Trim(), ct))
                .WithMessage("Username already exists.");
            RuleFor(x => x.Password!).StrongPassword();
        });
    }
}

public sealed class UpdateEmployeeDtoValidator : EmployeeWriteValidator<UpdateEmployeeDto>
{
    public UpdateEmployeeDtoValidator()
    {
        RuleFor(x => x.EmployeeCode).NotEmpty();
    }
}
