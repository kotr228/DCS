using System.Net;
using System.Net.Sockets;

namespace BlackCat.NetworkCore;

/// <summary>
/// Мінімальний STUN-клієнт (RFC 5389): визначає публічний IP:port цієї машини.
/// Використовується для UDP hole punching.
/// </summary>
public static class StunClient
{
    private static readonly (string Host, int Port)[] Servers =
    {
        ("stun.l.google.com",  19302),
        ("stun1.l.google.com", 19302),
        ("stun.cloudflare.com", 3478),
    };

    /// <summary>
    /// Повертає публічний endpoint цього сокета або null якщо всі STUN-сервери недоступні.
    /// </summary>
    public static async Task<IPEndPoint?> GetPublicEndpointAsync(CancellationToken ct = default)
    {
        foreach (var (host, port) in Servers)
        {
            var ep = await TryServerAsync(host, port, ct);
            if (ep != null) return ep;
        }
        return null;
    }

    private static async Task<IPEndPoint?> TryServerAsync(string host, int port, CancellationToken ct)
    {
        try
        {
            using var udp = new UdpClient(AddressFamily.InterNetwork);

            // Binding Request: type=0x0001, len=0, magic=0x2112A442, random transId
            var req = new byte[20];
            req[1] = 0x01;
            req[4] = 0x21; req[5] = 0x12; req[6] = 0xA4; req[7] = 0x42;
            Random.Shared.NextBytes(req.AsSpan(8, 12));

            var addrs = await Dns.GetHostAddressesAsync(host, ct);
            var serverEp = new IPEndPoint(
                addrs.First(a => a.AddressFamily == AddressFamily.InterNetwork), port);

            await udp.SendAsync(req, serverEp, ct);

            using var toCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            toCts.CancelAfter(3000);
            var res = await udp.ReceiveAsync(toCts.Token);
            return ParseResponse(res.Buffer);
        }
        catch { return null; }
    }

    private static IPEndPoint? ParseResponse(byte[] data)
    {
        // Must be Binding Success Response (0x0101), min 20 bytes
        if (data.Length < 20 || data[0] != 0x01 || data[1] != 0x01) return null;

        int msgLen = (data[2] << 8) | data[3];
        int off = 20;

        while (off + 4 <= 20 + msgLen && off + 4 <= data.Length)
        {
            int type = (data[off] << 8) | data[off + 1];
            int len  = (data[off + 2] << 8) | data[off + 3];
            off += 4;

            if (off + len > data.Length) break;

            if (type == 0x0020 && len >= 8) // XOR-MAPPED-ADDRESS (preferred)
            {
                int xport = ((data[off + 2] << 8) | data[off + 3]) ^ 0x2112;
                var ip = new byte[4];
                ip[0] = (byte)(data[off + 4] ^ 0x21);
                ip[1] = (byte)(data[off + 5] ^ 0x12);
                ip[2] = (byte)(data[off + 6] ^ 0xA4);
                ip[3] = (byte)(data[off + 7] ^ 0x42);
                return new IPEndPoint(new IPAddress(ip), xport);
            }

            if (type == 0x0001 && len >= 8) // MAPPED-ADDRESS (fallback)
            {
                int mport = (data[off + 2] << 8) | data[off + 3];
                var ip = new byte[4];
                Array.Copy(data, off + 4, ip, 0, 4);
                return new IPEndPoint(new IPAddress(ip), mport);
            }

            // Advance with 4-byte padding
            off += len + (len % 4 != 0 ? 4 - len % 4 : 0);
        }

        return null;
    }
}
