using CorePlasma.BAL.DTOs.Customers;
using CorePlasma.Common.Enums;
using CorePlasma.DAL.Interfaces;
using FluentValidation;

namespace CorePlasma.BAL.Validators;

public abstract class CustomerWriteValidator<T> : AbstractValidator<T> where T : CustomerWriteDto
{
    protected CustomerWriteValidator()
    {
        RuleFor(x => x.CustomerCode).MaximumLength(50);
        RuleFor(x => x.CustomerName).NotEmpty().MaximumLength(250);
        RuleFor(x => x.ShortName).MaximumLength(100);
        RuleFor(x => x.TaxCode).MaximumLength(50);
        RuleFor(x => x.Address).MaximumLength(500);
        RuleFor(x => x.Phone).ValidOptionalPhone();
        RuleFor(x => x.Email).ValidOptionalEmail();
        RuleFor(x => x.ContactPerson).MaximumLength(150);
        RuleFor(x => x.ContactPhone).ValidOptionalPhone();
        RuleFor(x => x.ContactEmail).ValidOptionalEmail();
        RuleFor(x => x.Website).MaximumLength(250);
        RuleFor(x => x.Description).MaximumLength(1000);
        RuleFor(x => x.Status).IsInEnum().NotEqual(CustomerStatus.Deleted).WithMessage("Status 'Deleted' is set only by deleting the record.");
    }
}

public sealed class CreateCustomerDtoValidator : CustomerWriteValidator<CreateCustomerDto>
{
    public CreateCustomerDtoValidator(IUnitOfWork unitOfWork)
    {
        RuleFor(x => x.CustomerCode)
            .MustAsync(async (code, ct) => !await unitOfWork.Customers.ExistsAsync(c => c.CustomerCode == code, ct))
            .When(x => !string.IsNullOrWhiteSpace(x.CustomerCode))
            .WithMessage("CustomerCode already exists.");
    }
}

public sealed class UpdateCustomerDtoValidator : CustomerWriteValidator<UpdateCustomerDto>
{
    public UpdateCustomerDtoValidator()
    {
        RuleFor(x => x.CustomerCode).NotEmpty();
    }
}
