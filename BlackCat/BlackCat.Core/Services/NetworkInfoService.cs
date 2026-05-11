using System.Net;
using System.Net.Sockets;
using System.Net.NetworkInformation;

namespace BlackCat.Core.Services;

/// <summary>
/// Сервіс для отримання мережевої інформації про пристрій
/// </summary>
public class NetworkInfoService
{
    /// <summary>
    /// Отримати локальну IP адресу
    /// </summary>
    public string GetLocalIPAddress()
    {
        try
        {
            using (Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0))
            {
                socket.Connect("8.8.8.8", 65530);
                IPEndPoint? endPoint = socket.LocalEndPoint as IPEndPoint;
                return endPoint?.Address.ToString() ?? "Невідомо";
            }
        }
        catch
        {
            // Альтернативний спосіб
            try
            {
                var host = Dns.GetHostEntry(Dns.GetHostName());
                foreach (var ip in host.AddressList)
                {
                    if (ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
                    {
                        return ip.ToString();
                    }
                }
            }
            catch { }

            return "Невідомо";
        }
    }

    /// <summary>
    /// Отримати всі локальні IP адреси
    /// </summary>
    public List<string> GetAllLocalIPAddresses()
    {
        var addresses = new List<string>();

        try
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
                {
                    addresses.Add(ip.ToString());
                }
            }
        }
        catch { }

        return addresses;
    }

    /// <summary>
    /// Отримати зовнішню (публічну) IP адресу
    /// </summary>
    public async Task<string> GetExternalIPAddressAsync()
    {
        try
        {
            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(5);

            // Спробувати кілька сервісів для надійності
            var services = new[]
            {
                "https://api.ipify.org",
                "https://icanhazip.com",
                "https://ifconfig.me/ip",
                "https://checkip.amazonaws.com"
            };

            foreach (var service in services)
            {
                try
                {
                    var response = await client.GetStringAsync(service);
                    var ip = response.Trim();

                    // Перевірити чи це валідна IP адреса
                    if (IPAddress.TryParse(ip, out _))
                    {
                        return ip;
                    }
                }
                catch
                {
                    continue;
                }
            }

            return "Недоступно";
        }
        catch
        {
            return "Недоступно";
        }
    }

    /// <summary>
    /// Отримати MAC адресу основного адаптера
    /// </summary>
    public string GetPrimaryMacAddress()
    {
        try
        {
            var nics = NetworkInterface.GetAllNetworkInterfaces()
                .Where(nic => nic.OperationalStatus == OperationalStatus.Up
                           && nic.NetworkInterfaceType != NetworkInterfaceType.Loopback
                           && nic.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
                .OrderByDescending(nic => nic.Speed)
                .ToList();

            if (nics.Any())
            {
                return string.Join(":", nics[0].GetPhysicalAddress().GetAddressBytes().Select(b => b.ToString("X2")));
            }
        }
        catch { }

        return "Невідомо";
    }

    /// <summary>
    /// Перевірити чи порт відкритий
    /// </summary>
    public bool IsPortOpen(int port)
    {
        try
        {
            var listener = new TcpListener(IPAddress.Any, port);
            listener.Start();
            listener.Stop();
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Отримати повну інформацію про мережу
    /// </summary>
    public async Task<NetworkInfo> GetNetworkInfoAsync()
    {
        var localIPs = GetAllLocalIPAddresses();
        var externalIP = await GetExternalIPAddressAsync();

        return new NetworkInfo
        {
            LocalIPAddress = GetLocalIPAddress(),
            AllLocalIPAddresses = localIPs,
            ExternalIPAddress = externalIP,
            MacAddress = GetPrimaryMacAddress(),
            HostName = Dns.GetHostName()
        };
    }
}

/// <summary>
/// Інформація про мережу
/// </summary>
public class NetworkInfo
{
    public string LocalIPAddress { get; set; } = string.Empty;
    public List<string> AllLocalIPAddresses { get; set; } = new();
    public string ExternalIPAddress { get; set; } = string.Empty;
    public string MacAddress { get; set; } = string.Empty;
    public string HostName { get; set; } = string.Empty;
}
