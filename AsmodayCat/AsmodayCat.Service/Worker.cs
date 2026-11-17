using AsmodayCat.Core.Hardware;
using AsmodayCat.Shared.Interfaces;

namespace AsmodayCat.Service;

public class Worker : BackgroundService
{
    private readonly ResourceManager  _resources;
    private readonly MetricsCollector _metrics;
    private readonly IModelRegistry   _models;
    private readonly AsmodayCat.Core.Engine.OllamaLauncher _launcher;
    private readonly ILogger<Worker>  _logger;

    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(1);

    private int _tickCount;

    public Worker(ResourceManager resources, MetricsCollector metrics, IModelRegistry models,
                  AsmodayCat.Core.Engine.OllamaLauncher launcher, ILogger<Worker> logger)
    {
        _launcher  = launcher;
        _resources = resources;
        _metrics   = metrics;
        _models    = models;
        _logger    = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("AsmodayCat Service started");

        // Bring up the local LLM engine right away so the first chat doesn't fail
        await _launcher.EnsureRunningAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            _resources.Sample();
            if (_tickCount % 2 == 0) _resources.Gpu.Refresh();    // nvidia-smi every 2 s
            _metrics.Tick();
            _tickCount++;

            // Which model Ollama actually has in memory (it unloads idle models by itself)
            if (_tickCount % 5 == 0 && _metrics.ActiveModelStatus != "Generating")
            {
                var loaded = await _models.GetLoadedModelsAsync(stoppingToken);
                _metrics.ActiveModelName   = string.Join(", ", loaded);
                _metrics.ActiveModelStatus = loaded.Count > 0 ? "Loaded" : "Idle";
            }

            if (_tickCount % 300 == 0)
            {
                // Debug level: goes to the log file only, not the Terminal tab
                var status = _resources.GetCurrentStatus();
                _logger.LogDebug(
                    "Heartbeat | CPU: {Cpu:F1}% | RAM free: {Ram} MB | VRAM used: {Vram} MB",
                    status.CpuLoad,
                    status.RamFree / 1024 / 1024,
                    _resources.GetTotalVramUsedMb());
            }

            await Task.Delay(TickInterval, stoppingToken);
        }

        _logger.LogInformation("AsmodayCat Service stopped");
    }
}
