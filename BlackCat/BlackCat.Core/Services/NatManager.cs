using Open.NAT;

namespace BlackCat.Core.Services;

/// <summary>
/// Менеджер для автоматичного проходження NAT (UPnP)
/// </summary>
public class NatManager : IDisposable
{
    private NatDevice? _device;
    private Mapping? _currentMapping;
    private readonly int _internalPort;
    private readonly int _externalPort;
    private readonly string _description;
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
            Console.WriteLine("🔍 Пошук UPnP пристроїв (роутер)...");

            // Знайти NAT пристрій (роутер з UPnP)
            var discoverer = new NatDiscoverer();
            _device = await discoverer.DiscoverDeviceAsync(PortMapper.Upnp | PortMapper.Pmp, cancellationToken);

            Console.WriteLine($"✅ Знайдено роутер: {_device.GetType().Name}");

            // Отримати зовнішню IP адресу
            ExternalIP = (await _device.GetExternalIPAsync()).ToString();
            Console.WriteLine($"🌐 Зовнішня IP: {ExternalIP}");

            // Створити маппінг порту
            _currentMapping = new Mapping(
                Protocol.Tcp,
                _internalPort,
                _externalPort,
                description: _description
            );

            // Відкрити порт
            await _device.CreatePortMapAsync(_currentMapping);

            _isPortOpen = true;

            Console.WriteLine($"✅ Порт {_externalPort} автоматично відкрито через UPnP!");
            Console.WriteLine($"   Внутрішній порт: {_internalPort}");
            Console.WriteLine($"   Зовнішній порт: {_externalPort}");

            StatusChanged?.Invoke(this, new NatStatusEventArgs
            {
                IsSuccess = true,
                Method = "UPnP",
                ExternalIP = ExternalIP,
                ExternalPort = _externalPort,
                Message = "Порт автоматично відкрито через UPnP"
            });

            return true;
        }
        catch (NatDeviceNotFoundException)
        {
            Console.WriteLine("⚠️ UPnP не знайдено - роутер не підтримує UPnP або він вимкнено");

            StatusChanged?.Invoke(this, new NatStatusEventArgs
            {
                IsSuccess = false,
                Method = "UPnP",
                Message = "UPnP недоступний на роутері"
            });

            return false;
        }
        catch (MappingException ex)
        {
            Console.WriteLine($"⚠️ Не вдалося створити маппінг порту: {ex.Message}");

            StatusChanged?.Invoke(this, new NatStatusEventArgs
            {
                IsSuccess = false,
                Method = "UPnP",
                Message = $"Помилка маппінгу: {ex.Message}"
            });

            return false;
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
    /// Закрити порт (видалити маппінг)
    /// </summary>
    public async Task ClosePortAsync()
    {
        if (_device == null || _currentMapping == null || !_isPortOpen)
            return;

        try
        {
            await _device.DeletePortMapAsync(_currentMapping);
            _isPortOpen = false;

            Console.WriteLine($"✅ Порт {_externalPort} закрито");

            StatusChanged?.Invoke(this, new NatStatusEventArgs
            {
                IsSuccess = true,
                Method = "UPnP",
                Message = "Порт закрито"
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️ Помилка закриття порту: {ex.Message}");
        }
    }

    /// <summary>
    /// Перевірити чи порт все ще відкритий
    /// </summary>
    public async Task<bool> CheckPortStatusAsync()
    {
        if (_device == null || _currentMapping == null)
            return false;

        try
        {
            var mappings = await _device.GetAllMappingsAsync();
            return mappings.Any(m => m.PublicPort == _externalPort);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Отримати всі активні маппінги на роутері
    /// </summary>
    public async Task<IEnumerable<Mapping>> GetAllMappingsAsync()
    {
        if (_device == null)
            return Enumerable.Empty<Mapping>();

        try
        {
            return await _device.GetAllMappingsAsync();
        }
        catch
        {
            return Enumerable.Empty<Mapping>();
        }
    }

    public void Dispose()
    {
        // Закрити порт при dispose
        try
        {
            ClosePortAsync().Wait(TimeSpan.FromSeconds(5));
        }
        catch
        {
            // Ignore cleanup errors
        }
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
