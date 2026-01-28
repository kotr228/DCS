namespace BlackCat.Shared.Enums;

/// <summary>
/// Тип мережевого протоколу
/// </summary>
public enum ProtocolType
{
    /// <summary>
    /// Будь-який протокол
    /// </summary>
    Any = 0,

    /// <summary>
    /// TCP протокол
    /// </summary>
    TCP = 6,

    /// <summary>
    /// UDP протокол
    /// </summary>
    UDP = 17,

    /// <summary>
    /// ICMP протокол
    /// </summary>
    ICMP = 1
}
