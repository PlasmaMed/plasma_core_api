using CorePlasma.BAL.DTOs.Tanks;
using CorePlasma.Common.Responses;

namespace CorePlasma.BAL.Interfaces;

/// <summary>
/// Read access for customer accounts, always scoped to the caller's own Customer.
/// </summary>
public interface ICustomerPortalService
{
    Task<PagedResult<TankDto>> GetMyTanksAsync(TankFilterDto filter, CancellationToken cancellationToken);

    Task<TankDetailDto> GetMyTankAsync(Guid id, CancellationToken cancellationToken);
}
