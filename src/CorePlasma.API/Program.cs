using CorePlasma.API.Extensions;
using CorePlasma.BAL.Extensions;
using CorePlasma.DAL.Extensions;
using Serilog;

Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.AddSerilogLogging();
    builder.Services.AddDalServices(builder.Configuration);
    builder.Services.AddBusinessServices();
    builder.Services.AddApplicationServices(builder.Configuration);

    var app = builder.Build();

    app.UseApplicationPipeline();
    await app.Services.InitializeDatabaseAsync(app.Configuration);

    await app.RunAsync();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
    Environment.ExitCode = 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}
