using GrayCat.Core.Constants;
using GrayCat.Core.Interfaces;
using GrayCat.Core.Serialization;
using GrayCat.Service;
using GrayCat.Service.Services;
using Microsoft.AspNetCore.Connections;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
    // Startup failures are reported once, in plain words, by the catch blocks below.
    .MinimumLevel.Override("Microsoft.Extensions.Hosting.Internal.Host", Serilog.Events.LogEventLevel.Fatal)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File(
        Path.Combine(AppConstants.Paths.Logs, "service-.txt"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 30)
    .CreateLogger();

var baseUrl = AppConstants.ServiceEndpoints.BaseUrl;

try
{
    Log.Information("=== GrayCat Service Starting ===");

    // A second copy cannot bind the port; tell the user clearly instead of crashing.
    if (await ServiceProbe.IsGrayCatServiceRunningAsync(baseUrl))
    {
        Log.Warning("GrayCat Service is already running on {Url}. This copy will exit.", baseUrl);
        return 0;
    }

    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog();
    builder.Host.UseWindowsService(options => options.ServiceName = AppConstants.ServiceName);

    builder.Services.AddSingleton<IProjectValidator, ValidationService>();
    builder.Services.AddSingleton<IGenerationService, ProjectGenerationService>();
    builder.Services.AddHostedService<Worker>();

    builder.Services
        .AddControllers()
        .AddJsonOptions(options => GrayCatJson.ApplyTo(options.JsonSerializerOptions));

    // Loopback only: the service writes files, so it must not be reachable from the network.
    builder.WebHost.UseUrls(baseUrl);

    var app = builder.Build();
    app.MapControllers();

    app.Lifetime.ApplicationStarted.Register(() => Log.Information("Service listening on {Url}", baseUrl));
    await app.RunAsync();
    return 0;
}
catch (IOException ex) when (ex.InnerException is AddressInUseException)
{
    Log.Fatal("Port {Port} is already used by another program, so GrayCat Service cannot start. " +
              "Close that program or free the port, then start the service again.", new Uri(baseUrl).Port);
    return 1;
}
catch (Exception ex)
{
    Log.Fatal(ex, "Service failed to start");
    return 1;
}
finally
{
    Log.Information("=== GrayCat Service Stopped ===");
    Log.CloseAndFlush();
}
