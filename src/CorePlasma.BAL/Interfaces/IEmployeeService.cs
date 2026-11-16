using CorePlasma.BAL.DTOs.Employees;

namespace CorePlasma.BAL.Interfaces;

public interface IEmployeeService
    : ICrudService<EmployeeDto, EmployeeDetailDto, CreateEmployeeDto, UpdateEmployeeDto, EmployeeFilterDto>;
