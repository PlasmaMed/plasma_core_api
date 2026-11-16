using CorePlasma.API.SourceSync;
using Microsoft.Extensions.Options;

namespace CorePlasma.API.QuanlyAccounts;

public sealed class QuanlyAccountImportOptions
{
    public bool Enabled { get; set; }
    public int PollSeconds { get; set; } = 60;
    public int MaxUsersPerPoll { get; set; } = 200;
    public int MaxSnapshotRows { get; set; } = 100_000;
}

public sealed record QuanlyAccountImportOutcome(long SourceUserId, string State, string? Reason, Guid? AccountId);
public sealed record QuanlyAccountImportScanResult(long NextSourceUserId, int Processed);

public static class QuanlyAccountImportRegistration
{
    public static IServiceCollection AddQuanlyAccountImportServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<QuanlyAccountImportOptions>(configuration.GetSection("QuanlyAccountImport"));
        services.AddScoped<QuanlyAccountImporter>();
        services.AddHostedService<QuanlyAccountImportWorker>();
        return services;
    }
}

/// <summary>Internal, opt-in import. Never exposes source credential material through an endpoint or log.</summary>
public sealed class QuanlyAccountImportWorker(IServiceScopeFactory scopes,
    IOptionsMonitor<QuanlyAccountImportOptions> options, ILogger<QuanlyAccountImportWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        long cursor = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            if (options.CurrentValue.Enabled)
            {
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    var result = await scope.ServiceProvider.GetRequiredService<QuanlyAccountImporter>().RunOnceAsync(cursor, stoppingToken);
                    cursor = result.NextSourceUserId;
                    if (result.Processed > 0) logger.LogInformation("Quanly account import checked {Count} source accounts", result.Processed);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                // Exception messages can include provider values; record only a fixed code/type.
                catch (Exception error) { logger.LogError("Quanly account import paused ({ErrorType})", error.GetType().Name); }
            }
            else cursor = 0;
            try { await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(options.CurrentValue.PollSeconds, 5, 3600)), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
