using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace BlackCat.Core.Services;

/// <summary>
/// Автоматичний пошук вузлів BlackCat у локальній мережі через UDP broadcast.
/// Не потребує інтернету або налаштування роутера.
/// </summary>
public class LanDiscoveryService : IDisposable
{
    private const int DiscoveryPort = 9998;
    private const string DiscoveryMagic = "BLACKCAT_LAN_V1";
    private const int AnnounceIntervalSeconds = 30;

    private UdpClient? _listener;
    private CancellationTokenSource? _cts;
    private string _blackID = string.Empty;
    private int _tunnelPort = 9999;
    private bool _isRunning;

    public event EventHandler<LanPeerDiscoveredEventArgs>? PeerDiscovered;

    public bool IsRunning => _isRunning;

    /// <summary>
    /// Запустити сервіс: слухати анонси та регулярно сповіщати про себе
    /// </summary>
    public Task StartAsync(string blackID, int tunnelPort = 9999, CancellationToken cancellationToken = default)
    {
        if (_isRunning) return Task.CompletedTask;

        _blackID = blackID;
        _tunnelPort = tunnelPort;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        try
        {
            _listener = new UdpClient();
            _listener.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _listener.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));
            _listener.EnableBroadcast = true;

            _isRunning = true;

            Console.WriteLine($"📡 LAN Discovery запущено на порту {DiscoveryPort}");

            _ = Task.Run(() => ListenLoopAsync(_cts.Token), _cts.Token);
            _ = Task.Run(() => AnnounceLoopAsync(_cts.Token), _cts.Token);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️ LAN Discovery: не вдалося запустити ({ex.Message})");
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Надіслати broadcast прямо зараз для негайного пошуку вузлів
    /// </summary>
    public async Task ScanNowAsync()
    {
        try
        {
            using var sender = new UdpClient();
            sender.EnableBroadcast = true;

            var msg = BuildMessage("HELLO");
            var bytes = Encoding.UTF8.GetBytes(msg);
            await sender.SendAsync(bytes, bytes.Length, new IPEndPoint(IPAddress.Broadcast, DiscoveryPort));

            Console.WriteLine("🔍 LAN Discovery: broadcast-запит надіслано");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️ LAN Discovery scan: {ex.Message}");
        }
    }

    private async Task AnnounceLoopAsync(CancellationToken token)
    {
        // Перший анонс одразу при старті
        await AnnounceAsync();

        while (!token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(AnnounceIntervalSeconds), token);
                await AnnounceAsync();
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ LAN announce: {ex.Message}");
            }
        }
    }

    private async Task AnnounceAsync()
    {
        if (_listener == null) return;
        try
        {
            var bytes = Encoding.UTF8.GetBytes(BuildMessage("ANNOUNCE"));
            await _listener.SendAsync(bytes, bytes.Length, new IPEndPoint(IPAddress.Broadcast, DiscoveryPort));
        }
        catch { }
    }

    private async Task ListenLoopAsync(CancellationToken token)
    {
        if (_listener == null) return;

        while (!token.IsCancellationRequested)
        {
            try
            {
                var result = await _listener.ReceiveAsync(token);
                _ = Task.Run(() => HandlePacket(result), token);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                if (!token.IsCancellationRequested)
                    Console.WriteLine($"⚠️ LAN listen: {ex.Message}");
            }
        }
    }

    private async void HandlePacket(UdpReceiveResult result)
    {
        try
        {
            var json = Encoding.UTF8.GetString(result.Buffer);
            var msg = JsonSerializer.Deserialize<DiscoveryPacket>(json);

            if (msg?.Magic != DiscoveryMagic) return;
            if (msg.BlackID == _blackID) return; // ігнорувати себе

            var senderIP = result.RemoteEndPoint.Address.ToString();
            Console.WriteLine($"📡 LAN: знайдено вузол {msg.BlackID} @ {senderIP}:{msg.Port}");

            PeerDiscovered?.Invoke(this, new LanPeerDiscoveredEventArgs
            {
                BlackID = msg.BlackID,
                IPAddress = senderIP,
                Port = msg.Port,
                DiscoveredAt = DateTime.UtcNow
            });

            // Відповісти на HELLO своїм анонсом (unicast)
            if (msg.Type == "HELLO" && _listener != null)
            {
                var reply = Encoding.UTF8.GetBytes(BuildMessage("ANNOUNCE"));
                await _listener.SendAsync(reply, reply.Length, result.RemoteEndPoint);
            }
        }
        catch { }
    }

    private string BuildMessage(string type)
    {
        return JsonSerializer.Serialize(new DiscoveryPacket
        {
            Magic = DiscoveryMagic,
            BlackID = _blackID,
            Port = _tunnelPort,
            Type = type
        });
    }

    public void Dispose()
    {
        _cts?.Cancel();
        try { _listener?.Close(); } catch { }
        _listener?.Dispose();
        _isRunning = false;
    }
}

public class DiscoveryPacket
{
    public string Magic { get; set; } = string.Empty;
    public string BlackID { get; set; } = string.Empty;
    public int Port { get; set; }
    public string Type { get; set; } = string.Empty;
}

public class LanPeerDiscoveredEventArgs : EventArgs
{
    public string BlackID { get; set; } = string.Empty;
    public string IPAddress { get; set; } = string.Empty;
    public int Port { get; set; }
    public DateTime DiscoveredAt { get; set; }
}
