using AutoMapper;
using CorePlasma.BAL.DTOs.Permissions;
using CorePlasma.DAL.Entities;

namespace CorePlasma.BAL.Mappings;

public sealed class PermissionProfile : Profile
{
    public PermissionProfile()
    {
        CreateMap<Permission, PermissionDto>();

        CreateMap<Permission, PermissionMatrixColumnDto>()
            .ForMember(d => d.PermissionId, o => o.MapFrom(s => s.Id));

        CreateMap<EmployeePermission, PermissionEmployeeDto>()
            .ForMember(d => d.EmployeeCode, o => o.MapFrom(s => s.Employee.EmployeeCode))
            .ForMember(d => d.FullName, o => o.MapFrom(s => s.Employee.FullName))
            .ForMember(d => d.Department, o => o.MapFrom(s => s.Employee.Department))
            .ForMember(d => d.Position, o => o.MapFrom(s => s.Employee.Position));
    }
}
