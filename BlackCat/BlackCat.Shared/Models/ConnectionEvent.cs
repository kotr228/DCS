namespace BlackCat.Shared.Models;

/// <summary>
/// Подія з'єднання для audit log
/// </summary>
public class ConnectionEvent
{
    /// <summary>
    /// ID події
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Black-ID віддаленого вузла (якщо відомий)
    /// </summary>
    public string? RemoteBlackID { get; set; }

    /// <summary>
    /// IP-адреса віддаленого вузла
    /// </summary>
    public string RemoteIP { get; set; } = string.Empty;

    /// <summary>
    /// Порт віддаленого вузла
    /// </summary>
    public int RemotePort { get; set; }

    /// <summary>
    /// Тип події
    /// </summary>
    public ConnectionEventType EventType { get; set; }

    /// <summary>
    /// Напрямок (Inbound/Outbound)
    /// </summary>
    public ConnectionDirection Direction { get; set; }

    /// <summary>
    /// Повідомлення про подію
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Деталі помилки (якщо є)
    /// </summary>
    public string? ErrorDetails { get; set; }

    /// <summary>
    /// Чи пройшла автентифікація
    /// </summary>
    public bool IsAuthenticated { get; set; }

    /// <summary>
    /// Час події
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Тривалість з'єднання (якщо застосовно)
    /// </summary>
    public TimeSpan? Duration { get; set; }

    /// <summary>
    /// Передано байт
    /// </summary>
    public long BytesSent { get; set; }

    /// <summary>
    /// Отримано байт
    /// </summary>
    public long BytesReceived { get; set; }
}

/// <summary>
/// Тип події з'єднання
/// </summary>
public enum ConnectionEventType
{
    /// <summary>
    /// Спроба підключення
    /// </summary>
    ConnectionAttempt,

    /// <summary>
    /// Успішне з'єднання
    /// </summary>
    Connected,

    /// <summary>
    /// Розрив з'єднання
    /// </summary>
    Disconnected,

    /// <summary>
    /// Відмова в доступі
    /// </summary>
    AccessDenied,

    /// <summary>
    /// Невдала автентифікація
    /// </summary>
    AuthenticationFailed,

    /// <summary>
    /// Успішна автентифікація
    /// </summary>
    AuthenticationSuccess,

    /// <summary>
    /// Помилка з'єднання
    /// </summary>
    ConnectionError,

    /// <summary>
    /// Підозріла активність
    /// </summary>
    SuspiciousActivity
}

/// <summary>
/// Напрямок з'єднання
/// </summary>
public enum ConnectionDirection
{
    /// <summary>
    /// Вхідне з'єднання
    /// </summary>
    Inbound,

    /// <summary>
    /// Вихідне з'єднання
    /// </summary>
    Outbound
}
