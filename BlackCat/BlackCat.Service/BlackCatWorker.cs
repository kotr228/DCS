using BlackCat.Core;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace BlackCat.Service;

/// <summary>
/// BlackCat Firewall Windows Service Worker
/// </summary>
public class BlackCatWorker : BackgroundService
{
    private readonly IConfiguration _configuration;
    private FirewallCoordinator? _coordinator;

    public BlackCatWorker(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            Log.Information("BlackCat Firewall Service запускається...");

            // Читання конфігурації
            string masterSecret = _configuration["BlackCat:MasterSecret"]
                ?? throw new InvalidOperationException("MasterSecret не налаштовано в конфігурації");

            string databasePath = _configuration["BlackCat:DatabasePath"] ?? "blackcat.db";
            int tunnelPort = int.Parse(_configuration["BlackCat:TunnelPort"] ?? "9999");

            // Створити координатор
            _coordinator = new FirewallCoordinator(masterSecret, databasePath, tunnelPort);

            // Підписатися на події
            _coordinator.LogMessage += OnLogMessage;
            _coordinator.StatisticsUpdated += OnStatisticsUpdated;

            // Запустити брандмауер
            await _coordinator.StartAsync(stoppingToken);

            Log.Information("BlackCat Firewall Service запущено успішно");

            // Тримати сервіс запущеним
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            Log.Information("BlackCat Firewall Service зупиняється...");
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Критична помилка в BlackCat Firewall Service");
            throw;
        }
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        Log.Information("Зупинка BlackCat Firewall Service...");
        _coordinator?.Stop();
        _coordinator?.Dispose();
        return base.StopAsync(cancellationToken);
    }

    private void OnLogMessage(object? sender, string message)
    {
        Log.Information(message);
    }

    private void OnStatisticsUpdated(object? sender, BlackCat.Shared.Models.FirewallStatistics stats)
    {
        // Періодично логувати статистику
        if (stats.TotalPackets % 1000 == 0)
        {
            Log.Information(
                "Статистика: Total={Total}, Allowed={Allowed}, Blocked={Blocked}, Tunneled={Tunneled}, Errors={Errors}",
                stats.TotalPackets,
                stats.AllowedPackets,
                stats.BlockedPackets,
                stats.TunneledPackets,
                stats.ErrorPackets
            );
        }
    }

    public override void Dispose()
    {
        _coordinator?.Dispose();
        base.Dispose();
    }
}
