using CorePlasma.BAL.DTOs.Auth;

namespace CorePlasma.BAL.Interfaces;

public interface IAuthService
{
    /// <summary>Sign in with an EmployeeUsers account.</summary>
    Task<LoginResponseDto> EmployeeLoginAsync(LoginRequestDto dto, CancellationToken cancellationToken);

    /// <summary>Sign in with a CustomerUsers account.</summary>
    Task<LoginResponseDto> CustomerLoginAsync(LoginRequestDto dto, CancellationToken cancellationToken);

    Task<CurrentUserDto> GetCurrentUserAsync(CancellationToken cancellationToken);

    Task ChangePasswordAsync(ChangePasswordDto dto, CancellationToken cancellationToken);
}
