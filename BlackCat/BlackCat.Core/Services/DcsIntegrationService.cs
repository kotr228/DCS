using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using BlackCat.Core.Data;
using BlackCat.Shared.Models;

namespace BlackCat.Core.Services;

/// <summary>Прогрес відправки/отримання файлу DCS.</summary>
public class DcsTransferProgressEventArgs : EventArgs
{
    public string  TransferId      { get; init; } = string.Empty;
    public string  FileName        { get; init; } = string.Empty;
    public string  PeerBlackID     { get; init; } = string.Empty;
    public double  ProgressPercent { get; init; }
    public long    BytesDone       { get; init; }
    public long    TotalBytes      { get; init; }
    public bool    IsComplete      { get; init; }
    public bool    IsError         { get; init; }
    public string? ErrorMessage    { get; init; }
}

/// <summary>
/// Сервісний міст між BlackCat (MQE-тунелі) та системою документообігу DCS.
///
/// Принцип роботи:
///   • Якщо DCS не встановлено/не запущено — BlackCat продовжує працювати в штатному режимі;
///     методи цього сервісу просто повертають false/не роблять нічого.
///   • Всі операції загорнуті в try/catch — жоден виняток не вириває назовні.
///   • Великі файли автоматично розбиваються на 32 KB чанки (MaxChunkSize),
///     щоб вписатися в обмеження UDP з урахуванням MQE-паддінгу.
/// </summary>
public sealed class DcsIntegrationService : IDisposable
{
    private readonly TunnelManager            _tunnelManager;
    private readonly ConnectionEventRepository _eventRepository;
    private readonly DcsTransferRepository    _transferRepository;
    private readonly string?                  _ourBlackID;

    // transferId → CTS для скасування вихідних передавань
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _outgoing = new();
    // transferId → стан збирання вхідних чанків
    private readonly ConcurrentDictionary<string, DcsIncomingTransfer>     _incoming = new();

    public event EventHandler<DcsTransferProgressEventArgs>? TransferProgress;
    public event EventHandler<DcsFileReceivedEventArgs>?     FileReceived;

    public DcsIntegrationService(
        TunnelManager            tunnelManager,
        ConnectionEventRepository eventRepository,
        DcsTransferRepository    transferRepository,
        string?                  ourBlackID)
    {
        _tunnelManager      = tunnelManager;
        _eventRepository    = eventRepository;
        _transferRepository = transferRepository;
        _ourBlackID         = ourBlackID;
    }

    /// <summary>Чи є активне TCP або UDP з'єднання з цим піром.</summary>
    public bool IsConnectedTo(string peerBlackID)
    {
        try { return _tunnelManager.IsConnectedTo(peerBlackID); }
        catch { return false; }
    }

    // ─── Відправка ────────────────────────────────────────────────────────────

    /// <summary>
    /// Надіслати файл з диску через MQE-тунель з автоматичним чанкуванням.
    /// Повертає false якщо тунель не активний або виникла помилка.
    /// </summary>
    public async Task<bool> SendFileAsync(
        string            peerBlackID,
        string            filePath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(peerBlackID) || string.IsNullOrWhiteSpace(filePath))
            return false;

        var transferId = Guid.NewGuid().ToString("N")[..12];
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _outgoing[transferId] = linked;

        var fileName = Path.GetFileName(filePath);
        try
        {
            // ── Читання файлу ──
            byte[] fileData;
            try
            {
                fileData = await File.ReadAllBytesAsync(filePath, linked.Token);
            }
            catch (Exception ex)
            {
                RaiseProgress(transferId, fileName, peerBlackID, 0, 0, 0, error: ex.Message);
                return false;
            }

            var  checksum    = ComputeSha256(fileData);
            long totalSize   = fileData.Length;
            int  chunkSize   = DcsPacket.MaxChunkSize;
            int  totalChunks = (int)Math.Ceiling((double)totalSize / chunkSize);

            // ── 1. FileMeta ──
            var metaPacket = new DcsPacket
            {
                PayloadType = DcsPayloadType.FileMeta,
                Meta = new Dictionary<string, string>
                {
                    ["transferId"]    = transferId,
                    ["fileName"]      = fileName,
                    ["totalSize"]     = totalSize.ToString(),
                    ["totalChunks"]   = totalChunks.ToString(),
                    ["checksum"]      = checksum,
                    ["senderBlackID"] = _ourBlackID ?? "UNKNOWN"
                }
            };
            if (!await SendPacketSafeAsync(peerBlackID, metaPacket)) return false;

            LogDcsEvent(peerBlackID, "DcsFileSyncStart",
                $"Надсилання: {fileName} ({totalSize} B) → {peerBlackID} [id:{transferId}]");

            // ── 2. Chunks ──
            for (int i = 0; i < totalChunks; i++)
            {
                if (linked.Token.IsCancellationRequested) return false;

                int start = i * chunkSize;
                int len   = Math.Min(chunkSize, (int)(totalSize - start));

                var chunk = new DcsPacket
                {
                    PayloadType = DcsPayloadType.FileChunk,
                    Meta = new Dictionary<string, string>
                    {
                        ["transferId"]  = transferId,
                        ["chunkIndex"]  = i.ToString(),
                        ["totalChunks"] = totalChunks.ToString()
                    },
                    Content = fileData.AsSpan(start, len).ToArray()
                };
                if (!await SendPacketSafeAsync(peerBlackID, chunk)) return false;

                long done = Math.Min((long)(i + 1) * chunkSize, totalSize);
                RaiseProgress(transferId, fileName, peerBlackID,
                    (double)(i + 1) / totalChunks * 100.0, done, totalSize);
            }

            // ── 3. FileComplete ──
            var completePacket = new DcsPacket
            {
                PayloadType = DcsPayloadType.FileComplete,
                Meta = new Dictionary<string, string>
                {
                    ["transferId"] = transferId,
                    ["checksum"]   = checksum,
                    ["fileName"]   = fileName
                }
            };
            if (!await SendPacketSafeAsync(peerBlackID, completePacket)) return false;

            RaiseProgress(transferId, fileName, peerBlackID, 100, totalSize, totalSize, isComplete: true);

            LogDcsEvent(peerBlackID, "DcsFileSyncSuccess",
                $"Файл надіслано: {fileName} ({totalSize} B) → {peerBlackID}",
                bytesSent: totalSize);

            _transferRepository.LogTransfer(new DcsTransferRecord
            {
                FilePath       = fileName,
                FileSize       = totalSize,
                ChecksumSHA256 = checksum,
                PeerBlackID    = peerBlackID,
                TransferredAt  = DateTime.UtcNow
            });

            return true;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception ex)
        {
            try { RaiseProgress(transferId, fileName, peerBlackID, 0, 0, 0, error: ex.Message); }
            catch { }
            return false;
        }
        finally
        {
            _outgoing.TryRemove(transferId, out _);
        }
    }

    // ─── Прийом ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Обробити вхідний DCS-пакет. Викликається з OnTunnelDataReceived у UI.
    /// Ніколи не кидає — всі помилки поглинаються.
    /// </summary>
    public void HandleIncomingPacket(string sourceBlackID, byte[] data)
    {
        try
        {
            var packet = DcsPacket.TryFromBytes(data);
            if (packet is null) return;

            switch (packet.PayloadType)
            {
                case DcsPayloadType.FileMeta:     HandleFileMeta(sourceBlackID, packet);     break;
                case DcsPayloadType.FileChunk:    HandleFileChunk(sourceBlackID, packet);    break;
                case DcsPayloadType.FileComplete: HandleFileComplete(sourceBlackID, packet); break;
                case DcsPayloadType.SyncCommand:  HandleSyncCommand(sourceBlackID, packet);  break;
            }
        }
        catch { }
    }

    public void CancelTransfer(string transferId)
    {
        try
        {
            if (_outgoing.TryGetValue(transferId, out var cts)) cts.Cancel();
        }
        catch { }
    }

    // ─── Внутрішні обробники ──────────────────────────────────────────────────

    private void HandleFileMeta(string sourceBlackID, DcsPacket p)
    {
        try
        {
            var t = new DcsIncomingTransfer
            {
                TransferId       = p.Meta.GetValueOrDefault("transferId", Guid.NewGuid().ToString("N")),
                FileName         = p.Meta.GetValueOrDefault("fileName", "unknown"),
                TotalSize        = long.TryParse(p.Meta.GetValueOrDefault("totalSize"),   out var sz) ? sz : 0,
                TotalChunks      = int.TryParse( p.Meta.GetValueOrDefault("totalChunks"), out var tc) ? tc : 1,
                ExpectedChecksum = p.Meta.GetValueOrDefault("checksum", ""),
                SourceBlackID    = sourceBlackID
            };
            _incoming[t.TransferId] = t;
            RaiseProgress(t.TransferId, t.FileName, sourceBlackID, 0, 0, t.TotalSize);
            LogDcsEvent(sourceBlackID, "DcsFileSyncStart",
                $"Отримання: {t.FileName} ({t.TotalSize} B) від {sourceBlackID}");
        }
        catch { }
    }

    private void HandleFileChunk(string sourceBlackID, DcsPacket p)
    {
        try
        {
            var id = p.Meta.GetValueOrDefault("transferId");
            if (id is null || !_incoming.TryGetValue(id, out var t)) return;
            int idx = int.TryParse(p.Meta.GetValueOrDefault("chunkIndex"), out var ci) ? ci : 0;
            lock (t.Chunks) { t.Chunks[idx] = p.Content; }
            long done = Math.Min((long)(idx + 1) * DcsPacket.MaxChunkSize, t.TotalSize);
            RaiseProgress(id, t.FileName, sourceBlackID, t.Progress, done, t.TotalSize);
        }
        catch { }
    }

    private void HandleFileComplete(string sourceBlackID, DcsPacket p)
    {
        try
        {
            var id = p.Meta.GetValueOrDefault("transferId");
            if (id is null || !_incoming.TryRemove(id, out var t)) return;
            if (!t.IsComplete) return;

            var data   = t.AssembleData();
            var actual = ComputeSha256(data);

            if (!string.IsNullOrEmpty(t.ExpectedChecksum) && actual != t.ExpectedChecksum)
            {
                LogDcsEvent(sourceBlackID, "DcsFileSyncError",
                    $"Контрольна сума не збігається: {t.FileName} від {sourceBlackID}");
                RaiseProgress(id, t.FileName, sourceBlackID, 0, 0, t.TotalSize,
                    error: "Checksum mismatch");
                return;
            }

            RaiseProgress(id, t.FileName, sourceBlackID, 100, t.TotalSize, t.TotalSize, isComplete: true);

            try
            {
                FileReceived?.Invoke(this, new DcsFileReceivedEventArgs
                {
                    TransferId    = t.TransferId,
                    FileName      = t.FileName,
                    SourceBlackID = sourceBlackID,
                    Data          = data
                });
            }
            catch { }

            LogDcsEvent(sourceBlackID, "DcsFileSyncSuccess",
                $"Файл отримано: {t.FileName} ({t.TotalSize} B) від {sourceBlackID}",
                bytesReceived: t.TotalSize);

            _transferRepository.LogTransfer(new DcsTransferRecord
            {
                FilePath       = t.FileName,
                FileSize       = t.TotalSize,
                ChecksumSHA256 = t.ExpectedChecksum,
                PeerBlackID    = sourceBlackID,
                TransferredAt  = DateTime.UtcNow
            });
        }
        catch { }
    }

    private void HandleSyncCommand(string sourceBlackID, DcsPacket p)
    {
        try
        {
            var cmd = p.Meta.GetValueOrDefault("command", "");
            LogDcsEvent(sourceBlackID, "DcsFolderShared",
                $"DCS команда '{cmd}' від {sourceBlackID}");
        }
        catch { }
    }

    // ─── Утиліти ──────────────────────────────────────────────────────────────

    private async Task<bool> SendPacketSafeAsync(string peerBlackID, DcsPacket packet)
    {
        try { return await _tunnelManager.SendDataViaRelayAsync(peerBlackID, packet.ToBytes()); }
        catch { return false; }
    }

    private void LogDcsEvent(string peerBlackID, string eventTypeName, string message,
        long bytesSent = 0, long bytesReceived = 0)
    {
        try
        {
            _eventRepository.LogEvent(new ConnectionEvent
            {
                RemoteBlackID    = peerBlackID,
                RemoteIP         = string.Empty,
                RemotePort       = 0,
                InitiatorBlackID = _ourBlackID,
                EventType        = ConnectionEventType.AuthenticationSuccess,
                Direction        = ConnectionDirection.Outbound,
                Message          = $"[DCS] {message}",
                IsAuthenticated  = true,
                BytesSent        = bytesSent,
                BytesReceived    = bytesReceived,
                Timestamp        = DateTime.UtcNow
            });
        }
        catch { }
    }

    private void RaiseProgress(string transferId, string fileName, string peerBlackID,
        double progress, long done, long total,
        bool isComplete = false, string? error = null)
    {
        try
        {
            TransferProgress?.Invoke(this, new DcsTransferProgressEventArgs
            {
                TransferId      = transferId,
                FileName        = fileName,
                PeerBlackID     = peerBlackID,
                ProgressPercent = progress,
                BytesDone       = done,
                TotalBytes      = total,
                IsComplete      = isComplete,
                IsError         = error != null,
                ErrorMessage    = error
            });
        }
        catch { }
    }

    private static string ComputeSha256(byte[] data)
    {
        try { return Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant(); }
        catch { return string.Empty; }
    }

    public void Dispose()
    {
        try
        {
            foreach (var cts in _outgoing.Values)
                try { cts.Cancel(); cts.Dispose(); } catch { }
            _outgoing.Clear();
            _incoming.Clear();
        }
        catch { }
        GC.SuppressFinalize(this);
    }
}
