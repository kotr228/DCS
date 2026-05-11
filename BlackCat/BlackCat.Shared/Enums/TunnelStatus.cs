namespace BlackCat.Shared.Enums;

/// <summary>
/// Статус тунелю
/// </summary>
public enum TunnelStatus
{
    /// <summary>
    /// Не підключено
    /// </summary>
    Disconnected,

    /// <summary>
    /// Підключення...
    /// </summary>
    Connecting,

    /// <summary>
    /// Підключено
    /// </summary>
    Connected,

    /// <summary>
    /// Помилка
    /// </summary>
    Error
}
