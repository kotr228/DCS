using System.Collections.Concurrent;
using BlackCat.Core.Data;
using BlackCat.Shared.Models;

namespace BlackCat.Core.Services;

/// <summary>
/// Сервіс для моніторингу з'єднань та автоматичного перепідключення
/// </summary>
public class ConnectionMonitorService : IDisposable
{
    private readonly PeerNodeRepository _peerNodeRepository;
    private readonly ConnectionEventRepository _eventRepository;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly ConcurrentDictionary<int, PeerConnectionState> _connectionStates = new();
    private Task? _monitorTask;
    private readonly TimeSpan _keepAliveInterval = TimeSpan.FromSeconds(30);
    private readonly TimeSpan _reconnectInterval = TimeSpan.FromSeconds(60);
    private readonly int _maxReconnectAttempts = 5;

    public event EventHandler<ConnectionStateChangedEventArgs>? ConnectionStateChanged;
    public event EventHandler<IPAddressChangedEventArgs>? IPAddressChanged;

    public ConnectionMonitorService(
        PeerNodeRepository peerNodeRepository,
        ConnectionEventRepository eventRepository)
    {
        _peerNodeRepository = peerNodeRepository;
        _eventRepository = eventRepository;
    }

    /// <summary>
    /// Запустити моніторинг з'єднань
    /// </summary>
    public void Start()
    {
        if (_monitorTask != null)
            return;

        _monitorTask = Task.Run(async () => await MonitorConnectionsAsync(_cancellationTokenSource.Token));
    }

    /// <summary>
    /// Зупинити моніторинг
    /// </summary>
    public void Stop()
    {
        _cancellationTokenSource.Cancel();
        _monitorTask?.Wait(TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// Зареєструвати успішне з'єднання
    /// </summary>
    public void RegisterSuccessfulConnection(int peerNodeId, string currentIP)
    {
        var state = _connectionStates.GetOrAdd(peerNodeId, id => new PeerConnectionState
        {
            PeerNodeId = id,
            IsConnected = true,
            LastSuccessfulConnection = DateTime.UtcNow,
            CurrentIP = currentIP,
            ReconnectAttempts = 0
        });

        state.IsConnected = true;
        state.LastSuccessfulConnection = DateTime.UtcNow;
        state.LastKeepAlive = DateTime.UtcNow;
        state.ReconnectAttempts = 0;

        // Перевірити чи змінилась IP адреса
        if (state.CurrentIP != currentIP)
        {
            var oldIP = state.CurrentIP;
            state.CurrentIP = currentIP;

            // Оновити IP в базі даних
            var peer = _peerNodeRepository.GetPeerNode(peerNodeId);
            if (peer != null && peer.Address != currentIP)
            {
                var previousAddress = peer.Address;
                peer.Address = currentIP;
                _peerNodeRepository.UpdatePeerNode(peer);

                // Зберегти в історію адрес
                SaveAddressHistory(peerNodeId, previousAddress, currentIP);

                // Подія про зміну IP
                IPAddressChanged?.Invoke(this, new IPAddressChangedEventArgs
                {
                    PeerNodeId = peerNodeId,
                    BlackID = peer.BlackID,
                    OldIP = previousAddress,
                    NewIP = currentIP,
                    Timestamp = DateTime.UtcNow
                });

                _eventRepository.LogEvent(new ConnectionEvent
                {
                    RemoteBlackID = peer.BlackID,
                    RemoteIP = currentIP,
                    RemotePort = peer.Port,
                    EventType = Shared.Enums.ConnectionEventType.Connected,
                    Direction = Shared.Enums.ConnectionDirection.Inbound,
                    Message = $"IP адресу оновлено: {previousAddress} → {currentIP}",
                    IsAuthenticated = true,
                    Timestamp = DateTime.UtcNow
                });
            }
        }

        ConnectionStateChanged?.Invoke(this, new ConnectionStateChangedEventArgs
        {
            PeerNodeId = peerNodeId,
            IsConnected = true,
            Timestamp = DateTime.UtcNow
        });
    }

    /// <summary>
    /// Зареєструвати невдале з'єднання
    /// </summary>
    public void RegisterFailedConnection(int peerNodeId)
    {
        var state = _connectionStates.GetOrAdd(peerNodeId, id => new PeerConnectionState
        {
            PeerNodeId = id,
            IsConnected = false
        });

        state.IsConnected = false;
        state.LastFailedConnection = DateTime.UtcNow;
        state.ReconnectAttempts++;

        ConnectionStateChanged?.Invoke(this, new ConnectionStateChangedEventArgs
        {
            PeerNodeId = peerNodeId,
            IsConnected = false,
            Timestamp = DateTime.UtcNow
        });
    }

    /// <summary>
    /// Оновити keep-alive
    /// </summary>
    public void UpdateKeepAlive(int peerNodeId)
    {
        if (_connectionStates.TryGetValue(peerNodeId, out var state))
        {
            state.LastKeepAlive = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Отримати стан з'єднання
    /// </summary>
    public PeerConnectionState? GetConnectionState(int peerNodeId)
    {
        return _connectionStates.TryGetValue(peerNodeId, out var state) ? state : null;
    }

    /// <summary>
    /// Моніторити з'єднання та автоматично перепідключатись
    /// </summary>
    private async Task MonitorConnectionsAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // Отримати всі довірені активні вузли
                var trustedPeers = _peerNodeRepository.GetTrustedPeerNodes();

                foreach (var peer in trustedPeers)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    var state = _connectionStates.GetOrAdd(peer.Id, id => new PeerConnectionState
                    {
                        PeerNodeId = id,
                        IsConnected = false
                    });

                    // Перевірити keep-alive timeout
                    if (state.IsConnected &&
                        DateTime.UtcNow - state.LastKeepAlive > _keepAliveInterval * 2)
                    {
                        // Keep-alive timeout - з'єднання втрачено
                        state.IsConnected = false;

                        _eventRepository.LogEvent(new ConnectionEvent
                        {
                            RemoteBlackID = peer.BlackID,
                            RemoteIP = peer.Address,
                            RemotePort = peer.Port,
                            EventType = Shared.Enums.ConnectionEventType.Disconnected,
                            Direction = Shared.Enums.ConnectionDirection.Outbound,
                            Message = "Keep-alive timeout - з'єднання втрачено",
                            Timestamp = DateTime.UtcNow
                        });

                        ConnectionStateChanged?.Invoke(this, new ConnectionStateChangedEventArgs
                        {
                            PeerNodeId = peer.Id,
                            IsConnected = false,
                            Timestamp = DateTime.UtcNow
                        });
                    }

                    // Спробувати перепідключитись якщо з'єднання втрачено
                    if (!state.IsConnected &&
                        state.ReconnectAttempts < _maxReconnectAttempts &&
                        DateTime.UtcNow - state.LastReconnectAttempt > GetReconnectDelay(state.ReconnectAttempts))
                    {
                        state.LastReconnectAttempt = DateTime.UtcNow;

                        // TODO: Викликати метод перепідключення з SecureTunnelService
                        // Поки що просто логуємо
                        _eventRepository.LogEvent(new ConnectionEvent
                        {
                            RemoteBlackID = peer.BlackID,
                            RemoteIP = peer.Address,
                            RemotePort = peer.Port,
                            EventType = Shared.Enums.ConnectionEventType.ConnectionAttempt,
                            Direction = Shared.Enums.ConnectionDirection.Outbound,
                            Message = $"Спроба автоматичного перепідключення #{state.ReconnectAttempts + 1}/{_maxReconnectAttempts}",
                            Timestamp = DateTime.UtcNow
                        });
                    }
                }

                // Чекати перед наступною перевіркою
                await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Помилка в ConnectionMonitor: {ex.Message}");
                await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            }
        }
    }

    /// <summary>
    /// Отримати затримку перед наступною спробою (exponential backoff)
    /// </summary>
    private TimeSpan GetReconnectDelay(int attemptNumber)
    {
        // 10s, 20s, 40s, 80s, 160s
        var delaySeconds = Math.Min(10 * Math.Pow(2, attemptNumber), 300); // max 5 хвилин
        return TimeSpan.FromSeconds(delaySeconds);
    }

    /// <summary>
    /// Зберегти історію зміни адрес
    /// </summary>
    private void SaveAddressHistory(int peerNodeId, string oldAddress, string newAddress)
    {
        // TODO: Реалізувати таблицю AddressHistory для збереження історії змін IP
        // Поки що просто логуємо в ConnectionEvents
        Console.WriteLine($"Peer {peerNodeId}: IP changed from {oldAddress} to {newAddress}");
    }

    /// <summary>
    /// Скинути лічильник спроб перепідключення
    /// </summary>
    public void ResetReconnectAttempts(int peerNodeId)
    {
        if (_connectionStates.TryGetValue(peerNodeId, out var state))
        {
            state.ReconnectAttempts = 0;
        }
    }

    public void Dispose()
    {
        Stop();
        _cancellationTokenSource.Dispose();
    }
}

/// <summary>
/// Стан з'єднання з вузлом
/// </summary>
public class PeerConnectionState
{
    public int PeerNodeId { get; set; }
    public bool IsConnected { get; set; }
    public string? CurrentIP { get; set; }
    public DateTime LastSuccessfulConnection { get; set; }
    public DateTime LastFailedConnection { get; set; }
    public DateTime LastKeepAlive { get; set; }
    public DateTime LastReconnectAttempt { get; set; }
    public int ReconnectAttempts { get; set; }
}

/// <summary>
/// Подія зміни стану з'єднання
/// </summary>
public class ConnectionStateChangedEventArgs : EventArgs
{
    public int PeerNodeId { get; set; }
    public bool IsConnected { get; set; }
    public DateTime Timestamp { get; set; }
}

/// <summary>
/// Подія зміни IP адреси
/// </summary>
public class IPAddressChangedEventArgs : EventArgs
{
    public int PeerNodeId { get; set; }
    public string BlackID { get; set; } = string.Empty;
    public string OldIP { get; set; } = string.Empty;
    public string NewIP { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}
