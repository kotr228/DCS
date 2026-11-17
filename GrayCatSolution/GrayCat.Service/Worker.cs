using GrayCat.Core.Constants;

namespace GrayCat.Service;

/// <summary>Prepares the app data folders and keeps the service alive.</summary>
public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;

    public Worker(ILogger<Worker> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("GrayCat Worker Service is running");

        Directory.CreateDirectory(AppConstants.Paths.AppData);
        Directory.CreateDirectory(AppConstants.Paths.Logs);
        Directory.CreateDirectory(AppConstants.Paths.Projects);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
    }
}
