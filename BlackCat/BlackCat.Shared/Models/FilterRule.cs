using BlackCat.Shared.Enums;

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
    /// Діапазон портів (наприклад: "80,443" або "8000-9000")
    /// null = порт з поля Port
    /// </summary>
    public string? PortRange { get; set; }

    /// <summary>
    /// Шлях до застосунку для Application Control
    /// null = правило не прив'язане до застосунку
    /// Приклад: "C:\\Program Files\\MyApp\\app.exe"
    /// </summary>
    public string? ApplicationPath { get; set; }

    /// <summary>
    /// Назва процесу для Application Control
    /// null = правило не прив'язане до процесу
    /// Приклад: "chrome.exe", "WINWORD.EXE"
    /// </summary>
    public string? ProcessName { get; set; }

    /// <summary>
    /// Опис правила
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Теги для категоризації (наприклад: "web", "database", "critical")
    /// </summary>
    public string? Tags { get; set; }

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

    /// <summary>
    /// Перевірити чи порт відповідає правилу
    /// </summary>
    public bool MatchesPort(int port)
    {
        // Якщо є PortRange, використовуємо його
        if (!string.IsNullOrWhiteSpace(PortRange))
        {
            return PortRangeContains(PortRange, port);
        }

        // Інакше - простий порт (0 = будь-який)
        return Port == 0 || Port == port;
    }

    /// <summary>
    /// Перевірити чи діапазон портів містить заданий порт
    /// </summary>
    private static bool PortRangeContains(string range, int port)
    {
        // Формати: "80,443,8080" або "8000-9000" або "80,443,8000-9000"
        var parts = range.Split(',');
        foreach (var part in parts)
        {
            var trimmed = part.Trim();

            if (trimmed.Contains('-'))
            {
                // Діапазон
                var bounds = trimmed.Split('-');
                if (bounds.Length == 2 &&
                    int.TryParse(bounds[0].Trim(), out int start) &&
                    int.TryParse(bounds[1].Trim(), out int end))
                {
                    if (port >= start && port <= end)
                        return true;
                }
            }
            else
            {
                // Окремий порт
                if (int.TryParse(trimmed, out int singlePort) && singlePort == port)
                    return true;
            }
        }

        return false;
    }
}
