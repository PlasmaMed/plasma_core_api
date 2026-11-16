using AutoMapper;
using CorePlasma.BAL.DTOs.Users;
using CorePlasma.DAL.Entities;

namespace CorePlasma.BAL.Mappings;

public sealed class UserProfile : Profile
{
    public UserProfile()
    {
        CreateMap<EmployeeUser, EmployeeUserDto>()
            .ForMember(d => d.QuanLyId, o => o.MapFrom(s => s.Employee.QuanLyId))
            .ForMember(d => d.NewAppId, o => o.MapFrom(s => s.Employee.NewAppId))
            .ForMember(d => d.EmployeeCode, o => o.MapFrom(s => s.Employee.EmployeeCode))
            .ForMember(d => d.EmployeeName, o => o.MapFrom(s => s.Employee.FullName))
            .ForMember(d => d.Department, o => o.MapFrom(s => s.Employee.Department));

        CreateMap<CustomerUser, CustomerUserDto>()
            .ForMember(d => d.CustomerCode, o => o.MapFrom(s => s.Customer.CustomerCode))
            .ForMember(d => d.CustomerName, o => o.MapFrom(s => s.Customer.CustomerName));

        // Password is hashed by the service, never mapped.
        CreateMap<CreateCustomerUserDto, CustomerUser>()
            .ForMember(d => d.PasswordHash, o => o.Ignore());
        CreateMap<UpdateCustomerUserDto, CustomerUser>();
    }
}
