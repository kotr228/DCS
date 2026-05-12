using System.Net;
using System.Net.Sockets;
using BlackCat.Crypto;
using BlackCat.Shared.Models;

namespace BlackCat.NetworkCore;

/// <summary>
/// UDP-тунель із MQE-шифруванням. Використовується після успішного hole punching.
/// </summary>
public sealed class UdpEncryptedTunnel : IDisposable
{
    private readonly UdpClient        _udp;
    private readonly MQECryptoService _crypto;
    private readonly IPEndPoint       _remote;
    private readonly CancellationTokenSource _cts = new();

    private DateTime _lastSeen = DateTime.UtcNow;

    private const int KeepaliveMs      = 5_000;
    private const int TimeoutSeconds   = 30;
    private static readonly byte[] KeepaliveBytes = { 0xFF };

    public bool IsConnected { get; private set; }

    public event EventHandler<byte[]>? DataReceived;
    public event EventHandler<string>? Disconnected;

    public UdpEncryptedTunnel(UdpClient udp, MQECryptoService crypto, IPEndPoint remote)
    {
        _udp    = udp;
        _crypto = crypto;
        _remote = remote;
    }

    public void Start()
    {
        IsConnected = true;
        _ = Task.Run(ReceiveLoopAsync, _cts.Token);
        _ = Task.Run(KeepaliveLoopAsync, _cts.Token);
    }

    public async Task SendAsync(byte[] plaintext, string srcIp = "", string dstIp = "")
    {
        var packet = _crypto.Encrypt(plaintext, srcIp, dstIp);
        var bytes  = packet.ToBytes();
        await _udp.SendAsync(bytes, bytes.Length, _remote);
    }

    private async Task ReceiveLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                var res = await _udp.ReceiveAsync(_cts.Token);

                // Keepalive ping — просто оновити timestamp
                if (res.Buffer.Length == 1 && res.Buffer[0] == 0xFF)
                {
                    _lastSeen = DateTime.UtcNow;
                    continue;
                }

                var packet = TunnelPacket.FromBytes(res.Buffer);
                var data   = _crypto.Decrypt(packet);
                _lastSeen  = DateTime.UtcNow;
                DataReceived?.Invoke(this, data);
            }
            catch (OperationCanceledException) { break; }
            catch { /* ігноруємо decode-помилки */ }
        }
    }

    private async Task KeepaliveLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(KeepaliveMs, _cts.Token);

                if (DateTime.UtcNow - _lastSeen > TimeSpan.FromSeconds(TimeoutSeconds))
                {
                    IsConnected = false;
                    Disconnected?.Invoke(this, "UDP keepalive timeout");
                    break;
                }

                await _udp.SendAsync(KeepaliveBytes, 1, _remote);
            }
            catch (OperationCanceledException) { break; }
            catch { break; }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _udp.Dispose();
    }
}
