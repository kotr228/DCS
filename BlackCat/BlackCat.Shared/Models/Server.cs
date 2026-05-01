namespace BlackCat.Shared.Models;

/// <summary>
/// Сервер в мережі BlackCat
/// </summary>
public class Server
{
    /// <summary>
    /// Унікальний ідентифікатор
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Black-ID сервера (FK → LocalBlackID)
    /// </summary>
    public string BlackID { get; set; } = string.Empty;

    /// <summary>
    /// Hardware Fingerprint
    /// </summary>
    public string HardwareFingerprint { get; set; } = string.Empty;

    /// <summary>
    /// Статус сервера (FK → ConnectionStatuses)
    /// </summary>
    public int StatusId { get; set; }

    /// <summary>
    /// Дружня назва сервера
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Опис сервера
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Операційна система
    /// </summary>
    public string? OperatingSystem { get; set; }

    /// <summary>
    /// Версія BlackCat Firewall
    /// </summary>
    public string? FirewallVersion { get; set; }

    /// <summary>
    /// Час останньої активності
    /// </summary>
    public DateTime? LastSeenAt { get; set; }

    /// <summary>
    /// Час першого підключення
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Чи активний сервер
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Метадані (JSON)
    /// </summary>
    public string? Metadata { get; set; }

    // Navigation properties
    public ConnectionStatus? Status { get; set; }
    public ServerLocation? Location { get; set; }
}
