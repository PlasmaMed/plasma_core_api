using CorePlasma.Common.Models;
using CorePlasma.DAL.Context;
using CorePlasma.DAL.Interceptors;
using CorePlasma.DAL.Interfaces;
using CorePlasma.DAL.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CorePlasma.DAL.Extensions;

public static class DalServiceCollectionExtensions
{
    public const string ConnectionStringName = "DefaultConnection";

    public static IServiceCollection AddDalServices(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException($"Connection string '{ConnectionStringName}' is not configured.");
        }

        var provider = configuration["Database:Provider"] ?? "PostgreSql";
        if (provider.Equals("PostgreSql", StringComparison.OrdinalIgnoreCase))
        {
            // The app stores UTC values but not every DateTime carries Kind=Utc.
            AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
        }
        else if (!provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Unsupported database provider '{provider}'.");
        }

        services.AddScoped<AuditableEntityInterceptor>();
        services.AddScoped<DataChangeLogInterceptor>();

        services.AddDbContext<ApplicationDbContext>((sp, options) =>
        {
            if (provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
            {
                options.UseSqlServer(connectionString);
            }
            else
            {
                options.UseNpgsql(connectionString, npgsql =>
                    npgsql.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName));
            }
            // Order matters: soft deletes are converted before the change log reads the entries.
            options.AddInterceptors(
                sp.GetRequiredService<AuditableEntityInterceptor>(),
                sp.GetRequiredService<DataChangeLogInterceptor>());
        });

        services.AddScoped<IUnitOfWork, UnitOfWork.UnitOfWork>();

        services.Configure<SuperAdminSettings>(configuration.GetSection(SuperAdminSettings.SectionName));
        services.AddScoped<DatabaseSeeder>();

        return services;
    }
}
