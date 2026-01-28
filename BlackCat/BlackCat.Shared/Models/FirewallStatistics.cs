using BlackCat.Shared.Enums;

namespace BlackCat.Shared.Models;

/// <summary>
/// Статистика роботи брандмауера
/// </summary>
public class FirewallStatistics
{
    /// <summary>
    /// Загальна кількість оброблених пакетів
    /// </summary>
    public long TotalPackets { get; set; }

    /// <summary>
    /// Кількість дозволених пакетів
    /// </summary>
    public long AllowedPackets { get; set; }

    /// <summary>
    /// Кількість заблокованих пакетів
    /// </summary>
    public long BlockedPackets { get; set; }

    /// <summary>
    /// Кількість пакетів через тунель
    /// </summary>
    public long TunneledPackets { get; set; }

    /// <summary>
    /// Кількість пакетів з помилками
    /// </summary>
    public long ErrorPackets { get; set; }

    /// <summary>
    /// Швидкість потоку (байт/сек)
    /// </summary>
    public double BytesPerSecond { get; set; }

    /// <summary>
    /// Середня затримка (мілісекунди)
    /// </summary>
    public double AverageLatencyMs { get; set; }

    /// <summary>
    /// Статус тунелю
    /// </summary>
    public TunnelStatus TunnelStatus { get; set; }

    /// <summary>
    /// Час останнього пакету
    /// </summary>
    public DateTime LastPacketTime { get; set; }

    /// <summary>
    /// Час запуску служби
    /// </summary>
    public DateTime ServiceStartTime { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Час роботи служби
    /// </summary>
    public TimeSpan Uptime => DateTime.UtcNow - ServiceStartTime;
}
