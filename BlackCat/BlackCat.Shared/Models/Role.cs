namespace BlackCat.Shared.Models;

/// <summary>
/// Довідник ролей для Black-ID
/// </summary>
public class Role
{
    /// <summary>
    /// Унікальний ідентифікатор
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Назва ролі (SKLAD, OFFICE, MAIN, BACKUP, SERVER, WORKSTATION)
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Опис ролі
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Чи активна роль
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Порядок сортування
    /// </summary>
    public int SortOrder { get; set; }
}
