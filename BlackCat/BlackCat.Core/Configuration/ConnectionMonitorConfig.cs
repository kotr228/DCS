namespace BlackCat.Core.Configuration;

/// <summary>
/// Конфігурація моніторингу з'єднань та автоматичного перепідключення
/// </summary>
public class ConnectionMonitorConfig
{
    /// <summary>
    /// Інтервал відправки keep-alive пакетів (за замовчуванням 30 секунд)
    /// </summary>
    public TimeSpan KeepAliveInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Timeout для keep-alive (якщо немає відповіді за цей час - з'єднання вважається втраченим)
    /// За замовчуванням 60 секунд (2 * KeepAliveInterval)
    /// </summary>
    public TimeSpan KeepAliveTimeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Початковий інтервал для спроб перепідключення (за замовчуванням 10 секунд)
    /// </summary>
    public TimeSpan InitialReconnectDelay { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Максимальний інтервал між спробами перепідключення (за замовчуванням 5 хвилин)
    /// </summary>
    public TimeSpan MaxReconnectDelay { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Максимальна кількість спроб перепідключення (за замовчуванням 5)
    /// </summary>
    public int MaxReconnectAttempts { get; set; } = 5;

    /// <summary>
    /// Чи використовувати exponential backoff для затримок перепідключення
    /// </summary>
    public bool UseExponentialBackoff { get; set; } = true;

    /// <summary>
    /// Чи автоматично оновлювати IP адреси при підключенні з нової адреси
    /// </summary>
    public bool AutoUpdateIPAddress { get; set; } = true;

    /// <summary>
    /// Чи зберігати історію змін IP адрес
    /// </summary>
    public bool SaveIPAddressHistory { get; set; } = true;

    /// <summary>
    /// Інтервал перевірки стану з'єднань (за замовчуванням 10 секунд)
    /// </summary>
    public TimeSpan MonitoringInterval { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Чи автоматично перепідключатись до довірених вузлів
    /// </summary>
    public bool AutoReconnectTrustedNodes { get; set; } = true;

    /// <summary>
    /// Чи логувати зміни IP адрес в ConsoleEventRepository
    /// </summary>
    public bool LogIPAddressChanges { get; set; } = true;

    /// <summary>
    /// Час очікування перед видаленням стану з'єднання після невдачі (за замовчуванням 1 година)
    /// </summary>
    public TimeSpan ConnectionStateRetention { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Створити конфігурацію за замовчуванням
    /// </summary>
    public static ConnectionMonitorConfig Default => new ConnectionMonitorConfig();

    /// <summary>
    /// Конфігурація для агресивного перепідключення (швидке відновлення)
    /// </summary>
    public static ConnectionMonitorConfig Aggressive => new ConnectionMonitorConfig
    {
        KeepAliveInterval = TimeSpan.FromSeconds(15),
        KeepAliveTimeout = TimeSpan.FromSeconds(30),
        InitialReconnectDelay = TimeSpan.FromSeconds(5),
        MaxReconnectAttempts = 10,
        MonitoringInterval = TimeSpan.FromSeconds(5)
    };

    /// <summary>
    /// Конфігурація для консервативного перепідключення (економія трафіку)
    /// </summary>
    public static ConnectionMonitorConfig Conservative => new ConnectionMonitorConfig
    {
        KeepAliveInterval = TimeSpan.FromMinutes(2),
        KeepAliveTimeout = TimeSpan.FromMinutes(4),
        InitialReconnectDelay = TimeSpan.FromSeconds(30),
        MaxReconnectAttempts = 3,
        MonitoringInterval = TimeSpan.FromSeconds(30)
    };

    /// <summary>
    /// Конфігурація для мобільних пристроїв (економія батареї)
    /// </summary>
    public static ConnectionMonitorConfig Mobile => new ConnectionMonitorConfig
    {
        KeepAliveInterval = TimeSpan.FromMinutes(5),
        KeepAliveTimeout = TimeSpan.FromMinutes(10),
        InitialReconnectDelay = TimeSpan.FromMinutes(1),
        MaxReconnectAttempts = 3,
        MonitoringInterval = TimeSpan.FromMinutes(1)
    };
}
