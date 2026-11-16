using CorePlasma.BAL.DTOs.Auth;
using CorePlasma.BAL.Interfaces;
using CorePlasma.Common.Enums;
using CorePlasma.Common.Exceptions;
using CorePlasma.Common.Helpers;
using CorePlasma.Common.Interfaces;
using CorePlasma.DAL.Entities;
using CorePlasma.DAL.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CorePlasma.BAL.Services;

/// <summary>
/// Sign-in for both account tables. Employee and customer accounts use separate endpoints, so the same
/// username may exist in each table without ambiguity.
/// </summary>
public sealed class AuthService(
    IUnitOfWork unitOfWork,
    IPermissionService permissionService,
    IJwtTokenGenerator tokenGenerator,
    ICurrentUserService currentUser,
    UserAccountGuard accountGuard,
    ILogger<AuthService> logger) : IAuthService
{
    private const string InvalidCredentials = "Invalid username or password.";

    public async Task<LoginResponseDto> EmployeeLoginAsync(LoginRequestDto dto, CancellationToken cancellationToken)
    {
        var account = await unitOfWork.EmployeeUsers.Query()
            .Include(x => x.Employee)
            .FirstOrDefaultAsync(x => x.Username == dto.Username, cancellationToken);

        if (account is not null && account.Employee.Status != EmployeeStatus.Active)
        {
            logger.LogWarning("Login rejected for {Username}: employee is not active", dto.Username);
            throw new UnauthorizedAccessException(InvalidCredentials);
        }

        await SignInAsync(account, dto, UserType.Employee, cancellationToken);

        var token = tokenGenerator.Generate(new TokenSubject(
            account!.Id, UserType.Employee, account.Username, account.Employee.FullName, account.EmployeeId, null), dto.RememberMe);

        return new LoginResponseDto
        {
            AccessToken = token.Token,
            ExpiresAt = token.ExpiresAt,
            User = await BuildEmployeeUserAsync(account.Id, cancellationToken)
        };
    }

    public async Task<LoginResponseDto> CustomerLoginAsync(LoginRequestDto dto, CancellationToken cancellationToken)
    {
        var account = await unitOfWork.CustomerUsers.Query()
            .Include(x => x.Customer)
            .FirstOrDefaultAsync(x => x.Username == dto.Username, cancellationToken);

        if (account is not null && account.Customer.Status != CustomerStatus.Active)
        {
            logger.LogWarning("Login rejected for customer account {Username}: customer is not active", dto.Username);
            throw new UnauthorizedAccessException(InvalidCredentials);
        }

        await SignInAsync(account, dto, UserType.Customer, cancellationToken);

        var token = tokenGenerator.Generate(new TokenSubject(
            account!.Id, UserType.Customer, account.Username, account.FullName, null, account.CustomerId), dto.RememberMe);

        return new LoginResponseDto
        {
            AccessToken = token.Token,
            ExpiresAt = token.ExpiresAt,
            User = await BuildCustomerUserAsync(account.Id, cancellationToken)
        };
    }

    public Task<CurrentUserDto> GetCurrentUserAsync(CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        return currentUser.UserType switch
        {
            UserType.Employee => BuildEmployeeUserAsync(userId, cancellationToken),
            UserType.Customer => BuildCustomerUserAsync(userId, cancellationToken),
            _ => throw new UnauthorizedAccessException()
        };
    }

    public async Task ChangePasswordAsync(ChangePasswordDto dto, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        IUserAccount? account = currentUser.UserType switch
        {
            UserType.Employee => await unitOfWork.EmployeeUsers.GetByIdAsync(userId, cancellationToken),
            UserType.Customer => await unitOfWork.CustomerUsers.GetByIdAsync(userId, cancellationToken),
            _ => null
        };

        if (account is null)
        {
            throw new UnauthorizedAccessException();
        }

        if (!PasswordHasher.Verify(dto.CurrentPassword, account.PasswordHash))
        {
            throw new BusinessException("Current password is incorrect.");
        }

        UserAccountGuard.SetPassword(account, dto.NewPassword);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task SignInAsync(IUserAccount? account, LoginRequestDto dto, UserType userType, CancellationToken cancellationToken)
    {
        if (account is null)
        {
            logger.LogWarning("Failed {UserType} login: unknown username {Username}", userType, dto.Username);
            throw new UnauthorizedAccessException(InvalidCredentials);
        }

        var result = accountGuard.CheckPassword(account, dto.Password);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        switch (result)
        {
            case SignInResult.Success:
                logger.LogInformation("{UserType} account {Username} signed in", userType, account.Username);
                return;
            case SignInResult.LockedOut:
                logger.LogWarning("{UserType} account {Username} is temporarily locked out", userType, account.Username);
                throw new UnauthorizedAccessException("Too many failed attempts. The account is temporarily locked.");
            case SignInResult.NotActive:
                logger.LogWarning("{UserType} account {Username} is not active", userType, account.Username);
                throw new UnauthorizedAccessException("The account is locked or inactive.");
            default:
                logger.LogWarning("Failed {UserType} login for {Username}", userType, account.Username);
                throw new UnauthorizedAccessException(InvalidCredentials);
        }
    }

    private async Task<CurrentUserDto> BuildEmployeeUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await unitOfWork.EmployeeUsers.QueryNoTracking()
                       .Where(x => x.Id == userId
                                   && x.Status == UserStatus.Active
                                   && x.Employee.Status == EmployeeStatus.Active)
                       .Select(x => new CurrentUserDto
                       {
                           UserId = x.Id,
                           UserType = UserType.Employee,
                           Username = x.Username,
                           FullName = x.Employee.FullName,
                           EmployeeId = x.EmployeeId,
                           EmployeeCode = x.Employee.EmployeeCode,
                           IsSuperAdmin = x.IsSuperAdmin
                       })
                       .FirstOrDefaultAsync(cancellationToken)
                   ?? throw new UnauthorizedAccessException("The account is no longer active.");

        var permissions = await permissionService.GetPermissionsAsync(user.EmployeeId!.Value, cancellationToken);
        user.Permissions = permissions.OrderBy(x => x).ToList();
        return user;
    }

    private async Task<CurrentUserDto> BuildCustomerUserAsync(Guid userId, CancellationToken cancellationToken) =>
        await unitOfWork.CustomerUsers.QueryNoTracking()
            .Where(x => x.Id == userId
                        && x.Status == UserStatus.Active
                        && x.Customer.Status == CustomerStatus.Active)
            .Select(x => new CurrentUserDto
            {
                UserId = x.Id,
                UserType = UserType.Customer,
                Username = x.Username,
                FullName = x.FullName,
                CustomerId = x.CustomerId,
                CustomerCode = x.Customer.CustomerCode,
                CustomerName = x.Customer.CustomerName
            })
            .FirstOrDefaultAsync(cancellationToken)
        ?? throw new UnauthorizedAccessException("The account is no longer active.");
}
