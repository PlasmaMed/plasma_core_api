using CorePlasma.BAL.DTOs.Engines;
using CorePlasma.Common.Enums;
using CorePlasma.DAL.Interfaces;
using FluentValidation;

namespace CorePlasma.BAL.Validators;

public abstract class EngineWriteValidator<T> : AbstractValidator<T> where T : EngineWriteDto
{
    protected EngineWriteValidator(IUnitOfWork unitOfWork)
    {
        RuleFor(x => x.EngineCode).MaximumLength(50);
        RuleFor(x => x.EngineName).NotEmpty().MaximumLength(250);
        RuleFor(x => x.SerialNumber).MaximumLength(100);
        RuleFor(x => x.Model).MaximumLength(100);
        RuleFor(x => x.Manufacturer).MaximumLength(150);
        RuleFor(x => x.ManufactureYear)
            .InclusiveBetween(1900, DateTime.UtcNow.Year + 1)
            .When(x => x.ManufactureYear.HasValue);
        RuleFor(x => x.Power).GreaterThanOrEqualTo(0).When(x => x.Power.HasValue);
        RuleFor(x => x.Voltage).GreaterThanOrEqualTo(0).When(x => x.Voltage.HasValue);
        RuleFor(x => x.Current).GreaterThanOrEqualTo(0).When(x => x.Current.HasValue);
        RuleFor(x => x.CuttingThickness).GreaterThanOrEqualTo(0).When(x => x.CuttingThickness.HasValue);
        RuleFor(x => x.CurrentLocation).MaximumLength(250);
        RuleFor(x => x.Department).MaximumLength(100);
        RuleFor(x => x.Status).IsInEnum().NotEqual(EngineStatus.Deleted).WithMessage("Status 'Deleted' is set only by deleting the record.");
        RuleFor(x => x.NextMaintenanceDate)
            .GreaterThanOrEqualTo(x => x.LastMaintenanceDate)
            .When(x => x.LastMaintenanceDate.HasValue && x.NextMaintenanceDate.HasValue)
            .WithMessage("NextMaintenanceDate must be on or after LastMaintenanceDate.");
        RuleFor(x => x.Description).MaximumLength(1000);
        RuleFor(x => x.EngineType).MaximumLength(30);
        RuleFor(x => x.TankType).MaximumLength(20);
        RuleFor(x => x.Store).MaximumLength(10);
        RuleFor(x => x.ResponsibleEmployeeId).MustExistIfProvided(unitOfWork.Employees, "Employee");
    }
}

public sealed class CreateEngineDtoValidator : EngineWriteValidator<CreateEngineDto>
{
    public CreateEngineDtoValidator(IUnitOfWork unitOfWork) : base(unitOfWork)
    {
        RuleFor(x => x.EngineCode)
            .MustAsync(async (code, ct) => !await unitOfWork.Engines.ExistsAsync(e => e.EngineCode == code, ct))
            .When(x => !string.IsNullOrWhiteSpace(x.EngineCode))
            .WithMessage("EngineCode already exists.");
    }
}

public sealed class UpdateEngineDtoValidator : EngineWriteValidator<UpdateEngineDto>
{
    public UpdateEngineDtoValidator(IUnitOfWork unitOfWork) : base(unitOfWork)
    {
        RuleFor(x => x.EngineCode).NotEmpty();
    }
}
