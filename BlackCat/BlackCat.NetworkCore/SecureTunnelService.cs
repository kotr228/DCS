using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using BlackCat.Crypto;
using BlackCat.Shared.Models;
using BlackCat.Shared.Enums;

namespace BlackCat.NetworkCore;

/// <summary>
/// Захищений тунель з кватерніонним шифруванням та Black-ID автентифікацією
/// </summary>
public class SecureTunnelService : IDisposable
{
    private readonly MQECryptoService _cryptoService;
    private readonly int _listenPort;
    private TcpListener? _listener;
    private readonly List<TcpClient> _connectedClients = new();
    private bool _isRunning;
    private readonly object _lockObject = new();

    // Black-ID та Stealth Mode
    private BlackID? _ourBlackID;
    private Func<HelloMessage, string, ChallengeMessage>? _handleHello;
    private Func<ResponseMessage, string, HandshakeMessage>? _handleResponse;
    private bool _stealthMode = true; // За замовчуванням увімкнено
    private readonly TimeSpan _handshakeTimeout = TimeSpan.FromSeconds(90); // 60с на popup + 30с буфер

    public event EventHandler<TunnelPacket>? PacketReceived;
    public event EventHandler<string>? TunnelError;
    public event EventHandler<TunnelStatusEventArgs>? StatusChanged;
    public event EventHandler<string>? AuthenticationFailed;

    /// <summary>
    /// Спрацьовує коли хтось намагається підключитися.
    /// Встановіть ApprovalSource.SetResult(true) щоб прийняти або (false) щоб відхилити.
    /// </summary>
    public event EventHandler<IncomingConnectionRequestEventArgs>? IncomingConnectionRequest;

    public TunnelStatus CurrentStatus { get; private set; } = TunnelStatus.Disconnected;

    /// <summary>
    /// Чи увімкнено Stealth Mode (не відповідати на невалідні з'єднання)
    /// </summary>
    public bool StealthMode
    {
        get => _stealthMode;
        set => _stealthMode = value;
    }

    public SecureTunnelService(string masterSecret, int listenPort = 9999)
    {
        _cryptoService = new MQECryptoService(masterSecret);
        _listenPort = listenPort;
    }

    /// <summary>
    /// Налаштувати Black-ID автентифікацію
    /// </summary>
    public void ConfigureBlackID(
        BlackID ourBlackID,
        Func<HelloMessage, string, ChallengeMessage> handleHello,
        Func<ResponseMessage, string, HandshakeMessage> handleResponse)
    {
        _ourBlackID = ourBlackID;
        _handleHello = handleHello;
        _handleResponse = handleResponse;
    }

    /// <summary>
    /// Запустити тунель (слухати вхідні з'єднання)
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_isRunning)
            throw new InvalidOperationException("Тунель вже запущено");

        try
        {
            // Зупинити попередній listener якщо є
            if (_listener != null)
            {
                try
                {
                    _listener.Stop();
                }
                catch { }
                _listener = null;
            }

            _listener = new TcpListener(IPAddress.Any, _listenPort);

            // Дозволити повторне використання адреси (для швидкого перезапуску)
            _listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

            _listener.Start();
            _isRunning = true;

            UpdateStatus(TunnelStatus.Connected);

            // Прийом з'єднань у фоновому режимі
            _ = Task.Run(() => AcceptClientsAsync(cancellationToken), cancellationToken);
        }
        catch (Exception ex)
        {
            UpdateStatus(TunnelStatus.Error);
            TunnelError?.Invoke(this, $"Помилка запуску тунелю: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Зупинити тунель
    /// </summary>
    public void Stop()
    {
        _isRunning = false;
        _listener?.Stop();

        lock (_lockObject)
        {
            foreach (var client in _connectedClients)
            {
                client?.Close();
            }
            _connectedClients.Clear();
        }

        UpdateStatus(TunnelStatus.Disconnected);
    }

    /// <summary>
    /// Відправити дані через тунель (зашифровані)
    /// </summary>
    public async Task<bool> SendAsync(byte[] data, string remoteIP, int remotePort, CancellationToken cancellationToken = default)
    {
        try
        {
            // Підключитися до віддаленого вузла
            using var client = new TcpClient();
            await client.ConnectAsync(remoteIP, remotePort, cancellationToken);

            // Зашифрувати дані
            var localIP = ((IPEndPoint)client.Client.LocalEndPoint!).Address.ToString();
            var tunnelPacket = _cryptoService.Encrypt(data, localIP, remoteIP);

            // Серіалізувати пакет
            byte[] packetBytes = tunnelPacket.ToBytes();

            // Відправити
            NetworkStream stream = client.GetStream();
            await stream.WriteAsync(BitConverter.GetBytes(packetBytes.Length), cancellationToken);
            await stream.WriteAsync(packetBytes, cancellationToken);
            await stream.FlushAsync(cancellationToken);

            return true;
        }
        catch (Exception ex)
        {
            TunnelError?.Invoke(this, $"Помилка відправки: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Прийом вхідних з'єднань
    /// </summary>
    private async Task AcceptClientsAsync(CancellationToken cancellationToken)
    {
        while (_isRunning && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (_listener == null) break;

                var client = await _listener.AcceptTcpClientAsync(cancellationToken);

                lock (_lockObject)
                {
                    _connectedClients.Add(client);
                }

                // Обробити клієнта в окремому завданні
                _ = Task.Run(() => HandleClientAsync(client, cancellationToken), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                // Listener закрито — нормальна зупинка
                break;
            }
            catch (Exception ex) when (!_isRunning || ex.Message.Contains("aborted") || ex.Message.Contains("WSACancelBlockingCall"))
            {
                // Помилка через зупинку сервера — не логувати
                break;
            }
            catch (Exception ex)
            {
                TunnelError?.Invoke(this, $"Помилка прийому з'єднання: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Обробка клієнта з Black-ID handshake
    /// </summary>
    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        var remoteIP = ((IPEndPoint)client.Client.RemoteEndPoint!).Address.ToString();
        bool isAuthenticated = false;
        string? remoteBlackID = null;

        try
        {
            using (client)
            {
                NetworkStream stream = client.GetStream();

                // Якщо налаштовано Black-ID, виконати handshake
                if (_ourBlackID != null && _handleHello != null && _handleResponse != null)
                {
                    try
                    {
                        // Встановити таймаут для handshake
                        using var cts = new CancellationTokenSource(_handshakeTimeout);
                        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, cts.Token);

                        isAuthenticated = await PerformServerHandshakeAsync(stream, remoteIP, linked.Token);

                        if (!isAuthenticated)
                        {
                            // Stealth Mode - просто закрити з'єднання без відповіді
                            if (_stealthMode)
                            {
                                // Закрити тихо
                                return;
                            }
                            else
                            {
                                // Відправити Reject message
                                var reject = new RejectMessage { Reason = "Authentication failed" };
                                await SendHandshakeMessageAsync(stream, reject, linked.Token);
                                return;
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // Timeout - Stealth Mode
                        if (_stealthMode)
                        {
                            // Закрити тихо без логування
                            return;
                        }

                        AuthenticationFailed?.Invoke(this, $"Handshake timeout від {remoteIP}");
                        return;
                    }
                    catch (Exception ex)
                    {
                        // Помилка handshake
                        if (!_stealthMode)
                        {
                            TunnelError?.Invoke(this, $"Помилка handshake від {remoteIP}: {ex.Message}");
                        }
                        return;
                    }
                }

                // Handshake успішний або не налаштований - обробляти дані
                while (_isRunning && !cancellationToken.IsCancellationRequested && client.Connected)
                {
                    // Прочитати розмір пакету (4 байти)
                    byte[] sizeBuffer = new byte[4];
                    int bytesRead = await stream.ReadAsync(sizeBuffer, 0, 4, cancellationToken);

                    if (bytesRead == 0) break; // Клієнт відключився

                    int packetSize = BitConverter.ToInt32(sizeBuffer, 0);

                    // Перевірка розміру пакету (захист від атак)
                    if (packetSize <= 0 || packetSize > 10 * 1024 * 1024) // Максимум 10 MB
                    {
                        TunnelError?.Invoke(this, $"Невалідний розмір пакету: {packetSize}");
                        break;
                    }

                    // Прочитати пакет
                    byte[] packetBuffer = new byte[packetSize];
                    int totalRead = 0;

                    while (totalRead < packetSize)
                    {
                        bytesRead = await stream.ReadAsync(
                            packetBuffer,
                            totalRead,
                            packetSize - totalRead,
                            cancellationToken
                        );

                        if (bytesRead == 0) break;
                        totalRead += bytesRead;
                    }

                    if (totalRead != packetSize)
                    {
                        TunnelError?.Invoke(this, "Неповний пакет отримано");
                        continue;
                    }

                    // Десеріалізувати пакет
                    var tunnelPacket = TunnelPacket.FromBytes(packetBuffer);

                    // Розшифрувати пакет
                    try
                    {
                        byte[] decryptedData = _cryptoService.Decrypt(tunnelPacket);

                        // Викликати подію
                        PacketReceived?.Invoke(this, tunnelPacket);
                    }
                    catch (Exception ex)
                    {
                        TunnelError?.Invoke(this, $"Помилка розшифрування: {ex.Message}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            if (!_stealthMode || isAuthenticated)
            {
                TunnelError?.Invoke(this, $"Помилка обробки клієнта {remoteIP}: {ex.Message}");
            }
        }
        finally
        {
            lock (_lockObject)
            {
                _connectedClients.Remove(client);
            }
        }
    }

    /// <summary>
    /// Виконати server-side handshake
    /// </summary>
    private async Task<bool> PerformServerHandshakeAsync(NetworkStream stream, string remoteIP, CancellationToken cancellationToken)
    {
        // 1. Читати Hello
        var hello = await ReceiveHandshakeMessageAsync<HelloMessage>(stream, cancellationToken);
        if (hello == null || !BlackID.IsValid(hello.BlackID))
        {
            return false;
        }

        // 1b. Показати popup прийняти/відхилити якщо є підписник на подію.
        //     Якщо підписника немає (UI не підключений) — дозволяємо автоматично.
        if (IncomingConnectionRequest != null)
        {
            var approvalSource = new TaskCompletionSource<bool>();
            var args = new IncomingConnectionRequestEventArgs
            {
                PeerBlackID = hello.BlackID,
                PeerIP = remoteIP,
                ApprovalSource = approvalSource
            };

            IncomingConnectionRequest.Invoke(this, args);

            using var approvalCts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            approvalCts.Token.Register(() => approvalSource.TrySetResult(false));

            bool approved = await approvalSource.Task;
            if (!approved)
            {
                Console.WriteLine($"❌ З'єднання від {hello.BlackID} ({remoteIP}) відхилено користувачем");
                return false;
            }
        }

        // 2. Відправити Challenge
        var challenge = _handleHello!(hello, remoteIP);
        await SendHandshakeMessageAsync(stream, challenge, cancellationToken);

        // 3. Читати Response
        var response = await ReceiveHandshakeMessageAsync<ResponseMessage>(stream, cancellationToken);
        if (response == null)
        {
            return false;
        }

        // 4. Перевірити response та відправити Accept/Reject
        var result = _handleResponse!(response, remoteIP);

        if (result is AcceptMessage)
        {
            await SendHandshakeMessageAsync(stream, result, cancellationToken);
            return true;
        }
        else
        {
            if (!_stealthMode)
            {
                await SendHandshakeMessageAsync(stream, result, cancellationToken);
            }
            return false;
        }
    }

    /// <summary>
    /// Відправити handshake повідомлення
    /// </summary>
    private async Task SendHandshakeMessageAsync(NetworkStream stream, HandshakeMessage message, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(message, message.GetType());
        var data = Encoding.UTF8.GetBytes(json);

        // Відправити розмір + дані
        await stream.WriteAsync(BitConverter.GetBytes(data.Length), cancellationToken);
        await stream.WriteAsync(data, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    /// <summary>
    /// Отримати handshake повідомлення
    /// </summary>
    private async Task<T?> ReceiveHandshakeMessageAsync<T>(NetworkStream stream, CancellationToken cancellationToken) where T : HandshakeMessage
    {
        // Читати розмір
        byte[] sizeBuffer = new byte[4];
        int bytesRead = await stream.ReadAsync(sizeBuffer, 0, 4, cancellationToken);
        if (bytesRead != 4) return null;

        int size = BitConverter.ToInt32(sizeBuffer, 0);
        if (size <= 0 || size > 1024 * 1024) return null; // Максимум 1 MB для handshake

        // Читати дані
        byte[] data = new byte[size];
        int totalRead = 0;
        while (totalRead < size)
        {
            bytesRead = await stream.ReadAsync(data, totalRead, size - totalRead, cancellationToken);
            if (bytesRead == 0) return null;
            totalRead += bytesRead;
        }

        // Десеріалізувати
        var json = Encoding.UTF8.GetString(data);
        return JsonSerializer.Deserialize<T>(json);
    }

    private void UpdateStatus(TunnelStatus status)
    {
        CurrentStatus = status;
        StatusChanged?.Invoke(this, new TunnelStatusEventArgs(status));
    }

    public void Dispose()
    {
        Stop();
        _listener?.Stop();
    }
}

public class TunnelStatusEventArgs : EventArgs
{
    public TunnelStatus Status { get; }

    public TunnelStatusEventArgs(TunnelStatus status)
    {
        Status = status;
    }
}

/// <summary>
/// Аргументи події вхідного запиту підключення.
/// Встановіть ApprovalSource.SetResult(true) для прийняття або (false) для відхилення.
/// </summary>
public class IncomingConnectionRequestEventArgs : EventArgs
{
    public string PeerBlackID { get; set; } = string.Empty;
    public string PeerIP { get; set; } = string.Empty;
    public TaskCompletionSource<bool> ApprovalSource { get; set; } = new();
}
