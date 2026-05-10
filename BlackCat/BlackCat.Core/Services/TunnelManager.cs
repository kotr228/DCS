using System.Collections.Concurrent;
using System.Diagnostics;
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

    private SecureTunnelService? _serverTunnel;
    private NatManager? _natManager;
    private RelayTunnelClient? _relayClient;
    private string? _relayHost;
    private int _relayPort = 9997;
    private readonly ConcurrentDictionary<string, PeerTunnelConnection> _activeTunnels = new();
    private readonly ConcurrentDictionary<string, RelayVirtualConnection> _relayConnections = new();

    public event EventHandler<TunnelConnectionEventArgs>? ConnectionEstablished;
    public event EventHandler<TunnelConnectionEventArgs>? ConnectionLost;
    public event EventHandler<TunnelConnectionEventArgs>? ConnectionFailed;
    public event EventHandler<TunnelDataEventArgs>? DataReceived;

    /// <summary>
    /// Подія вхідного запиту підключення — підпишіться щоб показати діалог прийняти/відхилити
    /// </summary>
    public event EventHandler<BlackCat.NetworkCore.IncomingConnectionRequestEventArgs>? IncomingConnectionRequest;

    /// <summary>true = UPnP відкрив порт, false = UPnP не спрацював</summary>
    public event EventHandler<bool>? UPnPStatusChanged;

    /// <summary>Повідомлення про стан правила Windows Firewall</summary>
    public event EventHandler<string>? FirewallStatusChanged;

    /// <summary>Результат NAT-діагностики після старту</summary>
    public event EventHandler<NatDiagnosticEventArgs>? NatDiagnosticReady;

    /// <summary>
    /// Реальна зовнішня IP адреса (отримана через ipify.org, не через UPnP)
    /// </summary>
    public string? ExternalIP { get; private set; }

    /// <summary>
    /// Чи активне автоматичне переадресування порту
    /// </summary>
    public bool IsPortForwardingActive => _natManager?.IsPortForwardingActive ?? false;

    /// <summary>Налаштувати relay-сервер (host:port). Якщо задано — використовується для з'єднань</summary>
    public void SetRelayServer(string host, int port = 9997)
    {
        _relayHost = host;
        _relayPort = port;
    }

    public bool IsRelayConfigured => !string.IsNullOrWhiteSpace(_relayHost);
    public bool IsRelayConnected  => _relayClient?.IsConnected == true;

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

        // Відкрити порт у Windows Firewall
        string firewallMsg = EnsureFirewallRule(_listenPort);
        FirewallStatusChanged?.Invoke(this, firewallMsg);

        // Реальна зовнішня IP через ipify.org + NAT-діагностика
        _ = Task.Run(async () =>
        {
            var netInfo = new NetworkInfoService();
            var localIP  = netInfo.GetLocalIPAddress();
            var publicIP = await netInfo.GetExternalIPAddressAsync();

            if (publicIP != "Недоступно")
                ExternalIP = publicIP;

            // Перевірка: чи машина за NAT?
            bool behindNat = publicIP != "Недоступно" && localIP != publicIP;
            NatDiagnosticReady?.Invoke(this, new NatDiagnosticEventArgs
            {
                LocalIP    = localIP,
                PublicIP   = publicIP,
                BehindNat  = behindNat,
                ListenPort = _listenPort
            });
        });

        // UPnP у фоні (маппінг порту на роутері)
        _natManager = new NatManager(_listenPort);
        _ = Task.Run(async () =>
        {
            bool upnpSuccess = await _natManager.TryOpenPortAsync();
            UPnPStatusChanged?.Invoke(this, upnpSuccess);
        });

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
        _serverTunnel.IncomingConnectionRequest += (s, e) => IncomingConnectionRequest?.Invoke(this, e);

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

            // Створити TCP клієнт (OS-default connect timeout — без штучного обмеження)
            var tcpClient = new TcpClient();
            try
            {
                await tcpClient.ConnectAsync(peer.Address, peer.Port);
            }
            catch (Exception ex)
            {
                tcpClient.Dispose();
                throw new InvalidOperationException(
                    $"Не вдалося підключитися до {peer.Address}:{peer.Port}: {ex.Message}");
            }

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

    // ─── RELAY ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Підключитися до relay-сервера і зареєструватись там.
    /// Викликати після StartServerAsync коли relay налаштовано.
    /// </summary>
    public async Task<bool> StartRelayAsync(BlackID ourBlackID)
    {
        if (string.IsNullOrWhiteSpace(_relayHost)) return false;

        _relayClient?.Dispose();
        _relayClient = new RelayTunnelClient(_relayHost, _relayPort, ourBlackID.FullID);

        _relayClient.Log         += (_, msg) => Console.WriteLine($"[Relay] {msg}");
        _relayClient.DataReceived += OnRelayDataReceived;
        _relayClient.Disconnected += (_, msg) => RelayStatusChanged?.Invoke(this, $"⚠️ Relay: {msg}");
        _relayClient.IncomingRequest += OnRelayIncomingRequest;

        bool ok = await _relayClient.ConnectAsync();
        RelayStatusChanged?.Invoke(this, ok
            ? $"✅ Relay: підключено до {_relayHost}:{_relayPort}"
            : $"❌ Relay: не вдалося підключитися до {_relayHost}:{_relayPort}");
        return ok;
    }

    /// <summary>
    /// Підключитися до вузла через relay (без портів на обох сторонах)
    /// </summary>
    public async Task<bool> ConnectViaRelayAsync(PeerNode peer, BlackID ourBlackID)
    {
        if (_relayClient == null || !_relayClient.IsConnected)
        {
            RelayStatusChanged?.Invoke(this, "❌ Relay не підключений. Налаштуйте Relay-сервер у налаштуваннях.");
            return false;
        }

        Console.WriteLine($"🔌 Relay: підключення до {peer.BlackID}...");

        bool accepted = await _relayClient.RequestConnectionAsync(peer.BlackID);
        if (!accepted)
        {
            ConnectionFailed?.Invoke(this, new TunnelConnectionEventArgs
            {
                PeerBlackID = peer.BlackID,
                Address = peer.Address,
                Port = peer.Port,
                Error = "Relay: відхилено або вузол недоступний"
            });
            return false;
        }

        // Реєструємо віртуальне з'єднання
        var conn = new RelayVirtualConnection
        {
            PeerBlackID = peer.BlackID,
            ConnectedAt = DateTime.UtcNow
        };
        _relayConnections[peer.BlackID] = conn;

        ConnectionEstablished?.Invoke(this, new TunnelConnectionEventArgs
        {
            PeerBlackID = peer.BlackID,
            Address = $"relay://{_relayHost}",
            Port = _relayPort,
            SessionId = Guid.NewGuid().ToString("N")[..8]
        });

        Console.WriteLine($"✅ Relay: з'єднано з {peer.BlackID}");
        return true;
    }

    /// <summary>
    /// Надіслати дані через relay (якщо є relay-з'єднання) або через прямий тунель
    /// </summary>
    public async Task<bool> SendDataViaRelayAsync(string peerBlackID, byte[] data)
    {
        if (_relayClient != null && _relayConnections.ContainsKey(peerBlackID))
            return await _relayClient.SendDataAsync(data);
        return await SendDataAsync(peerBlackID, data);
    }

    private void OnRelayDataReceived(object? sender, RelayDataEventArgs e)
    {
        DataReceived?.Invoke(this, new TunnelDataEventArgs
        {
            SourceIP = $"relay://{_relayHost}",
            DestinationIP = "local",
            Data = e.Data
        });
    }

    private void OnRelayIncomingRequest(object? sender, RelayIncomingMsg e)
    {
        // Показати той самий toast що і для прямих з'єднань
        var approvalSource = new TaskCompletionSource<bool>();
        var args = new BlackCat.NetworkCore.IncomingConnectionRequestEventArgs
        {
            PeerBlackID   = e.From,
            PeerIP        = $"{e.FromIP} (via relay)",
            ApprovalSource = approvalSource
        };

        IncomingConnectionRequest?.Invoke(this, args);

        // Чекати відповідь користувача і переслати relay-серверу
        _ = Task.Run(async () =>
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            cts.Token.Register(() => approvalSource.TrySetResult(false));
            bool approved = await approvalSource.Task;

            await _relayClient!.RespondToIncomingAsync(approved,
                approved ? "" : "Відхилено користувачем");

            if (approved)
            {
                var conn = new RelayVirtualConnection
                {
                    PeerBlackID = e.From,
                    ConnectedAt = DateTime.UtcNow
                };
                _relayConnections[e.From] = conn;

                ConnectionEstablished?.Invoke(this, new TunnelConnectionEventArgs
                {
                    PeerBlackID = e.From,
                    Address     = $"relay://{_relayHost}",
                    Port        = _relayPort,
                    SessionId   = Guid.NewGuid().ToString("N")[..8]
                });
            }
        });
    }

    public event EventHandler<string>? RelayStatusChanged;

    /// <summary>
    /// Додає правило у Windows Firewall щоб дозволити вхідні TCP-з'єднання на вказаний порт.
    /// Повертає повідомлення для відображення в UI.
    /// </summary>
    private static string EnsureFirewallRule(int port)
    {
        try
        {
            const string ruleName = "BlackCat Secure Tunnel";
            RunNetsh($"advfirewall firewall delete rule name=\"{ruleName}\"");
            bool added = RunNetsh(
                $"advfirewall firewall add rule name=\"{ruleName}\" " +
                $"dir=in action=allow protocol=TCP localport={port} " +
                $"description=\"BlackCat encrypted tunnel port\"");

            return added
                ? $"✅ Windows Firewall: дозволено вхідний TCP порт {port}"
                : $"⚠️ Windows Firewall: не вдалося додати правило — запустіть як Адміністратор";
        }
        catch (Exception ex)
        {
            return $"⚠️ Windows Firewall: {ex.Message}";
        }
    }

    private static bool RunNetsh(string args)
    {
        try
        {
            var psi = new ProcessStartInfo("netsh", args)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var p = Process.Start(psi);
            p?.WaitForExit(5000);
            return p?.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        foreach (var blackID in _activeTunnels.Keys.ToList())
            DisconnectFromNode(blackID);

        _serverTunnel?.Stop();
        _serverTunnel?.Dispose();
        _natManager?.Dispose();
        _relayClient?.Dispose();
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

/// <summary>
/// Віртуальне з'єднання через relay (без прямого TCP)
/// </summary>
public class RelayVirtualConnection
{
    public string PeerBlackID { get; set; } = string.Empty;
    public DateTime ConnectedAt { get; set; }
}

/// <summary>
/// Результат NAT-діагностики при старті
/// </summary>
public class NatDiagnosticEventArgs : EventArgs
{
    public string LocalIP { get; set; } = string.Empty;
    public string PublicIP { get; set; } = string.Empty;
    public bool BehindNat { get; set; }
    public int ListenPort { get; set; }
}
