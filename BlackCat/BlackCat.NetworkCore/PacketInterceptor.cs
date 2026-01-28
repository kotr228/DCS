using System.Net;
using System.Net.Sockets;
using BlackCat.Shared.Models;
using BlackCat.Shared.Enums;

namespace BlackCat.NetworkCore;

/// <summary>
/// Перехоплювач мережевих пакетів
/// ПРИМІТКА: Для повноцінного перехоплення пакетів на рівні драйвера
/// потрібен WinPcap/Npcap або WFP (Windows Filtering Platform)
/// Ця реалізація працює на рівні application layer
/// </summary>
public class PacketInterceptor
{
    private bool _isRunning;
    private Socket? _rawSocket;

    public event EventHandler<PacketInfo>? PacketCaptured;
    public event EventHandler<string>? InterceptorError;

    /// <summary>
    /// Запустити перехоплення пакетів
    /// </summary>
    public void Start()
    {
        if (_isRunning)
            throw new InvalidOperationException("Interceptor вже запущено");

        try
        {
            // Створити raw socket для прослуховування IP пакетів
            // ПРИМІТКА: Вимагає прав адміністратора
            _rawSocket = new Socket(
                AddressFamily.InterNetwork,
                SocketType.Raw,
                System.Net.Sockets.ProtocolType.IP
            );

            // Прив'язати до локального інтерфейсу
            var localEndPoint = new IPEndPoint(GetLocalIPAddress(), 0);
            _rawSocket.Bind(localEndPoint);

            // Встановити IOControl для отримання всіх пакетів
            _rawSocket.SetSocketOption(
                SocketOptionLevel.IP,
                SocketOptionName.HeaderIncluded,
                true
            );

            byte[] byteTrue = new byte[4] { 1, 0, 0, 0 };
            byte[] byteOut = new byte[4];

            // SIO_RCVALL - отримувати всі IP пакети
            _rawSocket.IOControl(
                IOControlCode.ReceiveAll,
                byteTrue,
                byteOut
            );

            _isRunning = true;

            // Початок прийому пакетів
            BeginReceive();
        }
        catch (Exception ex)
        {
            InterceptorError?.Invoke(this, $"Помилка запуску: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Зупинити перехоплення
    /// </summary>
    public void Stop()
    {
        _isRunning = false;
        _rawSocket?.Close();
        _rawSocket = null;
    }

    /// <summary>
    /// Початок асинхронного прийому
    /// </summary>
    private void BeginReceive()
    {
        if (!_isRunning || _rawSocket == null) return;

        try
        {
            byte[] buffer = new byte[65535]; // Максимальний розмір IP пакету

            _rawSocket.BeginReceive(
                buffer,
                0,
                buffer.Length,
                SocketFlags.None,
                OnReceive,
                buffer
            );
        }
        catch (Exception ex)
        {
            InterceptorError?.Invoke(this, $"Помилка прийому: {ex.Message}");
        }
    }

    /// <summary>
    /// Callback при отриманні пакету
    /// </summary>
    private void OnReceive(IAsyncResult ar)
    {
        if (!_isRunning || _rawSocket == null) return;

        try
        {
            int bytesReceived = _rawSocket.EndReceive(ar);
            byte[] buffer = (byte[])ar.AsyncState!;

            if (bytesReceived > 0)
            {
                // Парсити IP пакет
                var packetInfo = ParseIPPacket(buffer, bytesReceived);
                if (packetInfo != null)
                {
                    PacketCaptured?.Invoke(this, packetInfo);
                }
            }

            // Продовжити прийом
            BeginReceive();
        }
        catch (ObjectDisposedException)
        {
            // Socket закрито
        }
        catch (Exception ex)
        {
            InterceptorError?.Invoke(this, $"Помилка обробки пакету: {ex.Message}");
            BeginReceive();
        }
    }

    /// <summary>
    /// Парсинг IP пакету
    /// </summary>
    private PacketInfo? ParseIPPacket(byte[] buffer, int length)
    {
        try
        {
            if (length < 20) return null; // Мінімальний розмір IP заголовка

            // IP заголовок (спрощений парсинг)
            byte versionAndHeaderLength = buffer[0];
            int headerLength = (versionAndHeaderLength & 0x0F) * 4;

            byte protocol = buffer[9];

            // Витягти IP адреси
            byte[] sourceIP = new byte[4];
            byte[] destIP = new byte[4];
            Array.Copy(buffer, 12, sourceIP, 0, 4);
            Array.Copy(buffer, 16, destIP, 0, 4);

            string sourceIPString = new IPAddress(sourceIP).ToString();
            string destIPString = new IPAddress(destIP).ToString();

            int sourcePort = 0;
            int destPort = 0;

            // Витягти порти для TCP/UDP
            if (protocol == 6 || protocol == 17) // TCP або UDP
            {
                if (length >= headerLength + 4)
                {
                    sourcePort = (buffer[headerLength] << 8) | buffer[headerLength + 1];
                    destPort = (buffer[headerLength + 2] << 8) | buffer[headerLength + 3];
                }
            }

            // Витягти payload
            int payloadStart = headerLength;
            if (protocol == 6 || protocol == 17)
            {
                // Для TCP/UDP пропустити їхні заголовки (спрощено)
                payloadStart += (protocol == 6) ? 20 : 8;
            }

            int payloadLength = length - payloadStart;
            byte[] payload = new byte[payloadLength > 0 ? payloadLength : 0];
            if (payloadLength > 0)
            {
                Array.Copy(buffer, payloadStart, payload, 0, payloadLength);
            }

            return new PacketInfo
            {
                SourceIP = sourceIPString,
                DestinationIP = destIPString,
                SourcePort = sourcePort,
                DestinationPort = destPort,
                Protocol = (BlackCat.Shared.Models.ProtocolType)protocol,
                Payload = payload,
                Timestamp = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            InterceptorError?.Invoke(this, $"Помилка парсингу: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Отримати локальну IP адресу
    /// </summary>
    private IPAddress GetLocalIPAddress()
    {
        var host = Dns.GetHostEntry(Dns.GetHostName());
        foreach (var ip in host.AddressList)
        {
            if (ip.AddressFamily == AddressFamily.InterNetwork)
            {
                return ip;
            }
        }

        return IPAddress.Loopback;
    }
}
