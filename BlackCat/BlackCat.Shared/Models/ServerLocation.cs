namespace BlackCat.Shared.Models;

/// <summary>
/// Географічне розташування сервера (для мапи)
/// </summary>
public class ServerLocation
{
    /// <summary>
    /// Унікальний ідентифікатор
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// ID сервера (FK → Servers)
    /// </summary>
    public int ServerId { get; set; }

    /// <summary>
    /// Широта (Latitude)
    /// </summary>
    public double Latitude { get; set; }

    /// <summary>
    /// Довгота (Longitude)
    /// </summary>
    public double Longitude { get; set; }

    /// <summary>
    /// IP адреса
    /// </summary>
    public string IPAddress { get; set; } = string.Empty;

    /// <summary>
    /// Порт
    /// </summary>
    public int Port { get; set; } = 9999;

    /// <summary>
    /// Фізична адреса (країна, місто, вулиця)
    /// </summary>
    public string? Address { get; set; }

    /// <summary>
    /// Місто (FK → Cities)
    /// </summary>
    public int? CityId { get; set; }

    /// <summary>
    /// Країна (ISO код)
    /// </summary>
    public string? CountryCode { get; set; }

    /// <summary>
    /// Регіон/область
    /// </summary>
    public string? Region { get; set; }

    /// <summary>
    /// Поштовий індекс
    /// </summary>
    public string? PostalCode { get; set; }

    /// <summary>
    /// Точність геолокації (в метрах)
    /// </summary>
    public double? AccuracyMeters { get; set; }

    /// <summary>
    /// Дата останнього оновлення локації
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Дата створення запису
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public Server? Server { get; set; }
    public City? City { get; set; }
}
