using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace BlackCat.Shared.Models;

/// <summary>
/// Тип корисного навантаження DCS-пакету
/// </summary>
public enum DcsPayloadType : byte
{
    FileMeta     = 1,  // метадані перед чанками
    FileChunk    = 2,  // фрагмент файлу
    FileComplete = 3,  // сигнал завершення передачі
    SyncCommand  = 4,  // команда синхронізації (lock/unlock/list)
    SyncResponse = 5,  // відповідь на команду
}

/// <summary>
/// DCS-пакет. Формат:
/// [0xBC][0x1F][PayloadType:1B][MetaLen:4B LE][Meta:JSON][Content]
/// Маркер 0xBC 0x1F відрізняє від NodeInfo (0xBC 0x1D) та FileTransfer (0xBC 0x1E).
/// </summary>
public class DcsPacket
{
    public static readonly byte[] Marker = { 0xBC, 0x1F };
    public const int MaxChunkSize = 32 * 1024; // 32 KB — вписується в UDP MTU з MQE overhead

    public DcsPayloadType             PayloadType { get; set; }
    public Dictionary<string, string> Meta        { get; set; } = new();
    public byte[]                     Content     { get; set; } = Array.Empty<byte>();

    /// <summary>Серіалізувати в байти для відправки через MQE-тунель.</summary>
    public byte[] ToBytes()
    {
        var metaBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(Meta));
        var buf = new byte[Marker.Length + 1 + 4 + metaBytes.Length + Content.Length];
        int off = 0;
        Marker.CopyTo(buf, off);               off += Marker.Length;
        buf[off++] = (byte)PayloadType;
        BitConverter.GetBytes(metaBytes.Length).CopyTo(buf, off); off += 4;
        metaBytes.CopyTo(buf, off);            off += metaBytes.Length;
        Content.CopyTo(buf, off);
        return buf;
    }

    /// <summary>Десеріалізувати з байтів. Повертає null при будь-якій помилці.</summary>
    public static DcsPacket? TryFromBytes(byte[] data)
    {
        try
        {
            if (data.Length < Marker.Length + 1 + 4) return null;
            if (data[0] != Marker[0] || data[1] != Marker[1]) return null;

            int off = Marker.Length;
            var type    = (DcsPayloadType)data[off++];
            int metaLen = BitConverter.ToInt32(data, off); off += 4;
            if (metaLen < 0 || off + metaLen > data.Length) return null;

            var meta = JsonSerializer.Deserialize<Dictionary<string, string>>(
                           Encoding.UTF8.GetString(data, off, metaLen))
                       ?? new Dictionary<string, string>();
            off += metaLen;

            return new DcsPacket
            {
                PayloadType = type,
                Meta        = meta,
                Content     = data.AsSpan(off).ToArray()
            };
        }
        catch { return null; }
    }
}

/// <summary>Стан вхідного передавання — збирання чанків у пам'яті.</summary>
public class DcsIncomingTransfer
{
    public string   TransferId       { get; set; } = string.Empty;
    public string   FileName         { get; set; } = string.Empty;
    public long     TotalSize        { get; set; }
    public int      TotalChunks      { get; set; }
    public string   ExpectedChecksum { get; set; } = string.Empty;
    public string   SourceBlackID    { get; set; } = string.Empty;
    public DateTime StartedAt        { get; set; } = DateTime.UtcNow;
    public string   DirName          { get; set; } = string.Empty;
    public string   RelativePath     { get; set; } = string.Empty;

    // Ключ = індекс чанку. lock(Chunks) перед записом.
    public SortedDictionary<int, byte[]> Chunks { get; } = new();

    public double Progress   => TotalChunks > 0 ? (double)Chunks.Count / TotalChunks * 100.0 : 0;
    public bool   IsComplete => TotalChunks > 0 && Chunks.Count >= TotalChunks;

    /// <summary>Зібрати всі чанки в єдиний масив байтів.</summary>
    public byte[] AssembleData()
    {
        using var ms = new MemoryStream((int)Math.Max(TotalSize, 0));
        foreach (var kv in Chunks)
            ms.Write(kv.Value);
        return ms.ToArray();
    }
}

/// <summary>Аргументи події успішного отримання файлу DCS.</summary>
public class DcsFileReceivedEventArgs : EventArgs
{
    public string TransferId    { get; init; } = string.Empty;
    public string FileName      { get; init; } = string.Empty;
    public string SourceBlackID { get; init; } = string.Empty;
    public byte[] Data          { get; init; } = Array.Empty<byte>();
    public string DirName       { get; init; } = string.Empty;
    public string RelativePath  { get; init; } = string.Empty;
}
