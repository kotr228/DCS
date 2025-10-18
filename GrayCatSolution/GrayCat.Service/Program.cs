using GrayCat.Core.Constants;
using GrayCat.Core.Interfaces;
using GrayCat.Service;
using GrayCat.Service.Services;
using Microsoft.AspNetCore.Builder;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File(
        Path.Combine(AppConstants.Paths.Logs, "service-.txt"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 30)
    .CreateLogger();

try
{
    Log.Information("=== GrayCat Service Starting ===");

    var builder = WebApplication.CreateBuilder(args);

    // Add Serilog
    builder.Host.UseSerilog();

    // Configure Windows Service
    builder.Host.UseWindowsService(options =>
    {
        options.ServiceName = AppConstants.ServiceName;
    });

    // Register services
    builder.Services.AddSingleton<ITemplateEngine, TemplateEngine>();
    builder.Services.AddSingleton<ICodeGenerator, CodeGenerator>();
    builder.Services.AddSingleton<IProjectValidator, ValidationService>();
    builder.Services.AddSingleton<IGenerationService, ProjectGenerationService>();

    // Add Worker
    builder.Services.AddHostedService<Worker>();

    // Add Web API
    builder.Services.AddControllers();

    var app = builder.Build();

    app.MapControllers();

    app.Urls.Add("http://localhost:5123");

    Log.Information("Service listening on http://localhost:5123");

    await app.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Service failed to start");
}
finally
{
    Log.Information("=== GrayCat Service Stopped ===");
    Log.CloseAndFlush();
}