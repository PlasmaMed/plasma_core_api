using CorePlasma.API.Middleware;

namespace CorePlasma.API.Extensions;

public static class ApplicationBuilderExtensions
{
    public static WebApplication UseApplicationPipeline(this WebApplication app)
    {
        app.UseMiddleware<CorrelationIdMiddleware>();
        app.UseRequestLogging();
        app.UseExceptionHandler();

        if (app.Environment.IsDevelopment() || app.Configuration.GetValue("Swagger:Enabled", false))
        {
            app.UseSwaggerDocumentation();
        }

        if (!app.Environment.IsDevelopment())
        {
            app.UseHsts();
        }

        app.UseHttpsRedirection();
        app.UseCors(ApplicationServiceCollectionExtensions.CorsPolicyName);
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();

        return app;
    }
}
