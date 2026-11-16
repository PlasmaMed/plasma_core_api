using Microsoft.Extensions.Options;

namespace CorePlasma.API.SourceSync;

public static class SourceSyncServiceCollectionExtensions
{
    public static IServiceCollection AddSourceSyncServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<SourceSyncOptions>().Bind(configuration.GetSection("SourceSync"))
            .Validate(settings => !settings.Enabled || !string.IsNullOrWhiteSpace(settings.ConnectionString)
                && settings.Sources.Count is > 0 and <= 2 && settings.Sources.All(pair =>
                    pair.Key is "quanly" or "newapp" && pair.Value is not null && pair.Value.InstallationId != Guid.Empty
                    && pair.Value.Key is { Length: >= 32 and <= 512 } key
                    && key.All(character => character is >= '!' and <= '~' && character != ',')),
                "Enabled SourceSync requires a dedicated SQL connection and valid known source bindings/keys.")
            .ValidateOnStart();
        services.AddScoped<SourceSyncStore>();
        services.AddScoped<ISourceSyncRawReader>(provider => provider.GetRequiredService<SourceSyncStore>());
        services.AddHostedService<SourceSyncFinalizer>();
        return services;
    }
}
