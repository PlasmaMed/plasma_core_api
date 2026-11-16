using CorePlasma.BAL.DTOs.Customers;

namespace CorePlasma.BAL.Interfaces;

public interface ICustomerService
    : ICrudService<CustomerDto, CustomerDetailDto, CreateCustomerDto, UpdateCustomerDto, CustomerFilterDto>;
