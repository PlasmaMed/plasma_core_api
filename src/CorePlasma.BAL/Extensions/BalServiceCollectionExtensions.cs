using CorePlasma.BAL.Interfaces;
using CorePlasma.BAL.Mappings;
using CorePlasma.BAL.Services;
using CorePlasma.BAL.Services.Common;
using CorePlasma.Common.Models;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace CorePlasma.BAL.Extensions;

public static class BalServiceCollectionExtensions
{
    /// <summary>
    /// Registers business services, AutoMapper profiles, validators and the cache abstraction.
    /// AutoMapper reads its license key from the AUTOMAPPER_LICENSE_KEY environment variable.
    /// </summary>
    public static IServiceCollection AddBusinessServices(this IServiceCollection services)
    {
        var assembly = typeof(CustomerProfile).Assembly;

        services.AddAutoMapper(_ => { }, assembly);
        services.AddValidatorsFromAssembly(assembly);

        services.AddMemoryCache();
        services.AddOptions<PermissionCacheSettings>();
        services.AddOptions<LoginSecuritySettings>();
        services.AddOptions<LegacySyncSettings>();
        services.AddSingleton<ICacheService, MemoryCacheService>();

        // Shared building blocks
        services.AddScoped<IDocumentNumberService, DocumentNumberService>();
        services.AddScoped<IDocumentSequenceService, DocumentSequenceService>();
        services.AddScoped<IDataChangeLogService, DataChangeLogService>();
        services.AddScoped<IEquipmentImportService, EquipmentImportService>();
        services.AddScoped<ISessionService, SessionService>();
        services.AddScoped<IHistoryIngestService, HistoryIngestService>();
        services.AddScoped<ILegacyEventIngestService, LegacyEventIngestService>();
        services.AddScoped<IOrderService, OrderService>();

        services.AddScoped<ICustomerService, CustomerService>();
        services.AddScoped<IEmployeeService, EmployeeService>();
        services.AddScoped<ITankService, TankService>();
        services.AddScoped<IEngineAccessScope, EngineAccessScope>();
        services.AddScoped<IEngineService, EngineService>();
        services.AddScoped<ITankTransferService, TankTransferService>();
        services.AddScoped<IPermissionService, PermissionService>();
        services.AddScoped<IPermissionAssignmentService, PermissionAssignmentService>();
        services.AddScoped<IEmployeePermissionService, EmployeePermissionService>();
        services.AddScoped<IPermissionMatrixService, PermissionMatrixService>();
        services.AddScoped<IEmployeeUserService, EmployeeUserService>();
        services.AddScoped<ICustomerUserService, CustomerUserService>();
        services.AddScoped<ICustomerPortalService, CustomerPortalService>();
        services.AddSingleton<UserAccountGuard>();
        services.AddScoped<IAuthService, AuthService>();

        return services;
    }
}
