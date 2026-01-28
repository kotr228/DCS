using BlackCat.Core.Data;
using BlackCat.NetworkCore;
using BlackCat.Shared.Models;
using BlackCat.Shared.Enums;
using Serilog;

namespace BlackCat.Core;

/// <summary>
/// Головний координатор брандмауера
/// Об'єднує всі компоненти: перехоплення, фільтрацію, тунель, статистику
/// </summary>
public class FirewallCoordinator : IDisposable
{
    private readonly PacketInterceptor _interceptor;
    private readonly FilterEngine _filterEngine;
    private readonly SecureTunnelService _tunnelService;
    private readonly RuleRepository _ruleRepository;
    private readonly FirewallStatistics _statistics;

    private bool _isRunning;

    public event EventHandler<FirewallStatistics>? StatisticsUpdated;
    public event EventHandler<string>? LogMessage;

    public FirewallStatistics Statistics => _statistics;
    public FilterEngine FilterEngine => _filterEngine;

    public FirewallCoordinator(string masterSecret, string databasePath = "blackcat.db", int tunnelPort = 9999)
    {
        _interceptor = new PacketInterceptor();
        _filterEngine = new FilterEngine();
        _tunnelService = new SecureTunnelService(masterSecret, tunnelPort);
        _ruleRepository = new RuleRepository(databasePath);
        _statistics = new FirewallStatistics
        {
            ServiceStartTime = DateTime.UtcNow
        };

        // Підписка на події
        _interceptor.PacketCaptured += OnPacketCaptured;
        _interceptor.InterceptorError += OnInterceptorError;

        _filterEngine.PacketFiltered += OnPacketFiltered;

        _tunnelService.PacketReceived += OnTunnelPacketReceived;
        _tunnelService.TunnelError += OnTunnelError;
        _tunnelService.StatusChanged += OnTunnelStatusChanged;

        // Завантажити правила з БД
        LoadRules();
    }

    /// <summary>
    /// Завантажити правила з бази даних
    /// </summary>
    public void LoadRules()
    {
        var rules = _ruleRepository.GetActiveRules();
        _filterEngine.LoadRules(rules);
        Log($"Завантажено {rules.Count} правил фільтрації");
    }

    /// <summary>
    /// Запустити брандмауер
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_isRunning)
            throw new InvalidOperationException("Брандмауер вже запущено");

        try
        {
            Log("Запуск брандмауера BlackCat...");

            // Запустити тунель
            await _tunnelService.StartAsync(cancellationToken);
            Log($"Тунель запущено на порту {9999}");

            // Запустити перехоплення пакетів
            // ПРИМІТКА: Вимагає прав адміністратора
            try
            {
                _interceptor.Start();
                Log("Перехоплення пакетів активовано");
            }
            catch (Exception ex)
            {
                Log($"⚠️ Не вдалося запустити перехоплення пакетів: {ex.Message}");
                Log("⚠️ Переконайтеся, що програма запущена з правами адміністратора");
            }

            _isRunning = true;
            Log("✅ Брандмауер BlackCat запущено успішно");

            // Запустити моніторинг статистики
            _ = Task.Run(() => StatisticsMonitorAsync(cancellationToken), cancellationToken);
        }
        catch (Exception ex)
        {
            Log($"❌ Помилка запуску: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Зупинити брандмауер
    /// </summary>
    public void Stop()
    {
        if (!_isRunning) return;

        Log("Зупинка брандмауера...");

        _interceptor.Stop();
        _tunnelService.Stop();

        _isRunning = false;
        Log("✅ Брандмауер зупинено");
    }

    /// <summary>
    /// Подія: Перехоплено пакет
    /// </summary>
    private void OnPacketCaptured(object? sender, PacketInfo packet)
    {
        // Визначити напрямок (спрощено)
        TrafficDirection direction = DetermineDirection(packet);

        // Перевірити пакет через фільтр
        var decision = _filterEngine.CheckPacket(packet, direction);

        // Оновити статистику
        _statistics.TotalPackets++;
        _statistics.LastPacketTime = DateTime.UtcNow;

        // Виконати дію
        switch (decision.Action)
        {
            case FilterAction.Allow:
                _statistics.AllowedPackets++;
                // Пропустити пакет далі (no-op в цій реалізації)
                break;

            case FilterAction.Block:
                _statistics.BlockedPackets++;
                Log($"🚫 Заблоковано: {packet.SourceIP}:{packet.SourcePort} → {packet.DestinationIP}:{packet.DestinationPort}");
                break;

            case FilterAction.Tunnel:
                _statistics.TunneledPackets++;
                _ = SendThroughTunnelAsync(packet);
                break;
        }
    }

    /// <summary>
    /// Відправити пакет через захищений тунель
    /// </summary>
    private async Task SendThroughTunnelAsync(PacketInfo packet)
    {
        try
        {
            bool success = await _tunnelService.SendAsync(
                packet.Payload,
                packet.DestinationIP,
                packet.DestinationPort
            );

            if (success)
            {
                Log($"🔒 Відправлено через тунель: {packet.DestinationIP}:{packet.DestinationPort}");
            }
            else
            {
                _statistics.ErrorPackets++;
            }
        }
        catch (Exception ex)
        {
            _statistics.ErrorPackets++;
            Log($"❌ Помилка тунелю: {ex.Message}");
        }
    }

    /// <summary>
    /// Подія: Отримано пакет з тунелю
    /// </summary>
    private void OnTunnelPacketReceived(object? sender, TunnelPacket packet)
    {
        Log($"🔓 Отримано з тунелю: {packet.SourceIP} → {packet.DestinationIP}");
        // Тут можна передати пакет далі в локальну мережу
    }

    /// <summary>
    /// Подія: Пакет відфільтровано
    /// </summary>
    private void OnPacketFiltered(object? sender, FilterDecisionEventArgs e)
    {
        // Можна додати додаткове логування або обробку
    }

    /// <summary>
    /// Подія: Зміна статусу тунелю
    /// </summary>
    private void OnTunnelStatusChanged(object? sender, TunnelStatusEventArgs e)
    {
        _statistics.TunnelStatus = e.Status;
        Log($"Статус тунелю: {e.Status}");
    }

    /// <summary>
    /// Помилка перехоплювача
    /// </summary>
    private void OnInterceptorError(object? sender, string error)
    {
        Log($"⚠️ Interceptor: {error}");
    }

    /// <summary>
    /// Помилка тунелю
    /// </summary>
    private void OnTunnelError(object? sender, string error)
    {
        Log($"⚠️ Tunnel: {error}");
    }

    /// <summary>
    /// Визначити напрямок трафіку
    /// </summary>
    private TrafficDirection DetermineDirection(PacketInfo packet)
    {
        // Спрощена логіка: якщо джерело - локальна мережа, то Outbound
        if (packet.SourceIP.StartsWith("192.168.") ||
            packet.SourceIP.StartsWith("10.") ||
            packet.SourceIP.StartsWith("172.16."))
        {
            return TrafficDirection.Outbound;
        }

        return TrafficDirection.Inbound;
    }

    /// <summary>
    /// Моніторинг статистики
    /// </summary>
    private async Task StatisticsMonitorAsync(CancellationToken cancellationToken)
    {
        DateTime lastUpdate = DateTime.UtcNow;
        long lastTotalPackets = 0;

        while (_isRunning && !cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(1000, cancellationToken);

            // Обчислити швидкість
            TimeSpan elapsed = DateTime.UtcNow - lastUpdate;
            long packetsDiff = _statistics.TotalPackets - lastTotalPackets;

            // Припустимо середній розмір пакету 1024 байти
            _statistics.BytesPerSecond = (packetsDiff * 1024) / elapsed.TotalSeconds;

            lastUpdate = DateTime.UtcNow;
            lastTotalPackets = _statistics.TotalPackets;

            // Викликати подію оновлення
            StatisticsUpdated?.Invoke(this, _statistics);
        }
    }

    /// <summary>
    /// Логування
    /// </summary>
    private void Log(string message)
    {
        string logMessage = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
        Serilog.Log.Information(logMessage);
        LogMessage?.Invoke(this, logMessage);
    }

    public void Dispose()
    {
        Stop();
        _interceptor?.Stop();
        _tunnelService?.Dispose();
        _ruleRepository?.Dispose();
    }
}
