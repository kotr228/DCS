namespace BlackCat.Shared.Models;

/// <summary>
/// Інформація про мережевий пакет
/// </summary>
public class PacketInfo
{
    /// <summary>
    /// IP адреса джерела
    /// </summary>
    public string SourceIP { get; set; } = string.Empty;

    /// <summary>
    /// IP адреса призначення
    /// </summary>
    public string DestinationIP { get; set; } = string.Empty;

    /// <summary>
    /// Порт джерела
    /// </summary>
    public int SourcePort { get; set; }

    /// <summary>
    /// Порт призначення
    /// </summary>
    public int DestinationPort { get; set; }

    /// <summary>
    /// Протокол (TCP/UDP)
    /// </summary>
    public ProtocolType Protocol { get; set; }

    /// <summary>
    /// Дані пакету
    /// </summary>
    public byte[] Payload { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// Розмір пакету в байтах
    /// </summary>
    public int Size => Payload.Length;

    /// <summary>
    /// Час створення пакету
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Чи є це пакет тунелю
    /// </summary>
    public bool IsTunnelPacket { get; set; }
}
