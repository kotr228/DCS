using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace BlackCat.Core.Services;

/// <summary>
/// Паспорт вузла BlackCat, що передається через IPC до DCS NetworkCore.
/// </summary>
public class BlackCatPassport
{
    public string BlackId      { get; set; } = string.Empty;
    public string DisplayName  { get; set; } = string.Empty;
    public string IpAddress    { get; set; } = string.Empty;
    public int    Port         { get; set; }
    public string Status       { get; set; } = "Connected"; // "Connected" | "Offline"
    public DateTime Timestamp  { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// IPC-клієнт BlackCat → DCS NetworkCore.
/// Надсилає паспорт вузла через Named Pipe "BlackCatBridgePipe" при встановленні
/// або завершенні WAN-тунелю.  Всі помилки поглинаються — відсутність DCS
/// не повинна ламати роботу BlackCat.
/// </summary>
public sealed class IpcBridgeService : IDisposable
{
    private const string PipeName    = "BlackCatBridgePipe";
    private const int    ConnectMs   = 3000;

    private readonly TunnelManager _tunnelManager;
    private bool _disposed;

    public IpcBridgeService(TunnelManager tunnelManager)
    {
        _tunnelManager = tunnelManager;
        _tunnelManager.ConnectionEstablished += OnConnectionEstablished;
        _tunnelManager.ConnectionLost        += OnConnectionLost;
    }

    // ─── Обробники подій ─────────────────────────────────────────────────────

    private void OnConnectionEstablished(object? sender, TunnelConnectionEventArgs e)
    {
        _ = SendPassportAsync(new BlackCatPassport
        {
            BlackId     = e.PeerBlackID,
            DisplayName = e.PeerBlackID,
            IpAddress   = e.Address,
            Port        = e.Port,
            Status      = "Connected",
            Timestamp   = DateTime.UtcNow
        });
    }

    private void OnConnectionLost(object? sender, TunnelConnectionEventArgs e)
    {
        _ = SendPassportAsync(new BlackCatPassport
        {
            BlackId     = e.PeerBlackID,
            DisplayName = e.PeerBlackID,
            IpAddress   = e.Address,
            Port        = e.Port,
            Status      = "Offline",
            Timestamp   = DateTime.UtcNow
        });
    }

    // ─── Відправка паспорту ───────────────────────────────────────────────────

    public async Task SendPassportAsync(BlackCatPassport passport)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            await pipe.ConnectAsync(ConnectMs);

            if (!pipe.IsConnected) return;

            var json = JsonSerializer.Serialize(passport) + "\n";
            var data = Encoding.UTF8.GetBytes(json);
            await pipe.WriteAsync(data);
            await pipe.FlushAsync();
        }
        catch
        {
            // DCS не запущено або недоступне — тихо ігноруємо
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            _tunnelManager.ConnectionEstablished -= OnConnectionEstablished;
            _tunnelManager.ConnectionLost        -= OnConnectionLost;
        }
        catch { }
    }
}
