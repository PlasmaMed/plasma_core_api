using CorePlasma.BAL.DTOs.Tanks;
using CorePlasma.Common.Constants;
using CorePlasma.Common.Enums;
using CorePlasma.DAL.Interfaces;
using FluentValidation;

namespace CorePlasma.BAL.Validators;

public abstract class TankWriteValidator<T> : AbstractValidator<T> where T : TankWriteDto
{
    protected TankWriteValidator(IUnitOfWork unitOfWork)
    {
        RuleFor(x => x.TankCode).MaximumLength(50);
        RuleFor(x => x.SerialNumber).MaximumLength(100);
        RuleFor(x => x.TankType).MaximumLength(100);
        RuleFor(x => x.GasType)
            .Must(g => GasTypes.All.Contains(g!, StringComparer.OrdinalIgnoreCase))
            .When(x => !string.IsNullOrWhiteSpace(x.GasType))
            .WithMessage($"GasType must be one of: {string.Join(", ", GasTypes.All)}.");
        RuleFor(x => x.Manufacturer).MaximumLength(150);
        RuleFor(x => x.NextInspectionDate)
            .GreaterThanOrEqualTo(x => x.InspectionDate)
            .When(x => x.InspectionDate.HasValue && x.NextInspectionDate.HasValue)
            .WithMessage("NextInspectionDate must be on or after InspectionDate.");
        RuleFor(x => x.Capacity).GreaterThanOrEqualTo(0).When(x => x.Capacity.HasValue);
        RuleFor(x => x.WorkingPressure).GreaterThanOrEqualTo(0).When(x => x.WorkingPressure.HasValue);
        RuleFor(x => x.CurrentPressure).GreaterThanOrEqualTo(0).When(x => x.CurrentPressure.HasValue);
        RuleFor(x => x.Unit).MaximumLength(20);
        RuleFor(x => x.Status).IsInEnum().NotEqual(TankStatus.Deleted).WithMessage("Status 'Deleted' is set only by deleting the record.");
        RuleFor(x => x.CurrentLocation).MaximumLength(250);
        RuleFor(x => x.Description).MaximumLength(1000);
        RuleFor(x => x.Store).MaximumLength(10);
        RuleFor(x => x.CustomerId).MustExistIfProvided(unitOfWork.Customers, "Customer");
        RuleFor(x => x.AssignedEmployeeId).MustExistIfProvided(unitOfWork.Employees, "Employee");
    }
}

public sealed class CreateTankDtoValidator : TankWriteValidator<CreateTankDto>
{
    public CreateTankDtoValidator(IUnitOfWork unitOfWork) : base(unitOfWork)
    {
        RuleFor(x => x.TankCode)
            .MustAsync(async (code, ct) => !await unitOfWork.Tanks.ExistsAsync(t => t.TankCode == code, ct))
            .When(x => !string.IsNullOrWhiteSpace(x.TankCode))
            .WithMessage("TankCode already exists.");

        RuleFor(x => x.SerialNumber)
            .MustAsync(async (serial, ct) => !await unitOfWork.Tanks.ExistsAsync(t => t.SerialNumber == serial, ct))
            .When(x => !string.IsNullOrWhiteSpace(x.SerialNumber))
            .WithMessage("SerialNumber already exists.");
    }
}

public sealed class UpdateTankDtoValidator : TankWriteValidator<UpdateTankDto>
{
    public UpdateTankDtoValidator(IUnitOfWork unitOfWork) : base(unitOfWork)
    {
        RuleFor(x => x.TankCode).NotEmpty();
    }
}
