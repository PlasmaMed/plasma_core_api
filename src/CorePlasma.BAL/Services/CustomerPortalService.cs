using CorePlasma.BAL.DTOs.Tanks;
using CorePlasma.BAL.Interfaces;
using CorePlasma.Common.Enums;
using CorePlasma.Common.Exceptions;
using CorePlasma.Common.Interfaces;
using CorePlasma.Common.Responses;
using CorePlasma.DAL.Entities;
using CorePlasma.DAL.Interfaces;

namespace CorePlasma.BAL.Services;

public sealed class CustomerPortalService(
    IUnitOfWork unitOfWork,
    ITankService tankService,
    ICurrentUserService currentUser) : ICustomerPortalService
{
    public async Task<PagedResult<TankDto>> GetMyTanksAsync(TankFilterDto filter, CancellationToken cancellationToken)
    {
        var customerId = await GetActiveCustomerIdAsync(cancellationToken);

        // Scope is forced server-side; client-supplied customer/employee filters are ignored.
        filter.CustomerId = customerId;
        filter.AssignedEmployeeId = null;

        return await tankService.GetPagedAsync(filter, cancellationToken);
    }

    public async Task<TankDetailDto> GetMyTankAsync(Guid id, CancellationToken cancellationToken)
    {
        var customerId = await GetActiveCustomerIdAsync(cancellationToken);

        var tank = await tankService.GetByIdAsync(id, cancellationToken);
        if (tank.CustomerId != customerId)
        {
            // Same response as a missing tank: do not reveal other customers' data.
            throw new NotFoundException(nameof(Tank), id);
        }

        return tank;
    }

    /// <summary>
    /// The account and its customer must still be active (a lock takes effect before the JWT expires).
    /// </summary>
    private async Task<Guid> GetActiveCustomerIdAsync(CancellationToken cancellationToken)
    {
        if (currentUser.UserType != UserType.Customer || currentUser.UserId is not { } userId || currentUser.CustomerId is not { } customerId)
        {
            throw new ForbiddenException("This endpoint is only available to customer accounts.");
        }

        var active = await unitOfWork.CustomerUsers.ExistsAsync(
            x => x.Id == userId
                 && x.CustomerId == customerId
                 && x.Status == UserStatus.Active
                 && x.Customer.Status == CustomerStatus.Active,
            cancellationToken);

        if (!active)
        {
            throw new UnauthorizedAccessException("The account is no longer active.");
        }

        return customerId;
    }
}
