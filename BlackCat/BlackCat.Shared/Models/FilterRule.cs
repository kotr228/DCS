namespace BlackCat.Shared.Models;

/// <summary>
/// Правило фільтрації пакетів
/// </summary>
public class FilterRule
{
    public int Id { get; set; }

    /// <summary>
    /// Назва правила
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// IP адреса або підмережа (наприклад: 192.168.1.0/24)
    /// </summary>
    public string IPAddress { get; set; } = string.Empty;

    /// <summary>
    /// Порт (0 = будь-який)
    /// </summary>
    public int Port { get; set; }

    /// <summary>
    /// Протокол
    /// </summary>
    public ProtocolType Protocol { get; set; }

    /// <summary>
    /// Дія (дозволити/заблокувати)
    /// </summary>
    public FilterAction Action { get; set; }

    /// <summary>
    /// Напрямок (вхідний/вихідний/обидва)
    /// </summary>
    public TrafficDirection Direction { get; set; }

    /// <summary>
    /// Чи активне правило
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Дата створення правила
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Пріоритет правила (нижчий = вища пріоритетність)
    /// </summary>
    public int Priority { get; set; } = 100;
}
