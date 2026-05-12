using System.Net.Sockets;
using BlackCat.Shared.Models;

namespace BlackCat.Core.Services;

/// <summary>
/// Relay-клієнт: підключається до RelayServer і організовує захищений тунель
/// через проміжний вузол. Порти на власному роутері не потрібні.
/// </summary>
public class RelayTunnelClient : IDisposable
{
    private readonly string _relayHost;
    private readonly int    _relayPort;
    private readonly string _ourBlackID;

    private TcpClient?     _tcp;
    private NetworkStream? _stream;
    private CancellationTokenSource? _cts;
    private bool _registered;

    public bool IsConnected => _tcp?.Connected == true && _registered;

    public event EventHandler<RelayDataEventArgs>?    DataReceived;
    public event EventHandler<RelayIncomingMsg>?      IncomingRequest;
    public event EventHandler<RelayStunAnswerMsg>?    StunAnswerReceived;
    public event EventHandler<string>?                Log;
    public event EventHandler<string>?                Disconnected;

    public RelayTunnelClient(string relayHost, int relayPort, string ourBlackID)
    {
        _relayHost  = relayHost;
        _relayPort  = relayPort;
        _ourBlackID = ourBlackID;
    }

    /// <summary>
    /// Підключитися до relay-сервера і зареєструватись
    /// </summary>
    public async Task<bool> ConnectAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            _tcp = new TcpClient();
            using var connectCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await _tcp.ConnectAsync(_relayHost, _relayPort, connectCts.Token);
            _stream = _tcp.GetStream();

            // Зареєструватись
            await RelayProtocol.WriteControlAsync(_stream, new RelayRegisterMsg
            {
                BlackID = _ourBlackID
            }, _cts.Token);

            // Чекати підтвердження
            var frame = await RelayProtocol.ReadFrameAsync(_stream, _cts.Token);
            if (frame == null) return false;

            var msg = RelayProtocol.ParseControlBase(frame.Value.payload);
            if (msg?.Cmd != RelayProtocol.CmdRegistered) return false;

            _registered = true;
            WriteLog($"✅ Relay: підключено до {_relayHost}:{_relayPort}, зареєстровано як {_ourBlackID}");

            // Запустити фоновий listener
            _ = Task.Run(() => ListenLoopAsync(_cts.Token), _cts.Token);
            _ = Task.Run(() => KeepAliveLoopAsync(_cts.Token), _cts.Token);

            return true;
        }
        catch (Exception ex)
        {
            WriteLog($"❌ Relay: не вдалося підключитися — {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Запросити з'єднання з іншим вузлом через relay
    /// </summary>
    public async Task<bool> RequestConnectionAsync(string targetBlackID, string? ourStunEndpoint = null, CancellationToken cancellationToken = default)
    {
        if (_stream == null || !_registered) return false;

        var tcs = new TaskCompletionSource<bool>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        timeout.Token.Register(() => tcs.TrySetResult(false));

        void OnAcceptedOrRejected(string cmd)
        {
            if (cmd == RelayProtocol.CmdAccepted) tcs.TrySetResult(true);
            else if (cmd == RelayProtocol.CmdRejected) tcs.TrySetResult(false);
        }

        _pendingConnectionCallback = OnAcceptedOrRejected;

        await RelayProtocol.WriteControlAsync(_stream, new RelayConnectMsg
        {
            Target       = targetBlackID,
            From         = _ourBlackID,
            StunEndpoint = ourStunEndpoint
        }, cancellationToken);

        WriteLog($"🔌 Relay: запит на підключення до {targetBlackID}...");
        return await tcs.Task;
    }

    /// <summary>
    /// Надіслати STUN endpoint через relay до вказаного вузла (для hole punching)
    /// </summary>
    public async Task SendStunAnswerAsync(string toBlackID, string ourStunEndpoint, CancellationToken ct = default)
    {
        if (_stream == null || !_registered) return;
        await RelayProtocol.WriteControlAsync(_stream, new RelayStunAnswerMsg
        {
            To           = toBlackID,
            From         = _ourBlackID,
            StunEndpoint = ourStunEndpoint
        }, ct);
    }

    private Action<string>? _pendingConnectionCallback;

    /// <summary>
    /// Відповісти на вхідний запит (true = прийняти, false = відхилити)
    /// </summary>
    public async Task RespondToIncomingAsync(bool accept, string reason = "")
    {
        if (_stream == null) return;

        if (accept)
            await RelayProtocol.WriteControlAsync(_stream, new RelayAcceptMsg());
        else
            await RelayProtocol.WriteControlAsync(_stream, new RelayRejectMsg { Reason = reason });
    }

    /// <summary>
    /// Надіслати зашифровані дані через relay (партнер отримає їх на іншому кінці)
    /// </summary>
    public async Task<bool> SendDataAsync(byte[] data, CancellationToken ct = default)
    {
        if (_stream == null || !_registered) return false;
        try
        {
            await RelayProtocol.WriteDataAsync(_stream, data, ct);
            return true;
        }
        catch (Exception ex)
        {
            WriteLog($"❌ Relay send: {ex.Message}");
            return false;
        }
    }

    private async Task ListenLoopAsync(CancellationToken ct)
    {
        if (_stream == null) return;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var frame = await RelayProtocol.ReadFrameAsync(_stream, ct);
                if (frame == null) break;

                if (frame.Value.type == RelayProtocol.TypeControl)
                {
                    var msg = RelayProtocol.ParseControlBase(frame.Value.payload);
                    if (msg == null) continue;

                    switch (msg.Cmd)
                    {
                        case RelayProtocol.CmdIncoming:
                            var incoming = RelayProtocol.ParseControl<RelayIncomingMsg>(frame.Value.payload);
                            if (incoming != null)
                            {
                                WriteLog($"🔔 Relay: вхідний запит від {incoming.From} ({incoming.FromIP})");
                                IncomingRequest?.Invoke(this, incoming);
                            }
                            break;

                        case RelayProtocol.CmdAccepted:
                            WriteLog("✅ Relay: з'єднання прийнято");
                            _pendingConnectionCallback?.Invoke(RelayProtocol.CmdAccepted);
                            _pendingConnectionCallback = null;
                            break;

                        case RelayProtocol.CmdRejected:
                            var rej = RelayProtocol.ParseControl<RelayRejectMsg>(frame.Value.payload);
                            WriteLog($"❌ Relay: відхилено — {rej?.Reason}");
                            _pendingConnectionCallback?.Invoke(RelayProtocol.CmdRejected);
                            _pendingConnectionCallback = null;
                            break;

                        case RelayProtocol.CmdStunAnswer:
                            var stunMsg = RelayProtocol.ParseControl<RelayStunAnswerMsg>(frame.Value.payload);
                            if (stunMsg != null)
                                StunAnswerReceived?.Invoke(this, stunMsg);
                            break;

                        case RelayProtocol.CmdPong:
                            break;
                    }
                }
                else if (frame.Value.type == RelayProtocol.TypeData)
                {
                    DataReceived?.Invoke(this, new RelayDataEventArgs { Data = frame.Value.payload });
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                if (!ct.IsCancellationRequested)
                    WriteLog($"❌ Relay listen: {ex.Message}");
                break;
            }
        }

        _registered = false;
        Disconnected?.Invoke(this, "З'єднання з relay-сервером втрачено");
    }

    private async Task KeepAliveLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _registered)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(25), ct);
                if (_stream != null)
                    await RelayProtocol.WriteControlAsync(_stream, new RelaySimpleMsg(RelayProtocol.CmdPing), ct);
            }
            catch { break; }
        }
    }

    private void WriteLog(string msg) => Log?.Invoke(this, msg);

    public void Dispose()
    {
        _cts?.Cancel();
        try { _tcp?.Close(); } catch { }
    }
}

public class RelayDataEventArgs : EventArgs
{
    public byte[] Data { get; set; } = Array.Empty<byte>();
}
