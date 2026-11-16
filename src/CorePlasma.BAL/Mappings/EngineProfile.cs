using AutoMapper;
using CorePlasma.BAL.DTOs.Engines;
using CorePlasma.BAL.DTOs.Sessions;
using CorePlasma.DAL.Entities;

namespace CorePlasma.BAL.Mappings;

public sealed class EngineProfile : Profile
{
    private const decimal PressureDropWarningThreshold = 6m * TankSessionDto.PressureUnitsPerPercent;

    public EngineProfile()
    {
        CreateMap<Engine, EngineDto>()
            .ForMember(d => d.ResponsibleEmployeeName,
                o => o.MapFrom(s => s.ResponsibleEmployee != null ? s.ResponsibleEmployee.FullName : null))
            // EngineService enriches this from today's sessions in one batch for the returned machines.
            .ForMember(d => d.RecurringErrorWarnings, o => o.Ignore())
            // ProjectTo evaluates this as EXISTS in the list/detail query, without loading session history.
            // Compare raw readings so rounding the displayed percentages cannot hide a drop just above 6 %.
            .ForMember(d => d.HasPressureDropWarning,
                o => o.MapFrom(s => s.Sessions.Any(session => session.EndTime != null &&
                    session.TankSessions.Any(tank => tank.PressureBefore >= 0m && tank.PressureAfter >= 0m &&
                        tank.PressureBefore - tank.PressureAfter > PressureDropWarningThreshold))));

        CreateMap<Engine, EngineDetailDto>()
            .IncludeBase<Engine, EngineDto>()
            .ForMember(d => d.ResponsibleEmployeeCode,
                o => o.MapFrom(s => s.ResponsibleEmployee != null ? s.ResponsibleEmployee.EmployeeCode : null));

        CreateMap<CreateEngineDto, Engine>();
        CreateMap<UpdateEngineDto, Engine>();
    }
}
