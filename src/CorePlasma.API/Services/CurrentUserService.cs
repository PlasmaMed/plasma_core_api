using System.Security.Claims;
using CorePlasma.API.Middleware;
using CorePlasma.Common.Constants;
using CorePlasma.Common.Enums;
using CorePlasma.Common.Interfaces;

namespace CorePlasma.API.Services;

public sealed class CurrentUserService(IHttpContextAccessor httpContextAccessor) : ICurrentUserService
{
    private HttpContext? HttpContext => httpContextAccessor.HttpContext;

    private ClaimsPrincipal? User => HttpContext?.User;

    public Guid? UserId => GetGuid(AppClaimTypes.UserId);

    public UserType? UserType =>
        Enum.TryParse<UserType>(User?.FindFirstValue(AppClaimTypes.UserType), out var type) ? type : null;

    public string? Username => User?.FindFirstValue(AppClaimTypes.Username);

    public Guid? EmployeeId => UserType == Common.Enums.UserType.Employee ? GetGuid(AppClaimTypes.EmployeeId) : null;

    public Guid? CustomerId => UserType == Common.Enums.UserType.Customer ? GetGuid(AppClaimTypes.CustomerId) : null;

    public Guid? ActorId => EmployeeId ?? (UserType == Common.Enums.UserType.Customer ? UserId : null);

    public bool IsAuthenticated => User?.Identity?.IsAuthenticated == true;

    public string? IpAddress => HttpContext?.Connection.RemoteIpAddress?.ToString();

    public string? CorrelationId => HttpContext?.Items[CorrelationIdMiddleware.ItemKey] as string;

    private Guid? GetGuid(string claimType) =>
        Guid.TryParse(User?.FindFirstValue(claimType), out var id) ? id : null;
}
