using CorePlasma.BAL.DTOs.Users;
using CorePlasma.Common.Enums;
using CorePlasma.DAL.Interfaces;
using FluentValidation;

namespace CorePlasma.BAL.Validators;

public sealed class CreateEmployeeUserDtoValidator : AbstractValidator<CreateEmployeeUserDto>
{
    public CreateEmployeeUserDtoValidator(IUnitOfWork unitOfWork)
    {
        RuleFor(x => x.EmployeeId)
            .NotEmpty()
            .MustAsync(async (id, ct) => await unitOfWork.Employees.ExistsAsync(e => e.Id == id, ct))
            .WithMessage("Employee does not exist.")
            .MustAsync(async (id, ct) => !await unitOfWork.EmployeeUsers.ExistsAsync(u => u.EmployeeId == id, ct))
            .WithMessage("This employee already has an account.");

        RuleFor(x => x.Username)
            .ValidUsername()
            .MustAsync(async (username, ct) => !await unitOfWork.EmployeeUsers.ExistsAsync(u => u.Username == username, ct))
            .WithMessage("Username already exists.");

        RuleFor(x => x.Password).StrongPassword();
        RuleFor(x => x.Status).IsInEnum().NotEqual(UserStatus.Deleted).WithMessage("Status 'Deleted' is set only by deleting the record.");
    }
}

public sealed class UpdateEmployeeUserDtoValidator : AbstractValidator<UpdateEmployeeUserDto>
{
    public UpdateEmployeeUserDtoValidator()
    {
        RuleFor(x => x.Username).ValidUsername();
        RuleFor(x => x.Status).IsInEnum().NotEqual(UserStatus.Deleted).WithMessage("Status 'Deleted' is set only by deleting the record.");
    }
}

public abstract class CustomerUserWriteValidator<T> : AbstractValidator<T> where T : CustomerUserWriteDto
{
    protected CustomerUserWriteValidator()
    {
        RuleFor(x => x.Username).ValidUsername();
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Email).ValidOptionalEmail();
        RuleFor(x => x.Phone).ValidOptionalPhone();
        RuleFor(x => x.Position).MaximumLength(100);
        RuleFor(x => x.Status).IsInEnum().NotEqual(UserStatus.Deleted).WithMessage("Status 'Deleted' is set only by deleting the record.");
    }
}

public sealed class CreateCustomerUserDtoValidator : CustomerUserWriteValidator<CreateCustomerUserDto>
{
    public CreateCustomerUserDtoValidator(IUnitOfWork unitOfWork)
    {
        RuleFor(x => x.CustomerId)
            .NotEmpty()
            .MustAsync(async (id, ct) => await unitOfWork.Customers.ExistsAsync(c => c.Id == id, ct))
            .WithMessage("Customer does not exist.");

        RuleFor(x => x.Username)
            .MustAsync(async (username, ct) => !await unitOfWork.CustomerUsers.ExistsAsync(u => u.Username == username, ct))
            .WithMessage("Username already exists.");

        RuleFor(x => x.Password).StrongPassword();
    }
}

public sealed class UpdateCustomerUserDtoValidator : CustomerUserWriteValidator<UpdateCustomerUserDto>;

public sealed class ResetPasswordDtoValidator : AbstractValidator<ResetPasswordDto>
{
    public ResetPasswordDtoValidator()
    {
        RuleFor(x => x.NewPassword).StrongPassword();
    }
}
