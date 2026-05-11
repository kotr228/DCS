using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Xml.Linq;

namespace BlackCat.Core.Services;

/// <summary>
/// Менеджер для автоматичного проходження NAT (UPnP) - власна реалізація
/// </summary>
public class NatManager : IDisposable
{
    private readonly int _internalPort;
    private readonly int _externalPort;
    private readonly string _description;
    private string? _controlUrl;
    private string? _serviceType;
    private bool _isPortOpen = false;

    public event EventHandler<NatStatusEventArgs>? StatusChanged;

    public bool IsPortForwardingActive => _isPortOpen;
    public string? ExternalIP { get; private set; }

    public NatManager(int port = 9999, string description = "BlackCat Secure Tunnel")
    {
        _internalPort = port;
        _externalPort = port;
        _description = description;
    }

    /// <summary>
    /// Спробувати автоматично відкрити порт через UPnP
    /// </summary>
    public async Task<bool> TryOpenPortAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // 1. Знайти роутер через SSDP
            var deviceUrl = await DiscoverRouterAsync(cancellationToken);
            if (deviceUrl == null)
            {
                StatusChanged?.Invoke(this, new NatStatusEventArgs
                {
                    IsSuccess = false,
                    Method = "UPnP",
                    Message = "UPnP не підтримується роутером"
                });
                return false;
            }

            Console.WriteLine($"✅ Знайдено UPnP пристрій: {deviceUrl}");

            // 2. Отримати Service Control URL
            await GetServiceInfoAsync(deviceUrl, cancellationToken);

            if (_controlUrl == null || _serviceType == null)
            {
                Console.WriteLine("⚠️ Не вдалося отримати інформацію про сервіс");
                return false;
            }

            // 3. Отримати зовнішню IP
            ExternalIP = await GetExternalIPAsync(cancellationToken);
            Console.WriteLine($"🌐 Зовнішня IP: {ExternalIP}");

            // 4. Відкрити порт
            bool success = await AddPortMappingAsync(cancellationToken);

            if (success)
            {
                _isPortOpen = true;
                Console.WriteLine($"✅ Порт {_externalPort} автоматично відкрито через UPnP!");
                Console.WriteLine($"💡 Інші користувачі можуть підключатися до вас БЕЗ налаштування роутера!");

                StatusChanged?.Invoke(this, new NatStatusEventArgs
                {
                    IsSuccess = true,
                    Method = "UPnP",
                    ExternalIP = ExternalIP,
                    ExternalPort = _externalPort,
                    Message = "Порт автоматично відкрито через UPnP"
                });
            }

            return success;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Помилка UPnP: {ex.Message}");
            StatusChanged?.Invoke(this, new NatStatusEventArgs
            {
                IsSuccess = false,
                Method = "UPnP",
                Message = $"Помилка: {ex.Message}"
            });
            return false;
        }
    }

    /// <summary>
    /// Знайти роутер через SSDP (Simple Service Discovery Protocol)
    /// </summary>
    private async Task<string?> DiscoverRouterAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var client = new UdpClient();
            client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

            var multicastAddress = IPAddress.Parse("239.255.255.250");
            var multicastEndpoint = new IPEndPoint(multicastAddress, 1900);

            // SSDP M-SEARCH запит
            string searchMessage =
                "M-SEARCH * HTTP/1.1\r\n" +
                "HOST: 239.255.255.250:1900\r\n" +
                "ST: urn:schemas-upnp-org:device:InternetGatewayDevice:1\r\n" +
                "MAN: \"ssdp:discover\"\r\n" +
                "MX: 3\r\n" +
                "\r\n";

            byte[] searchBytes = Encoding.ASCII.GetBytes(searchMessage);
            await client.SendAsync(searchBytes, searchBytes.Length, multicastEndpoint);

            // Чекати відповідь (2 секунди — щоб не блокувати запуск)
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(2));

            try
            {
                var result = await client.ReceiveAsync(cts.Token);
                string response = Encoding.ASCII.GetString(result.Buffer);

                // Знайти LOCATION header
                var lines = response.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    if (line.StartsWith("LOCATION:", StringComparison.OrdinalIgnoreCase))
                    {
                        return line.Substring(9).Trim();
                    }
                }
            }
            catch (OperationCanceledException)
            {
                return null;
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Отримати інформацію про UPnP сервіс з device descriptor
    /// </summary>
    private async Task GetServiceInfoAsync(string deviceUrl, CancellationToken cancellationToken)
    {
        try
        {
            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(5);

            var xml = await httpClient.GetStringAsync(deviceUrl, cancellationToken);
            var doc = XDocument.Parse(xml);

            XNamespace ns = "urn:schemas-upnp-org:device-1-0";

            // Знайти WANIPConnection або WANPPPConnection сервіс
            var services = doc.Descendants(ns + "service");

            foreach (var service in services)
            {
                var serviceType = service.Element(ns + "serviceType")?.Value;

                if (serviceType?.Contains("WANIPConnection") == true ||
                    serviceType?.Contains("WANPPPConnection") == true)
                {
                    _serviceType = serviceType;
                    var controlUrl = service.Element(ns + "controlURL")?.Value;

                    if (controlUrl != null)
                    {
                        // Побудувати повний URL
                        var baseUri = new Uri(deviceUrl);
                        _controlUrl = new Uri(baseUri, controlUrl).ToString();
                        return;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️ Помилка отримання service info: {ex.Message}");
        }
    }

    /// <summary>
    /// Отримати зовнішню IP адресу через UPnP
    /// </summary>
    private async Task<string?> GetExternalIPAsync(CancellationToken cancellationToken)
    {
        try
        {
            string soapAction = "GetExternalIPAddress";
            string soapBody = $@"<?xml version=""1.0""?>
<s:Envelope xmlns:s=""http://schemas.xmlsoap.org/soap/envelope/"" s:encodingStyle=""http://schemas.xmlsoap.org/soap/encoding/"">
<s:Body>
<u:{soapAction} xmlns:u=""{_serviceType}""/>
</s:Body>
</s:Envelope>";

            var response = await SendSoapRequestAsync(soapAction, soapBody, cancellationToken);

            if (response != null)
            {
                var doc = XDocument.Parse(response);
                var ip = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "NewExternalIPAddress")?.Value;
                return ip;
            }
        }
        catch { }

        return null;
    }

    /// <summary>
    /// Додати port mapping через UPnP
    /// </summary>
    private async Task<bool> AddPortMappingAsync(CancellationToken cancellationToken)
    {
        try
        {
            string localIP = GetLocalIPAddress();
            string soapAction = "AddPortMapping";

            string soapBody = $@"<?xml version=""1.0""?>
<s:Envelope xmlns:s=""http://schemas.xmlsoap.org/soap/envelope/"" s:encodingStyle=""http://schemas.xmlsoap.org/soap/encoding/"">
<s:Body>
<u:{soapAction} xmlns:u=""{_serviceType}"">
<NewRemoteHost></NewRemoteHost>
<NewExternalPort>{_externalPort}</NewExternalPort>
<NewProtocol>TCP</NewProtocol>
<NewInternalPort>{_internalPort}</NewInternalPort>
<NewInternalClient>{localIP}</NewInternalClient>
<NewEnabled>1</NewEnabled>
<NewPortMappingDescription>{_description}</NewPortMappingDescription>
<NewLeaseDuration>0</NewLeaseDuration>
</u:{soapAction}>
</s:Body>
</s:Envelope>";

            var response = await SendSoapRequestAsync(soapAction, soapBody, cancellationToken);
            return response != null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️ Помилка відкриття порту: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Видалити port mapping
    /// </summary>
    public async Task ClosePortAsync()
    {
        if (!_isPortOpen || _controlUrl == null || _serviceType == null)
            return;

        try
        {
            string soapAction = "DeletePortMapping";
            string soapBody = $@"<?xml version=""1.0""?>
<s:Envelope xmlns:s=""http://schemas.xmlsoap.org/soap/envelope/"" s:encodingStyle=""http://schemas.xmlsoap.org/soap/encoding/"">
<s:Body>
<u:{soapAction} xmlns:u=""{_serviceType}"">
<NewRemoteHost></NewRemoteHost>
<NewExternalPort>{_externalPort}</NewExternalPort>
<NewProtocol>TCP</NewProtocol>
</u:{soapAction}>
</s:Body>
</s:Envelope>";

            await SendSoapRequestAsync(soapAction, soapBody, CancellationToken.None);
            _isPortOpen = false;

            Console.WriteLine($"✅ Порт {_externalPort} закрито");
        }
        catch { }
    }

    /// <summary>
    /// Відправити SOAP запит до роутера
    /// </summary>
    private async Task<string?> SendSoapRequestAsync(string action, string body, CancellationToken cancellationToken)
    {
        if (_controlUrl == null || _serviceType == null)
            return null;

        try
        {
            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(10);

            var request = new HttpRequestMessage(HttpMethod.Post, _controlUrl);
            request.Headers.Add("SOAPAction", $"\"{_serviceType}#{action}\"");
            request.Content = new StringContent(body, Encoding.UTF8, "text/xml");

            var response = await httpClient.SendAsync(request, cancellationToken);
            return await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Отримати локальну IP адресу
    /// </summary>
    private string GetLocalIPAddress()
    {
        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0);
            socket.Connect("8.8.8.8", 65530);
            var endPoint = socket.LocalEndPoint as IPEndPoint;
            return endPoint?.Address.ToString() ?? "127.0.0.1";
        }
        catch
        {
            return "127.0.0.1";
        }
    }

    public Task<bool> CheckPortStatusAsync()
    {
        return Task.FromResult(_isPortOpen);
    }

    public void Dispose()
    {
        try
        {
            ClosePortAsync().Wait(TimeSpan.FromSeconds(5));
        }
        catch { }
    }
}

/// <summary>
/// Події про стан NAT
/// </summary>
public class NatStatusEventArgs : EventArgs
{
    public bool IsSuccess { get; set; }
    public string Method { get; set; } = string.Empty;
    public string? ExternalIP { get; set; }
    public int ExternalPort { get; set; }
    public string Message { get; set; } = string.Empty;
}
