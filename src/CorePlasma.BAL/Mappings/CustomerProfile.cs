using AutoMapper;
using CorePlasma.BAL.DTOs.Customers;
using CorePlasma.DAL.Entities;

namespace CorePlasma.BAL.Mappings;

public sealed class CustomerProfile : Profile
{
    public CustomerProfile()
    {
        CreateMap<Customer, CustomerDto>();
        CreateMap<Customer, CustomerDetailDto>()
            .ForMember(d => d.TankCount, o => o.MapFrom(s => s.Tanks.Count));

        CreateMap<CreateCustomerDto, Customer>();
        CreateMap<UpdateCustomerDto, Customer>();
    }
}
