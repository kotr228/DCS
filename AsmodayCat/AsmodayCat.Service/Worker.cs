using AsmodayCat.Core.Hardware;

namespace AsmodayCat.Service;

public class Worker : BackgroundService
{
    private readonly ResourceManager  _resources;
    private readonly MetricsCollector _metrics;
    private readonly ILogger<Worker>  _logger;

    private static readonly TimeSpan TickInterval      = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(30);

    private int _tickCount;

    public Worker(ResourceManager resources, MetricsCollector metrics, ILogger<Worker> logger)
    {
        _resources = resources;
        _metrics   = metrics;
        _logger    = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("AsmodayCat Service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            _metrics.Tick();

            if (++_tickCount % 30 == 0)
            {
                var status = _resources.GetCurrentStatus();
                _logger.LogInformation(
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
