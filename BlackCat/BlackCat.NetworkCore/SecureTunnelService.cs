using System.Net;
using System.Net.Sockets;
using BlackCat.Crypto;
using BlackCat.Shared.Models;
using BlackCat.Shared.Enums;

namespace BlackCat.NetworkCore;

/// <summary>
/// Захищений тунель з кватерніонним шифруванням
/// </summary>
public class SecureTunnelService : IDisposable
{
    private readonly MQECryptoService _cryptoService;
    private readonly int _listenPort;
    private TcpListener? _listener;
    private readonly List<TcpClient> _connectedClients = new();
    private bool _isRunning;
    private readonly object _lockObject = new();

    public event EventHandler<TunnelPacket>? PacketReceived;
    public event EventHandler<string>? TunnelError;
    public event EventHandler<TunnelStatusEventArgs>? StatusChanged;

    public TunnelStatus CurrentStatus { get; private set; } = TunnelStatus.Disconnected;

    public SecureTunnelService(string masterSecret, int listenPort = 9999)
    {
        _cryptoService = new MQECryptoService(masterSecret);
        _listenPort = listenPort;
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
            _listener = new TcpListener(IPAddress.Any, _listenPort);
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
            catch (Exception ex)
            {
                TunnelError?.Invoke(this, $"Помилка прийому з'єднання: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Обробка клієнта
    /// </summary>
    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        try
        {
            using (client)
            {
                NetworkStream stream = client.GetStream();

                while (_isRunning && !cancellationToken.IsCancellationRequested && client.Connected)
                {
                    // Прочитати розмір пакету (4 байти)
                    byte[] sizeBuffer = new byte[4];
                    int bytesRead = await stream.ReadAsync(sizeBuffer, 0, 4, cancellationToken);

                    if (bytesRead == 0) break; // Клієнт відключився

                    int packetSize = BitConverter.ToInt32(sizeBuffer, 0);

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
            TunnelError?.Invoke(this, $"Помилка обробки клієнта: {ex.Message}");
        }
        finally
        {
            lock (_lockObject)
            {
                _connectedClients.Remove(client);
            }
        }
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
