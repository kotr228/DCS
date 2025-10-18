using GrayCat.Core.Constants;
using GrayCat.Core.Interfaces;

namespace GrayCat.Service;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IGenerationService _generationService;

    public Worker(ILogger<Worker> logger, IGenerationService generationService)
    {
        _logger = logger;
        _generationService = generationService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("GrayCat Worker Service is running");

        // Ensure required directories exist
        Directory.CreateDirectory(AppConstants.Paths.AppData);
        Directory.CreateDirectory(AppConstants.Paths.Logs);
        Directory.CreateDirectory(AppConstants.Paths.Projects);
        Directory.CreateDirectory(AppConstants.Paths.Templates);

        while (!stoppingToken.IsCancellationRequested)
        {
            // Background maintenance tasks can go here
            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
        }
    }
}