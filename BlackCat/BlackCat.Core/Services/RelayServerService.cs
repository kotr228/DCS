using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace BlackCat.Core.Services;

/// <summary>
/// Relay-сервер: приймає підключення від клієнтів і пробрасовує трафік між парами.
/// Запускайте на будь-якій машині з відкритим портом (VPS, сервер).
/// Клієнти SKLAD і MAIN підключаються до нього назовні — їм самим відкритий порт не потрібен.
/// </summary>
public class RelayServerService : IDisposable
{
    private readonly int _port;
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;

    // Зареєстровані клієнти: blackID → сесія
    private readonly ConcurrentDictionary<string, RelaySession> _sessions = new();

    public event EventHandler<string>? Log;

    public RelayServerService(int port = 9997)
    {
        _port = port;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _listener = new TcpListener(IPAddress.Any, _port);
        _listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _listener.Start();

        WriteLog($"✅ Relay-сервер запущено на порту {_port}");

        while (!_cts.Token.IsCancellationRequested)
        {
            try
            {
                var client = await _listener.AcceptTcpClientAsync(_cts.Token);
                _ = Task.Run(() => HandleClientAsync(client, _cts.Token));
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (Exception ex) when (!_cts.IsCancellationRequested)
            {
                WriteLog($"⚠️ Помилка прийому: {ex.Message}");
            }
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        var remoteIP = ((IPEndPoint)client.Client.RemoteEndPoint!).Address.ToString();
        RelaySession? session = null;

        try
        {
            var stream = client.GetStream();

            // Перший фрейм: реєстрація Black-ID
            var frame = await RelayProtocol.ReadFrameAsync(stream, ct);
            if (frame == null || frame.Value.type != RelayProtocol.TypeControl) return;

            var reg = RelayProtocol.ParseControl<RelayRegisterMsg>(frame.Value.payload);
            if (reg?.Cmd != RelayProtocol.CmdRegister || string.IsNullOrEmpty(reg.BlackID)) return;

            session = new RelaySession(reg.BlackID, client, stream, remoteIP);

            // Якщо вже є стара сесія — закрити її
            if (_sessions.TryRemove(reg.BlackID, out var old))
            {
                old.CancellationSource.Cancel();
                WriteLog($"↩️  {reg.BlackID} перепідключився");
            }

            _sessions[reg.BlackID] = session;
            WriteLog($"📋 Зареєстровано: {reg.BlackID} ({remoteIP})");

            await RelayProtocol.WriteControlAsync(stream, new RelaySimpleMsg(RelayProtocol.CmdRegistered), ct);

            // Слухати команди від клієнта
            await ListenSessionAsync(session, ct);
        }
        catch (Exception ex)
        {
            if (!ct.IsCancellationRequested)
                WriteLog($"❌ {session?.BlackID ?? remoteIP}: {ex.Message}");
        }
        finally
        {
            if (session != null)
            {
                _sessions.TryRemove(session.BlackID, out _);
                WriteLog($"📤 Відключено: {session.BlackID}");
            }
            try { client.Close(); } catch { }
        }
    }

    private async Task ListenSessionAsync(RelaySession session, CancellationToken outerCt)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(outerCt, session.CancellationSource.Token);
        var ct = linked.Token;

        while (!ct.IsCancellationRequested)
        {
            var frame = await RelayProtocol.ReadFrameAsync(session.Stream, ct);
            if (frame == null) break;

            if (frame.Value.type == RelayProtocol.TypeControl)
            {
                var msg = RelayProtocol.ParseControlBase(frame.Value.payload);
                if (msg == null) continue;

                switch (msg.Cmd)
                {
                    case RelayProtocol.CmdConnect:
                        await HandleConnectAsync(session, frame.Value.payload, ct);
                        break;

                    case RelayProtocol.CmdAccept:
                        await HandleAcceptAsync(session, ct);
                        break;

                    case RelayProtocol.CmdReject:
                        await HandleRejectAsync(session, frame.Value.payload, ct);
                        break;

                    case RelayProtocol.CmdStunAnswer:
                        await HandleStunAnswerAsync(session, frame.Value.payload, ct);
                        break;

                    case RelayProtocol.CmdPing:
                        await RelayProtocol.WriteControlAsync(session.Stream, new RelaySimpleMsg(RelayProtocol.CmdPong), ct);
                        break;

                    case RelayProtocol.CmdDisconnect:
                        return;
                }
            }
            else if (frame.Value.type == RelayProtocol.TypeData)
            {
                // Пробросити дані партнеру
                if (session.Partner != null)
                {
                    try { await RelayProtocol.WriteDataAsync(session.Partner.Stream, frame.Value.payload, ct); }
                    catch { session.Partner = null; }
                }
            }
        }
    }

    private async Task HandleStunAnswerAsync(RelaySession sender, byte[] payload, CancellationToken ct)
    {
        var msg = RelayProtocol.ParseControl<RelayStunAnswerMsg>(payload);
        if (msg == null || string.IsNullOrEmpty(msg.To)) return;

        if (_sessions.TryGetValue(msg.To, out var target))
        {
            try { await RelayProtocol.WriteControlAsync(target.Stream, msg, ct); }
            catch { }
        }
    }

    private async Task HandleConnectAsync(RelaySession requester, byte[] payload, CancellationToken ct)
    {
        var msg = RelayProtocol.ParseControl<RelayConnectMsg>(payload);
        if (msg == null) return;

        WriteLog($"🔌 {requester.BlackID} → {msg.Target}");

        if (!_sessions.TryGetValue(msg.Target, out var target))
        {
            await RelayProtocol.WriteControlAsync(requester.Stream,
                new RelayRejectMsg { Reason = $"Вузол '{msg.Target}' не підключений до relay" }, ct);
            return;
        }

        // Зберігаємо pending request
        target.PendingRequester = requester;
        requester.PendingTarget = target;

        // Повідомити target про вхідний запит (включаємо STUN endpoint для hole punching)
        await RelayProtocol.WriteControlAsync(target.Stream, new RelayIncomingMsg
        {
            From         = requester.BlackID,
            FromIP       = requester.RemoteIP,
            StunEndpoint = msg.StunEndpoint
        }, ct);

        WriteLog($"📨 Надіслано запит підключення від {requester.BlackID} до {target.BlackID}");
    }

    private async Task HandleAcceptAsync(RelaySession accepter, CancellationToken ct)
    {
        var requester = accepter.PendingRequester;
        if (requester == null) return;

        // Зв'язати сесії
        accepter.Partner  = requester;
        requester.Partner = accepter;
        accepter.PendingRequester  = null;
        requester.PendingTarget    = null;

        // Повідомити requester що з'єднання прийнято
        await RelayProtocol.WriteControlAsync(requester.Stream, new RelaySimpleMsg(RelayProtocol.CmdAccepted), ct);
        WriteLog($"✅ З'єднано: {requester.BlackID} ↔ {accepter.BlackID}");
    }

    private async Task HandleRejectAsync(RelaySession rejecter, byte[] payload, CancellationToken ct)
    {
        var msg    = RelayProtocol.ParseControl<RelayRejectMsg>(payload);
        var requester = rejecter.PendingRequester;
        if (requester == null) return;

        rejecter.PendingRequester = null;
        requester.PendingTarget   = null;

        await RelayProtocol.WriteControlAsync(requester.Stream,
            new RelayRejectMsg { Reason = msg?.Reason ?? "Відхилено" }, ct);
        WriteLog($"❌ {rejecter.BlackID} відхилив запит від {requester.BlackID}");
    }

    private void WriteLog(string msg) => Log?.Invoke(this, msg);

    public void Dispose()
    {
        _cts?.Cancel();
        _listener?.Stop();
    }
}

internal class RelaySession(string blackID, TcpClient client, NetworkStream stream, string remoteIP)
{
    public string          BlackID     { get; } = blackID;
    public TcpClient       Client      { get; } = client;
    public NetworkStream   Stream      { get; } = stream;
    public string          RemoteIP    { get; } = remoteIP;
    public CancellationTokenSource CancellationSource { get; } = new();

    public RelaySession? Partner          { get; set; }
    public RelaySession? PendingRequester { get; set; }
    public RelaySession? PendingTarget    { get; set; }
}
