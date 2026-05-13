using System.Text;
using System.Text.Json;

namespace BlackCat.Core.Services;

/// <summary>
/// Спільний протокол для relay-з'єднань.
/// Формат фрейму: [1 byte type][4 bytes length][payload]
/// </summary>
public static class RelayProtocol
{
    public const byte TypeControl = 0x01;
    public const byte TypeData    = 0x02;

    // --- Команди ---
    public const string CmdRegister  = "register";
    public const string CmdRegistered= "registered";
    public const string CmdConnect   = "connect";
    public const string CmdIncoming  = "incoming";
    public const string CmdAccept    = "accept";
    public const string CmdReject    = "reject";
    public const string CmdAccepted  = "accepted";
    public const string CmdRejected  = "rejected";
    public const string CmdPing        = "ping";
    public const string CmdPong        = "pong";
    public const string CmdDisconnect  = "disconnect";
    public const string CmdStunAnswer  = "stun_answer"; // hole punch signaling

    public static async Task WriteControlAsync(Stream stream, object message, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(message);
        var payload = Encoding.UTF8.GetBytes(json);
        await WriteFrameAsync(stream, TypeControl, payload, ct);
    }

    public static async Task WriteDataAsync(Stream stream, byte[] data, CancellationToken ct = default)
    {
        await WriteFrameAsync(stream, TypeData, data, ct);
    }

    private static async Task WriteFrameAsync(Stream stream, byte type, byte[] payload, CancellationToken ct)
    {
        var header = new byte[5];
        header[0] = type;
        BitConverter.GetBytes(payload.Length).CopyTo(header, 1);
        await stream.WriteAsync(header, ct);
        await stream.WriteAsync(payload, ct);
        await stream.FlushAsync(ct);
    }

    public static async Task<(byte type, byte[] payload)?> ReadFrameAsync(Stream stream, CancellationToken ct = default)
    {
        var header = new byte[5];
        if (!await ReadExactAsync(stream, header, ct)) return null;

        byte type = header[0];
        int length = BitConverter.ToInt32(header, 1);
        if (length < 0 || length > 10 * 1024 * 1024) return null;

        var payload = new byte[length];
        if (!await ReadExactAsync(stream, payload, ct)) return null;
        return (type, payload);
    }

    private static async Task<bool> ReadExactAsync(Stream stream, byte[] buf, CancellationToken ct)
    {
        int offset = 0;
        while (offset < buf.Length)
        {
            int read = await stream.ReadAsync(buf, offset, buf.Length - offset, ct);
            if (read == 0) return false;
            offset += read;
        }
        return true;
    }

    public static T? ParseControl<T>(byte[] payload) where T : RelayMessage
    {
        var json = Encoding.UTF8.GetString(payload);
        return JsonSerializer.Deserialize<T>(json);
    }

    public static RelayMessage? ParseControlBase(byte[] payload)
    {
        var json = Encoding.UTF8.GetString(payload);
        return JsonSerializer.Deserialize<RelayMessage>(json);
    }
}

public class RelayMessage
{
    public string Cmd { get; set; } = string.Empty;
}

public class RelayRegisterMsg : RelayMessage
{
    public RelayRegisterMsg() => Cmd = RelayProtocol.CmdRegister;
    public string BlackID { get; set; } = string.Empty;
}

public class RelayConnectMsg : RelayMessage
{
    public RelayConnectMsg() => Cmd = RelayProtocol.CmdConnect;
    public string  Target        { get; set; } = string.Empty;
    public string  From          { get; set; } = string.Empty;
    public string? StunEndpoint  { get; set; } // "ip:port" для hole punching
}

public class RelayIncomingMsg : RelayMessage
{
    public RelayIncomingMsg() => Cmd = RelayProtocol.CmdIncoming;
    public string  From          { get; set; } = string.Empty;
    public string  FromIP        { get; set; } = string.Empty;
    public string? StunEndpoint  { get; set; } // публічний UDP endpoint ініціатора
}

public class RelayStunAnswerMsg : RelayMessage
{
    public RelayStunAnswerMsg() => Cmd = RelayProtocol.CmdStunAnswer;
    public string  To            { get; set; } = string.Empty;
    public string  From          { get; set; } = string.Empty;
    public string  StunEndpoint  { get; set; } = string.Empty;
}

public class RelayAcceptMsg : RelayMessage
{
    public RelayAcceptMsg() => Cmd = RelayProtocol.CmdAccept;
}

public class RelayRejectMsg : RelayMessage
{
    public RelayRejectMsg() => Cmd = RelayProtocol.CmdReject;
    public string Reason { get; set; } = string.Empty;
}

public class RelaySimpleMsg : RelayMessage
{
    public RelaySimpleMsg(string cmd) => Cmd = cmd;
}
