using AutoMapper;
using CorePlasma.BAL.DTOs.Customers;
using CorePlasma.BAL.Interfaces;
using CorePlasma.BAL.Services.Common;
using CorePlasma.Common.Constants;
using CorePlasma.Common.Exceptions;
using CorePlasma.Common.Extensions;
using CorePlasma.DAL.Entities;
using CorePlasma.DAL.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CorePlasma.BAL.Services;

public sealed class CustomerService(IUnitOfWork unitOfWork, IMapper mapper, IDocumentNumberService documentNumberService)
    : CrudServiceBase<Customer, CustomerDto, CustomerDetailDto, CreateCustomerDto, UpdateCustomerDto, CustomerFilterDto>(
        unitOfWork, mapper, documentNumberService),
      ICustomerService
{
    protected override IRepository<Customer> Repository => UnitOfWork.Customers;

    protected override IReadOnlyCollection<string> SortableFields =>
    [
        nameof(Customer.CustomerCode), nameof(Customer.CustomerName), nameof(Customer.ShortName),
        nameof(Customer.TaxCode), nameof(Customer.Status), nameof(Customer.CreatedDate), nameof(Customer.UpdatedDate)
    ];

    protected override AutoCodeOptions<Customer> AutoCode => new(SequenceCodes.Customer, x => x.CustomerCode);

    protected override IQueryable<Customer> ApplyFilter(IQueryable<Customer> query, CustomerFilterDto filter)
    {
        var keyword = filter.Keyword.TrimToNull();
        var taxCode = filter.TaxCode.TrimToNull();

        return query
            .WhereContainsAny(UnitOfWork.IsSqlServer, keyword,
                x => x.CustomerCode, x => x.CustomerName, x => x.ShortName,
                x => x.Phone, x => x.Email, x => x.ContactPerson)
            .WhereIf(taxCode is not null, x => x.TaxCode == taxCode)
            .WhereIf(filter.Status.HasValue, x => x.Status == filter.Status);
    }

    protected override Task BeforeUpdateAsync(Customer entity, UpdateCustomerDto dto, CancellationToken cancellationToken) =>
        EnsureUniqueAsync(x => x.CustomerCode == dto.CustomerCode, entity.Id, "CustomerCode already exists.", cancellationToken);

    protected override async Task BeforeDeleteAsync(Customer entity, CancellationToken cancellationToken)
    {
        if (await UnitOfWork.Tanks.ExistsAsync(t => t.CustomerId == entity.Id, cancellationToken))
        {
            throw new BusinessException("Cannot delete a customer that still has tanks assigned.");
        }

        // Login accounts of the customer go with it (soft delete, same SaveChanges).
        var accounts = await UnitOfWork.CustomerUsers.Query()
            .Where(x => x.CustomerId == entity.Id)
            .ToListAsync(cancellationToken);
        UnitOfWork.CustomerUsers.DeleteRange(accounts);
    }
}
