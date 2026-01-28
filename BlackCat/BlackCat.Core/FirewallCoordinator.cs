using BlackCat.Core.Data;
using BlackCat.NetworkCore;
using BlackCat.Shared.Models;
using BlackCat.Shared.Enums;
using NetworkProtocol = BlackCat.Shared.Enums.ProtocolType;
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
    private bool _packetInterceptorActive;

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

        Log("Запуск брандмауера BlackCat...");

        // Запустити тунель
        try
        {
            await _tunnelService.StartAsync(cancellationToken);
            Log($"✅ Тунель запущено на порту {9999}");
        }
        catch (Exception ex)
        {
            Log($"❌ Помилка запуску тунелю: {ex.Message}");
            throw new InvalidOperationException($"Не вдалося запустити тунель: {ex.Message}", ex);
        }

        // Запустити перехоплення пакетів
        // ПРИМІТКА: Вимагає прав адміністратора або буде працювати в тестовому режимі
        try
        {
            _interceptor.Start();
            _packetInterceptorActive = true;
            Log("✅ Перехоплення пакетів активовано (реальний трафік)");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Адреса вже використовується"))
        {
            // Специфічна помилка - адреса зайнята іншим процесом
            _packetInterceptorActive = false;
            Log("⚠️━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━⚠️");
            Log($"⚠️ {ex.Message}");
            Log("⚠️ Закрийте інші мережеві інструменти (Wireshark, VPN, тощо)");
            Log("⚠️ Або перезавантажте комп'ютер для звільнення сокетів");
            Log("⚠️━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━⚠️");
            Log("📊 Перемикання в ТЕСТОВИЙ РЕЖИМ з симульованими пакетами");

            // Запустити генератор тестових пакетів
            _ = Task.Run(() => TestPacketGeneratorAsync(cancellationToken), cancellationToken);
        }
        catch (Exception ex)
        {
            _packetInterceptorActive = false;
            Log($"⚠️ Не вдалося запустити перехоплення пакетів: {ex.Message}");
            Log("⚠️ Можливі причини:");
            Log("   - Відсутні права адміністратора");
            Log("   - Інший процес використовує Raw Socket");
            Log("   - Проблеми з мережевим адаптером");
            Log("📊 Перемикання в ТЕСТОВИЙ РЕЖИМ з симульованими пакетами");

            // Запустити генератор тестових пакетів
            _ = Task.Run(() => TestPacketGeneratorAsync(cancellationToken), cancellationToken);
        }

        _isRunning = true;

        if (_packetInterceptorActive)
        {
            Log("✅ Брандмауер BlackCat запущено у ПОВНОМУ РЕЖИМІ");
        }
        else
        {
            Log("⚠️ Брандмауер BlackCat запущено у ТЕСТОВОМУ РЕЖИМІ");
        }

        // Запустити моніторинг статистики
        _ = Task.Run(() => StatisticsMonitorAsync(cancellationToken), cancellationToken);
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
    /// Генератор тестових пакетів (коли PacketInterceptor не працює)
    /// </summary>
    private async Task TestPacketGeneratorAsync(CancellationToken cancellationToken)
    {
        var random = new Random();
        string[] testIPs = { "192.168.0.1", "10.0.0.1", "8.8.8.8", "1.1.1.1", "192.168.1.100" };

        while (_isRunning && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(random.Next(500, 2000), cancellationToken);

                // Згенерувати фейковий пакет
                var packet = new PacketInfo
                {
                    SourceIP = testIPs[random.Next(testIPs.Length)],
                    DestinationIP = testIPs[random.Next(testIPs.Length)],
                    SourcePort = random.Next(1024, 65535),
                    DestinationPort = random.Next(1, 1024),
                    Protocol = (NetworkProtocol)random.Next(0, 3),
                    Payload = new byte[random.Next(64, 1500)],
                    Timestamp = DateTime.UtcNow
                };

                // Симулювати обробку пакету
                OnPacketCaptured(this, packet);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Log($"Помилка тестового генератора: {ex.Message}");
            }
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
