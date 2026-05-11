namespace BlackCat.Shared.Models;

/// <summary>
/// Структура зашифрованого пакету тунелю
/// </summary>
public class TunnelPacket
{
    /// <summary>
    /// Версія протоколу
    /// </summary>
    public byte Version { get; set; } = 1;

    /// <summary>
    /// Часова мітка (Timestamp) для генерації ключа
    /// </summary>
    public long Timestamp { get; set; }

    /// <summary>
    /// Зашифровані дані
    /// </summary>
    public byte[] EncryptedPayload { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// Контрольна сума (для перевірки цілісності)
    /// </summary>
    public byte[] Checksum { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// IP джерела (незашифрований)
    /// </summary>
    public string SourceIP { get; set; } = string.Empty;

    /// <summary>
    /// IP призначення (незашифрований)
    /// </summary>
    public string DestinationIP { get; set; } = string.Empty;

    /// <summary>
    /// Серіалізація в байтовий масив
    /// </summary>
    public byte[] ToBytes()
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        writer.Write(Version);
        writer.Write(Timestamp);
        writer.Write(EncryptedPayload.Length);
        writer.Write(EncryptedPayload);
        writer.Write(Checksum.Length);
        writer.Write(Checksum);

        return ms.ToArray();
    }

    /// <summary>
    /// Десеріалізація з байтового масиву
    /// </summary>
    public static TunnelPacket FromBytes(byte[] data)
    {
        using var ms = new MemoryStream(data);
        using var reader = new BinaryReader(ms);

        var packet = new TunnelPacket
        {
            Version = reader.ReadByte(),
            Timestamp = reader.ReadInt64()
        };

        int payloadLength = reader.ReadInt32();
        packet.EncryptedPayload = reader.ReadBytes(payloadLength);

        int checksumLength = reader.ReadInt32();
        packet.Checksum = reader.ReadBytes(checksumLength);

        return packet;
    }
}
