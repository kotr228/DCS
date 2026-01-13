namespace BlackCat.Shared.Models;

/// <summary>
/// Дія фільтрації
/// </summary>
public enum FilterAction
{
    /// <summary>
    /// Дозволити пакет
    /// </summary>
    Allow,

    /// <summary>
    /// Заблокувати пакет
    /// </summary>
    Block,

    /// <summary>
    /// Направити через тунель
    /// </summary>
    Tunnel
}
