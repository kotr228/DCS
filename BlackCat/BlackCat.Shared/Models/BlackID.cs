namespace BlackCat.Shared.Models;

/// <summary>
/// Black-ID - унікальний ідентифікатор вузла
/// Формат: [РОЛЬ]-[МІСТО]-[НАЗВА]-[КОД]
/// Приклад: SKLAD-ODESA-SERVER-7X99
/// </summary>
public class BlackID
{
    /// <summary>
    /// Повний ID у форматі РОЛЬ-МІСТО-НАЗВА-КОД
    /// </summary>
    public string FullID { get; set; } = string.Empty;

    /// <summary>
    /// Роль вузла (SKLAD, OFFICE, MAIN, BACKUP)
    /// </summary>
    public string Role { get; set; } = string.Empty;

    /// <summary>
    /// Місто (KYIV, ODESA, LVIV, KHARKIV, тощо)
    /// </summary>
    public string City { get; set; } = string.Empty;

    /// <summary>
    /// Назва/призначення (SERVER, WORKSTATION, MAIN, тощо)
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Унікальний код (4 символи, наприклад 7X99)
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// Hardware Fingerprint - хеш залізу
    /// </summary>
    public string HardwareFingerprint { get; set; } = string.Empty;

    /// <summary>
    /// Криптографічний підпис для верифікації
    /// </summary>
    public string Signature { get; set; } = string.Empty;

    /// <summary>
    /// Дата створення
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Чи активний цей ID
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Парсити Black-ID з рядка
    /// </summary>
    public static BlackID? Parse(string fullID)
    {
        var parts = fullID.Split('-');
        if (parts.Length != 4)
            return null;

        return new BlackID
        {
            FullID = fullID,
            Role = parts[0],
            City = parts[1],
            Name = parts[2],
            Code = parts[3]
        };
    }

    /// <summary>
    /// Перевірити формат Black-ID
    /// </summary>
    public static bool IsValid(string fullID)
    {
        if (string.IsNullOrWhiteSpace(fullID))
            return false;

        var parts = fullID.Split('-');
        if (parts.Length != 4)
            return false;

        // Перевірити що всі частини не пусті
        return parts.All(p => !string.IsNullOrWhiteSpace(p) && p.Length >= 2);
    }

    public override string ToString() => FullID;
}
