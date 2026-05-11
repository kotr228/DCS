namespace BlackCat.Shared.Models;

/// <summary>
/// Віддалений вузол (peer) в телефонній книзі
/// </summary>
public class PeerNode
{
    /// <summary>
    /// Внутрішній ID в БД
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Black-ID віддаленого вузла
    /// </summary>
    public string BlackID { get; set; } = string.Empty;

    /// <summary>
    /// IP-адреса або доменне ім'я
    /// </summary>
    public string Address { get; set; } = string.Empty;

    /// <summary>
    /// Порт для підключення
    /// </summary>
    public int Port { get; set; } = 9999;

    /// <summary>
    /// Дружнє ім'я для відображення
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Опис вузла
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Чи довірений цей вузол (пройшов handshake)
    /// </summary>
    public bool IsTrusted { get; set; }

    /// <summary>
    /// Дата останнього успішного з'єднання
    /// </summary>
    public DateTime? LastConnectedAt { get; set; }

    /// <summary>
    /// Дата додавання в книгу
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Чи активний цей вузол
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Статус з'єднання (FK → ConnectionStatuses)
    /// </summary>
    public int? StatusId { get; set; }

    /// <summary>
    /// Кількість успішних з'єднань
    /// </summary>
    public int SuccessfulConnections { get; set; }

    /// <summary>
    /// Кількість невдалих спроб
    /// </summary>
    public int FailedConnections { get; set; }

    /// <summary>
    /// Публічний ключ для криптографії
    /// </summary>
    public string? PublicKey { get; set; }

    /// <summary>
    /// Теги для категоризації (наприклад: "склад", "офіс", "критичний")
    /// </summary>
    public string? Tags { get; set; }

    // Navigation properties
    public ConnectionStatus? Status { get; set; }
}
