using AutoMapper;
using CorePlasma.BAL.DTOs.Employees;
using CorePlasma.DAL.Entities;

namespace CorePlasma.BAL.Mappings;

public sealed class EmployeeProfile : Profile
{
    public EmployeeProfile()
    {
        CreateMap<Employee, EmployeeDto>()
            .ForMember(d => d.IsSuperAdmin, o => o.MapFrom(s => s.User != null && s.User.IsSuperAdmin));
        CreateMap<Employee, EmployeeDetailDto>()
            .IncludeBase<Employee, EmployeeDto>()
            .ForMember(d => d.HasLogin, o => o.MapFrom(s => s.User != null))
            .ForMember(d => d.UserId, o => o.MapFrom(s => s.User != null ? s.User.Id : (Guid?)null))
            .ForMember(d => d.Username, o => o.MapFrom(s => s.User != null ? s.User.Username : null))
            .ForMember(d => d.PermissionCount, o => o.MapFrom(s => s.EmployeePermissions.Count(p => p.IsActive)))
            .ForMember(d => d.AssignedTankCount, o => o.MapFrom(s => s.AssignedTanks.Count))
            .ForMember(d => d.ResponsibleEngineCount, o => o.MapFrom(s => s.ResponsibleEngines.Count));

        CreateMap<CreateEmployeeDto, Employee>();
        CreateMap<UpdateEmployeeDto, Employee>();
    }
}
