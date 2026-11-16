using CorePlasma.BAL.DTOs.Permissions;

namespace CorePlasma.BAL.Interfaces;

public interface IPermissionMatrixService
{
    Task<PermissionMatrixDto> GetAsync(PermissionMatrixFilterDto filter, CancellationToken cancellationToken);

    Task<PermissionChangeResultDto> UpdateAsync(UpdatePermissionMatrixDto dto, CancellationToken cancellationToken);
}
