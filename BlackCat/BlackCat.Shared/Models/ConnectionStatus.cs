namespace BlackCat.Shared.Models;

/// <summary>
/// Довідник статусів з'єднання
/// </summary>
public class ConnectionStatus
{
    /// <summary>
    /// Унікальний ідентифікатор
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Назва статусу (Connected, Disconnected, Connecting, Failed тощо)
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Опис статусу
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Колір для UI (HEX формат, наприклад #4EC9B0)
    /// </summary>
    public string? Color { get; set; }

    /// <summary>
    /// Іконка для UI
    /// </summary>
    public string? Icon { get; set; }

    /// <summary>
    /// Чи це фінальний стан (Connected, Disconnected)
    /// </summary>
    public bool IsFinal { get; set; }

    /// <summary>
    /// Чи активний статус
    /// </summary>
    public bool IsActive { get; set; } = true;
}
