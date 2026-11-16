using CorePlasma.Common.Constants;
using Serilog;

namespace CorePlasma.API.Extensions;

public static class LoggingExtensions
{
    public static WebApplicationBuilder AddSerilogLogging(this WebApplicationBuilder builder)
    {
        builder.Services.AddSerilog((services, configuration) => configuration
            .ReadFrom.Configuration(builder.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext());

        return builder;
    }

    /// <summary>
    /// One log line per request. Only path/method/status/elapsed are logged — never headers
    /// (Authorization), query strings or bodies.
    /// </summary>
    public static WebApplication UseRequestLogging(this WebApplication app)
    {
        app.UseSerilogRequestLogging(options =>
        {
            options.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";
            options.EnrichDiagnosticContext = (diagnostics, httpContext) =>
            {
                diagnostics.Set("ClientIp", httpContext.Connection.RemoteIpAddress?.ToString());
                diagnostics.Set("UserId", httpContext.User.FindFirst(AppClaimTypes.UserId)?.Value);
                diagnostics.Set("UserType", httpContext.User.FindFirst(AppClaimTypes.UserType)?.Value);
            };
        });

        return app;
    }
}
