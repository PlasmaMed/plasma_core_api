using CorePlasma.DAL.Context;
using CorePlasma.DAL.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CorePlasma.DAL.Extensions;

public static class DatabaseInitializerExtensions
{
    /// <summary>
    /// Optionally applies pending migrations (Database:ApplyMigrationsOnStartup) and seeds
    /// permissions + Super Admin (Database:SeedOnStartup).
    /// </summary>
    public static async Task InitializeDatabaseAsync(
        this IServiceProvider services,
        IConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        var applyMigrations = configuration.GetValue("Database:ApplyMigrationsOnStartup", false);
        var seed = configuration.GetValue("Database:SeedOnStartup", true);

        if (!applyMigrations && !seed)
        {
            return;
        }

        await using var scope = services.CreateAsyncScope();

        if (applyMigrations)
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            if (context.Database.IsSqlServer())
            {
                throw new InvalidOperationException("The existing EF migrations target PostgreSQL and cannot be applied to SQL Server.");
            }

            await context.Database.MigrateAsync(cancellationToken);
        }

        if (seed)
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            if (context.Database.IsSqlServer())
            {
                throw new InvalidOperationException("The imported SQL Server database must not be seeded on startup.");
            }

            var seeder = scope.ServiceProvider.GetRequiredService<DatabaseSeeder>();
            await seeder.SeedAsync(cancellationToken);
        }
    }
}
