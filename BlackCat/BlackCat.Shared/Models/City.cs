namespace BlackCat.Shared.Models;

/// <summary>
/// Довідник міст для Black-ID
/// </summary>
public class City
{
    /// <summary>
    /// Унікальний ідентифікатор
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Назва міста (KYIV, ODESA, LVIV тощо)
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Регіон/область
    /// </summary>
    public string? Region { get; set; }

    /// <summary>
    /// Широта (для мапи серверів)
    /// </summary>
    public double? Latitude { get; set; }

    /// <summary>
    /// Довгота (для мапи серверів)
    /// </summary>
    public double? Longitude { get; set; }

    /// <summary>
    /// Часовий пояс (UTC offset)
    /// </summary>
    public int? TimezoneOffset { get; set; }

    /// <summary>
    /// Чи активне місто
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Порядок сортування
    /// </summary>
    public int SortOrder { get; set; }
}
