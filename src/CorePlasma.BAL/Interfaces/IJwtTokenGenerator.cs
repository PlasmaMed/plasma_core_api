using CorePlasma.BAL.DTOs.Auth;

namespace CorePlasma.BAL.Interfaces;

/// <summary>
/// Issues access tokens. Implemented in the API layer, which owns JWT configuration.
/// </summary>
public interface IJwtTokenGenerator
{
    /// <param name="rememberMe">Long-lived token (Jwt:RememberMeDays) instead of Jwt:ExpirationMinutes.</param>
    AccessToken Generate(TokenSubject subject, bool rememberMe = false);
}
