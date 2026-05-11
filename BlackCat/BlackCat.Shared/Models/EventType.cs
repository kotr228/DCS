namespace BlackCat.Shared.Models;

/// <summary>
/// Довідник типів подій з'єднання
/// </summary>
public class EventType
{
    /// <summary>
    /// Унікальний ідентифікатор
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Назва події (HelloReceived, ChallengeReceived, ResponseReceived тощо)
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Опис події
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Категорія (Handshake, Connection, Error тощо)
    /// </summary>
    public string? Category { get; set; }

    /// <summary>
    /// Рівень важливості (Info, Warning, Error)
    /// </summary>
    public string? Severity { get; set; }

    /// <summary>
    /// Чи активний тип
    /// </summary>
    public bool IsActive { get; set; } = true;
}
