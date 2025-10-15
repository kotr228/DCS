using GrayCat.Service.Generation;
using GrayCat.Service.Services;
using GrayCat.Service.Validation;
using Serilog;

// === 0. Configure Logging (Serilog) ===
// Set up the path for log files in %AppData%\CatSuite\Logs\Generator\
var logPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
    "CatSuite", "Logs", "Generator", "log-.txt");

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File(logPath, rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7) // Keep logs for 7 days
    .CreateLogger();

try
{
    Log.Information("--- GrayCat Service is starting up ---");

    var builder = WebApplication.CreateBuilder(args);

    // Integrate Serilog with the .NET logging system
    builder.Host.UseSerilog();

    // === 1. Configure Services ===
    builder.Host.UseWindowsService(options =>
    {
        options.ServiceName = "GrayCat Generation Service";
    });

    // Register our custom services for dependency injection
    builder.Services.AddSingleton<ProjectValidator>(); // Add the validator
    builder.Services.AddSingleton<TemplateLoader>();
    builder.Services.AddSingleton<CodeGenerator>();
    builder.Services.AddSingleton<ProjectService>();

    builder.Services.AddControllers();

    // === 2. Build the Application ===
    var app = builder.Build();

    // === 3. Configure Routing ===
    app.MapControllers();

    // === 4. Run the Application ===
    app.Run("http://localhost:5123");
}
catch (Exception ex)
{
    Log.Fatal(ex, "--- Service failed to start ---");
}
finally
{
    Log.Information("--- GrayCat Service is shutting down ---");
    Log.CloseAndFlush();
}
