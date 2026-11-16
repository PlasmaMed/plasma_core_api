using AutoMapper;
using CorePlasma.BAL.DTOs.Tanks;
using CorePlasma.DAL.Entities;

namespace CorePlasma.BAL.Mappings;

public sealed class TankProfile : Profile
{
    public TankProfile()
    {
        CreateMap<Tank, TankDto>()
            .ForMember(d => d.SourceIdentities, o => o.Ignore())
            .ForMember(d => d.IsCurrentlyUsed, o => o.Ignore())
            .ForMember(d => d.CurrentEngineId, o => o.Ignore())
            .ForMember(d => d.CurrentEngineCode, o => o.Ignore())
            .ForMember(d => d.GasRemainingPercent, o => o.Ignore())
            .ForMember(d => d.GasWarning, o => o.Ignore())
            .ForMember(d => d.GasUsedPercent, o => o.Ignore())
            .ForMember(d => d.GasReadingTime, o => o.Ignore())
            .ForMember(d => d.CustomerName, o => o.MapFrom(s => s.Customer != null ? s.Customer.CustomerName : null))
            .ForMember(d => d.AssignedEmployeeName, o => o.MapFrom(s => s.AssignedEmployee != null ? s.AssignedEmployee.FullName : null));

        CreateMap<Tank, TankDetailDto>()
            .IncludeBase<Tank, TankDto>()
            .ForMember(d => d.CustomerCode, o => o.MapFrom(s => s.Customer != null ? s.Customer.CustomerCode : null))
            .ForMember(d => d.AssignedEmployeeCode, o => o.MapFrom(s => s.AssignedEmployee != null ? s.AssignedEmployee.EmployeeCode : null));

        CreateMap<CreateTankDto, Tank>();
        CreateMap<UpdateTankDto, Tank>();
    }
}
