using System.Collections.Concurrent;
using System.Net.Sockets;
using BlackCat.Core.Data;
using BlackCat.NetworkCore;
using BlackCat.Shared.Models;

namespace BlackCat.Core.Services;

/// <summary>
/// Менеджер для керування з'єднаннями з віддаленими вузлами
/// </summary>
public class TunnelManager : IDisposable
{
    private readonly BlackIDService _blackIDService;
    private readonly HandshakeService _handshakeService;
    private readonly ConnectionMonitorService _connectionMonitor;
    private readonly ConnectionEventRepository _eventRepository;
    private readonly string _masterSecret;
    private readonly int _listenPort;

    private SecureTunnelService? _serverTunnel; // Для прослуховування вхідних з'єднань
    private NatManager? _natManager; // Для автоматичного відкриття портів
    private readonly ConcurrentDictionary<string, PeerTunnelConnection> _activeTunnels = new();

    public event EventHandler<TunnelConnectionEventArgs>? ConnectionEstablished;
    public event EventHandler<TunnelConnectionEventArgs>? ConnectionLost;
    public event EventHandler<TunnelConnectionEventArgs>? ConnectionFailed;
    public event EventHandler<TunnelDataEventArgs>? DataReceived;

    /// <summary>
    /// Зовнішня IP адреса (якщо UPnP успішно налаштовано)
    /// </summary>
    public string? ExternalIP => _natManager?.ExternalIP;

    /// <summary>
    /// Чи активне автоматичне переадресування порту
    /// </summary>
    public bool IsPortForwardingActive => _natManager?.IsPortForwardingActive ?? false;

    public TunnelManager(
        BlackIDService blackIDService,
        HandshakeService handshakeService,
        ConnectionMonitorService connectionMonitor,
        ConnectionEventRepository eventRepository,
        string masterSecret,
        int listenPort = 9999)
    {
        _blackIDService = blackIDService;
        _handshakeService = handshakeService;
        _connectionMonitor = connectionMonitor;
        _eventRepository = eventRepository;
        _masterSecret = masterSecret;
        _listenPort = listenPort;
    }

    /// <summary>
    /// Запустити сервер для прийому вхідних з'єднань
    /// </summary>
    public async Task StartServerAsync(BlackID ourBlackID)
    {
        if (_serverTunnel != null)
            return;

        // Спробувати автоматично відкрити порт через UPnP
        Console.WriteLine("🔧 Автоматичне налаштування мережі...");
        _natManager = new NatManager(_listenPort);

        bool upnpSuccess = await _natManager.TryOpenPortAsync();

        if (upnpSuccess)
        {
            Console.WriteLine($"✅ Порт {_listenPort} автоматично відкрито через UPnP!");
            Console.WriteLine($"🌐 Зовнішня IP: {_natManager.ExternalIP}");
            Console.WriteLine($"💡 Інші вузли можуть підключатися до вас напряму!");
        }
        else
        {
            Console.WriteLine($"⚠️ UPnP недоступний - може знадобитися ручне налаштування роутера");
            Console.WriteLine($"💡 Локальні з'єднання (в одній мережі) працюватимуть без проблем");
        }

        _serverTunnel = new SecureTunnelService(_masterSecret, _listenPort);

        // Налаштувати Black-ID автентифікацію
        _serverTunnel.ConfigureBlackID(
            ourBlackID: ourBlackID,
            handleHello: (hello, ip) => _handshakeService.HandleHello(hello, ourBlackID, ip),
            handleResponse: (response, ip) => _handshakeService.HandleResponse(response, ip)
        );

        // Підписатись на події
        _serverTunnel.PacketReceived += OnPacketReceived;
        _serverTunnel.TunnelError += OnTunnelError;

        await _serverTunnel.StartAsync();

        Console.WriteLine($"✅ TunnelManager: Сервер запущено на порту {_listenPort}");
    }

    /// <summary>
    /// Підключитися до віддаленого вузла
    /// </summary>
    public async Task<bool> ConnectToNodeAsync(PeerNode peer, BlackID ourBlackID)
    {
        try
        {
            Console.WriteLine($"🔌 Підключення до {peer.BlackID} ({peer.Address}:{peer.Port})...");

            // Перевірити чи вже підключені
            if (_activeTunnels.ContainsKey(peer.BlackID))
            {
                Console.WriteLine($"⚠️ Вже підключено до {peer.BlackID}");
                return true;
            }

            // Створити TCP клієнт
            var tcpClient = new TcpClient();
            await tcpClient.ConnectAsync(peer.Address, peer.Port);

            var stream = tcpClient.GetStream();

            // === HANDSHAKE PROTOCOL ===

            // 1. Відправити HELLO
            var helloMessage = _handshakeService.InitiateHandshake(ourBlackID);
            var helloJson = System.Text.Json.JsonSerializer.Serialize(helloMessage);
            var helloBytes = System.Text.Encoding.UTF8.GetBytes(helloJson);

            await stream.WriteAsync(BitConverter.GetBytes(helloBytes.Length));
            await stream.WriteAsync(helloBytes);
            await stream.FlushAsync();

            Console.WriteLine($"  → Відправлено HELLO");

            // 2. Отримати CHALLENGE
            var challengeLengthBytes = new byte[4];
            await stream.ReadAsync(challengeLengthBytes, 0, 4);
            var challengeLength = BitConverter.ToInt32(challengeLengthBytes, 0);

            var challengeBytes = new byte[challengeLength];
            await stream.ReadAsync(challengeBytes, 0, challengeLength);
            var challengeJson = System.Text.Encoding.UTF8.GetString(challengeBytes);
            var challengeMessage = System.Text.Json.JsonSerializer.Deserialize<ChallengeMessage>(challengeJson);

            if (challengeMessage == null)
            {
                throw new InvalidOperationException("Невірна відповідь CHALLENGE від сервера");
            }

            Console.WriteLine($"  ← Отримано CHALLENGE від {challengeMessage.BlackID}");

            // 3. Відправити RESPONSE (підписаний challenge)
            var responseMessage = _handshakeService.HandleChallenge(challengeMessage, ourBlackID);
            var responseJson = System.Text.Json.JsonSerializer.Serialize(responseMessage);
            var responseBytes = System.Text.Encoding.UTF8.GetBytes(responseJson);

            await stream.WriteAsync(BitConverter.GetBytes(responseBytes.Length));
            await stream.WriteAsync(responseBytes);
            await stream.FlushAsync();

            Console.WriteLine($"  → Відправлено RESPONSE");

            // 4. Отримати ACCEPT/REJECT
            var resultLengthBytes = new byte[4];
            await stream.ReadAsync(resultLengthBytes, 0, 4);
            var resultLength = BitConverter.ToInt32(resultLengthBytes, 0);

            var resultBytes = new byte[resultLength];
            await stream.ReadAsync(resultBytes, 0, resultLength);
            var resultJson = System.Text.Encoding.UTF8.GetString(resultBytes);

            // Спробувати розпарсити як AcceptMessage або RejectMessage
            try
            {
                var acceptMessage = System.Text.Json.JsonSerializer.Deserialize<AcceptMessage>(resultJson);
                if (acceptMessage != null && !string.IsNullOrEmpty(acceptMessage.SessionId))
                {
                    Console.WriteLine($"  ✅ ACCEPT: {acceptMessage.Message}");

                    // Зберегти з'єднання
                    var peerConnection = new PeerTunnelConnection
                    {
                        PeerBlackID = peer.BlackID,
                        PeerId = peer.Id,
                        TcpClient = tcpClient,
                        Stream = stream,
                        SessionId = acceptMessage.SessionId,
                        ConnectedAt = DateTime.UtcNow,
                        LastKeepAlive = DateTime.UtcNow
                    };

                    _activeTunnels[peer.BlackID] = peerConnection;

                    // Оновити статистику
                    _connectionMonitor.RegisterSuccessfulConnection(peer.Id, peer.Address);

                    // Подія
                    ConnectionEstablished?.Invoke(this, new TunnelConnectionEventArgs
                    {
                        PeerBlackID = peer.BlackID,
                        Address = peer.Address,
                        Port = peer.Port,
                        SessionId = acceptMessage.SessionId
                    });

                    // Запустити keep-alive цикл
                    _ = Task.Run(() => KeepAliveLoopAsync(peer.BlackID));

                    Console.WriteLine($"✅ Підключено до {peer.BlackID}");
                    return true;
                }
            }
            catch
            {
                // Спробувати як RejectMessage
                var rejectMessage = System.Text.Json.JsonSerializer.Deserialize<RejectMessage>(resultJson);
                if (rejectMessage != null)
                {
                    Console.WriteLine($"  ❌ REJECT: {rejectMessage.Reason}");
                    throw new InvalidOperationException($"З'єднання відхилено: {rejectMessage.Reason}");
                }
            }

            throw new InvalidOperationException("Невідома відповідь від сервера");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Помилка підключення до {peer.BlackID}: {ex.Message}");

            _connectionMonitor.RegisterFailedConnection(peer.Id);

            ConnectionFailed?.Invoke(this, new TunnelConnectionEventArgs
            {
                PeerBlackID = peer.BlackID,
                Address = peer.Address,
                Port = peer.Port,
                Error = ex.Message
            });

            return false;
        }
    }

    /// <summary>
    /// Від'єднатися від вузла
    /// </summary>
    public void DisconnectFromNode(string peerBlackID)
    {
        if (_activeTunnels.TryRemove(peerBlackID, out var connection))
        {
            connection.Stream?.Close();
            connection.TcpClient?.Close();

            ConnectionLost?.Invoke(this, new TunnelConnectionEventArgs
            {
                PeerBlackID = peerBlackID
            });

            Console.WriteLine($"🔌 Від'єднано від {peerBlackID}");
        }
    }

    /// <summary>
    /// Відправити дані через тунель
    /// </summary>
    public async Task<bool> SendDataAsync(string peerBlackID, byte[] data)
    {
        if (!_activeTunnels.TryGetValue(peerBlackID, out var connection))
        {
            Console.WriteLine($"❌ Немає активного з'єднання з {peerBlackID}");
            return false;
        }

        try
        {
            await connection.Stream.WriteAsync(BitConverter.GetBytes(data.Length));
            await connection.Stream.WriteAsync(data);
            await connection.Stream.FlushAsync();

            connection.LastKeepAlive = DateTime.UtcNow;
            _connectionMonitor.UpdateKeepAlive(connection.PeerId);

            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Помилка відправки даних до {peerBlackID}: {ex.Message}");
            DisconnectFromNode(peerBlackID);
            return false;
        }
    }

    /// <summary>
    /// Keep-alive цикл для підтримки з'єднання
    /// </summary>
    private async Task KeepAliveLoopAsync(string peerBlackID)
    {
        while (_activeTunnels.ContainsKey(peerBlackID))
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30));

                if (!_activeTunnels.TryGetValue(peerBlackID, out var connection))
                    break;

                // Відправити keep-alive ping
                var pingData = System.Text.Encoding.UTF8.GetBytes("PING");
                await SendDataAsync(peerBlackID, pingData);

                // Перевірити timeout
                if (DateTime.UtcNow - connection.LastKeepAlive > TimeSpan.FromSeconds(90))
                {
                    Console.WriteLine($"⚠️ Keep-alive timeout для {peerBlackID}");
                    DisconnectFromNode(peerBlackID);
                    break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Keep-alive помилка для {peerBlackID}: {ex.Message}");
                DisconnectFromNode(peerBlackID);
                break;
            }
        }
    }

    /// <summary>
    /// Отримати активні з'єднання
    /// </summary>
    public IReadOnlyDictionary<string, PeerTunnelConnection> GetActiveConnections()
    {
        return _activeTunnels;
    }

    /// <summary>
    /// Перевірити чи підключено до вузла
    /// </summary>
    public bool IsConnectedTo(string peerBlackID)
    {
        return _activeTunnels.ContainsKey(peerBlackID);
    }

    private void OnPacketReceived(object? sender, TunnelPacket packet)
    {
        DataReceived?.Invoke(this, new TunnelDataEventArgs
        {
            SourceIP = packet.SourceIP,
            DestinationIP = packet.DestinationIP,
            Data = packet.EncryptedPayload
        });
    }

    private void OnTunnelError(object? sender, string error)
    {
        Console.WriteLine($"❌ Tunnel Error: {error}");
    }

    public void Dispose()
    {
        // Від'єднати всі активні тунелі
        foreach (var blackID in _activeTunnels.Keys.ToList())
        {
            DisconnectFromNode(blackID);
        }

        _serverTunnel?.Stop();
        _serverTunnel?.Dispose();

        // Закрити UPnP порт
        _natManager?.Dispose();
    }
}

/// <summary>
/// Інформація про активне з'єднання з вузлом
/// </summary>
public class PeerTunnelConnection
{
    public string PeerBlackID { get; set; } = string.Empty;
    public int PeerId { get; set; }
    public TcpClient TcpClient { get; set; } = null!;
    public NetworkStream Stream { get; set; } = null!;
    public string SessionId { get; set; } = string.Empty;
    public DateTime ConnectedAt { get; set; }
    public DateTime LastKeepAlive { get; set; }
    public long BytesSent { get; set; }
    public long BytesReceived { get; set; }
}

/// <summary>
/// Події з'єднання
/// </summary>
public class TunnelConnectionEventArgs : EventArgs
{
    public string PeerBlackID { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public int Port { get; set; }
    public string? SessionId { get; set; }
    public string? Error { get; set; }
}

/// <summary>
/// Події отримання даних
/// </summary>
public class TunnelDataEventArgs : EventArgs
{
    public string SourceIP { get; set; } = string.Empty;
    public string DestinationIP { get; set; } = string.Empty;
    public byte[] Data { get; set; } = Array.Empty<byte>();
}
