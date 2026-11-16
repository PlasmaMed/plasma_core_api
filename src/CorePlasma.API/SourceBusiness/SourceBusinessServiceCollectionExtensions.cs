namespace CorePlasma.API.SourceBusiness;

public static class SourceBusinessServiceCollectionExtensions
{
    public static IServiceCollection AddSourceBusinessServices(this IServiceCollection services)
    {
        services.AddScoped<ISourceBusinessProjector, SourceBusinessProjector>();
        return services;
    }
}
