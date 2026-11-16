using System.Security.Claims;
using System.Text;
using CorePlasma.BAL.DTOs.Auth;
using CorePlasma.BAL.Interfaces;
using CorePlasma.Common.Constants;
using CorePlasma.Common.Models;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace CorePlasma.API.Services;

/// <summary>
/// Issues identity-only tokens. Permissions are deliberately NOT embedded; they are resolved per request
/// through the permission cache so that grants/revokes apply immediately.
/// </summary>
public sealed class JwtTokenGenerator(IOptions<JwtSettings> options) : IJwtTokenGenerator
{
    private readonly JsonWebTokenHandler _handler = new();

    public AccessToken Generate(TokenSubject subject, bool rememberMe = false)
    {
        var settings = options.Value;
        var expiresAt = rememberMe
            ? DateTime.UtcNow.AddDays(settings.RememberMeDays)
            : DateTime.UtcNow.AddMinutes(settings.ExpirationMinutes);

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = settings.Issuer,
            Audience = settings.Audience,
            Expires = expiresAt,
            Subject = new ClaimsIdentity(BuildClaims(subject)),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.Secret)),
                SecurityAlgorithms.HmacSha256)
        };

        return new AccessToken(_handler.CreateToken(descriptor), expiresAt);
    }

    private static List<Claim> BuildClaims(TokenSubject subject)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, subject.UserId.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(AppClaimTypes.UserId, subject.UserId.ToString()),
            new(AppClaimTypes.UserType, subject.UserType.ToString()),
            new(AppClaimTypes.Username, subject.Username),
            new(AppClaimTypes.FullName, subject.FullName)
        };

        if (subject.EmployeeId is { } employeeId)
        {
            claims.Add(new Claim(AppClaimTypes.EmployeeId, employeeId.ToString()));
        }

        if (subject.CustomerId is { } customerId)
        {
            claims.Add(new Claim(AppClaimTypes.CustomerId, customerId.ToString()));
        }

        return claims;
    }
}
