using System.Management;
using System.Security.Cryptography;
using System.Text;

namespace BlackCat.Core.Services;

/// <summary>
/// Сервіс для генерації hardware fingerprint
/// Використовує WMI для отримання інформації про залізо
/// </summary>
public class HardwareFingerprintService
{
    /// <summary>
    /// Отримати hardware fingerprint поточного пристрою
    /// </summary>
    public string GetFingerprint()
    {
        try
        {
            var components = new StringBuilder();

            // CPU ID
            components.Append(GetCpuId());
            components.Append('|');

            // Motherboard Serial
            components.Append(GetMotherboardSerial());
            components.Append('|');

            // MAC Address
            components.Append(GetMacAddress());

            // Хешувати для компактності
            using var sha256 = SHA256.Create();
            var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(components.ToString()));
            return Convert.ToBase64String(hash);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Не вдалося отримати hardware fingerprint: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Перевірити чи fingerprint співпадає
    /// </summary>
    public bool ValidateFingerprint(string storedFingerprint)
    {
        try
        {
            var currentFingerprint = GetFingerprint();
            return currentFingerprint == storedFingerprint;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Отримати інформацію про процесор
    /// </summary>
    private string GetCpuId()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT ProcessorId FROM Win32_Processor");
            foreach (ManagementObject obj in searcher.Get())
            {
                return obj["ProcessorId"]?.ToString() ?? "UNKNOWN_CPU";
            }
        }
        catch
        {
            // Fallback
        }

        return "UNKNOWN_CPU";
    }

    /// <summary>
    /// Отримати серійний номер материнської плати
    /// </summary>
    private string GetMotherboardSerial()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT SerialNumber FROM Win32_BaseBoard");
            foreach (ManagementObject obj in searcher.Get())
            {
                var serial = obj["SerialNumber"]?.ToString();
                if (!string.IsNullOrWhiteSpace(serial))
                    return serial;
            }
        }
        catch
        {
            // Fallback
        }

        return "UNKNOWN_MOBO";
    }

    /// <summary>
    /// Отримати MAC адресу першого активного адаптера
    /// </summary>
    private string GetMacAddress()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT MACAddress FROM Win32_NetworkAdapterConfiguration WHERE IPEnabled = True");
            foreach (ManagementObject obj in searcher.Get())
            {
                var mac = obj["MACAddress"]?.ToString();
                if (!string.IsNullOrWhiteSpace(mac))
                    return mac;
            }
        }
        catch
        {
            // Fallback
        }

        return "UNKNOWN_MAC";
    }

    /// <summary>
    /// Отримати детальну інформацію про систему
    /// </summary>
    public HardwareInfo GetHardwareInfo()
    {
        return new HardwareInfo
        {
            CpuId = GetCpuId(),
            MotherboardSerial = GetMotherboardSerial(),
            MacAddress = GetMacAddress(),
            Fingerprint = GetFingerprint()
        };
    }
}

/// <summary>
/// Інформація про залізо
/// </summary>
public class HardwareInfo
{
    public string CpuId { get; set; } = string.Empty;
    public string MotherboardSerial { get; set; } = string.Empty;
    public string MacAddress { get; set; } = string.Empty;
    public string Fingerprint { get; set; } = string.Empty;
}
