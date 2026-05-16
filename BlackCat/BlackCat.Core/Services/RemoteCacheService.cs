using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using BlackCat.Core.Data;
using BlackCat.Shared.Models;
using Microsoft.Data.Sqlite;

namespace BlackCat.Core.Services;

/// <summary>Запис кешу файлів для одного вузла.</summary>
public class RemoteFileCache
{
    public string          BlackId   { get; set; } = string.Empty;
    public DateTime        UpdatedAt { get; set; } = DateTime.UtcNow;
    public List<RemoteCacheEntry> Files { get; set; } = new();
}

/// <summary>Один файл/директорія у кеші.</summary>
public class RemoteCacheEntry
{
    public string   Name         { get; set; } = string.Empty;
    public string   FullPath     { get; set; } = string.Empty;
    public bool     IsDirectory  { get; set; }
    public long     Size         { get; set; }
    public DateTime ModifiedDate { get; set; }
    public string   Extension    { get; set; } = string.Empty;
}

/// <summary>
/// Кешує файловий список віддаленого вузла DCS у поле Servers.Metadata.
///
/// Принцип роботи:
///   • Коли тунель встановлено, надсилає DCS-команду GetFileList через тунель.
///   • Відповідь отримується через HandleIncomingPacket → HandleSyncResponse.
///   • Збережений JSON доступний через GetCachedFiles(blackId).
/// </summary>
public sealed class RemoteCacheService : IDisposable
{
    private readonly BlackCatDatabase  _database;
    private readonly DcsIntegrationService _dcsService;
    private bool _disposed;

    public RemoteCacheService(BlackCatDatabase database, DcsIntegrationService dcsService)
    {
        _database   = database;
        _dcsService = dcsService;
        _dcsService.FileReceived += OnFileReceived;
    }

    // ─── Запит файлового списку ───────────────────────────────────────────────

    /// <summary>
    /// Надіслати DCS-команду GetFileList до вузла через тунель.
    /// Відповідь прийде асинхронно через HandleIncomingPacket у DcsIntegrationService.
    /// </summary>
    public async Task RequestFileListAsync(string peerBlackID, string remotePath = "/")
    {
        try
        {
            if (!_dcsService.IsConnectedTo(peerBlackID)) return;

            var packet = new DcsPacket
            {
                PayloadType = DcsPayloadType.SyncCommand,
                Meta = new Dictionary<string, string>
                {
                    ["command"]  = "GetFileList",
                    ["path"]     = remotePath,
                    ["filter"]   = "*.*"
                }
            };

            // Відправка через DcsIntegrationService (через тунель)
            await SendSyncCommandAsync(peerBlackID, packet);
        }
        catch { }
    }

    // ─── Читання кешу ────────────────────────────────────────────────────────

    /// <summary>Повернути кешований список файлів для вузла (або null якщо кешу немає).</summary>
    public RemoteFileCache? GetCachedFiles(string blackId)
    {
        try
        {
            var json = ReadMetadata(blackId);
            if (string.IsNullOrEmpty(json)) return null;
            return JsonSerializer.Deserialize<RemoteFileCache>(json);
        }
        catch { return null; }
    }

    /// <summary>Зберегти кеш (викликається при отриманні SyncResponse).</summary>
    public void SaveCachedFiles(RemoteFileCache cache)
    {
        try
        {
            var json = JsonSerializer.Serialize(cache);
            WriteMetadata(cache.BlackId, json);
        }
        catch { }
    }

    // ─── Обробка вхідних відповідей ──────────────────────────────────────────

    private void OnFileReceived(object? sender, DcsFileReceivedEventArgs e)
    {
        try
        {
            // Отримали файл з тегом "FileList" — це відповідь на GetFileList
            if (!e.FileName.StartsWith("__filelist__", StringComparison.OrdinalIgnoreCase)) return;

            var json = Encoding.UTF8.GetString(e.Data);
            var entries = JsonSerializer.Deserialize<List<RemoteCacheEntry>>(json);
            if (entries == null) return;

            SaveCachedFiles(new RemoteFileCache
            {
                BlackId   = e.SourceBlackID,
                UpdatedAt = DateTime.UtcNow,
                Files     = entries
            });
        }
        catch { }
    }

    // ─── SQLite helper ────────────────────────────────────────────────────────

    private string? ReadMetadata(string blackId)
    {
        try
        {
            using var conn = new SqliteConnection(_database.ConnectionString);
            conn.Open();
            using var cmd  = new SqliteCommand(
                "SELECT Metadata FROM Servers WHERE BlackID = @id LIMIT 1", conn);
            cmd.Parameters.AddWithValue("@id", blackId);
            var result = cmd.ExecuteScalar();
            return result is DBNull or null ? null : (string)result;
        }
        catch { return null; }
    }

    private void WriteMetadata(string blackId, string json)
    {
        try
        {
            using var conn = new SqliteConnection(_database.ConnectionString);
            conn.Open();
            using var cmd  = new SqliteCommand(
                @"INSERT INTO Servers (BlackID, HardwareFingerprint, StatusId, DisplayName,
                      CreatedAt, IsActive, Metadata)
                  VALUES (@id, '', 1, @id, @now, 1, @meta)
                  ON CONFLICT(BlackID) DO UPDATE SET Metadata = @meta, LastSeenAt = @now",
                conn);
            cmd.Parameters.AddWithValue("@id",   blackId);
            cmd.Parameters.AddWithValue("@meta", json);
            cmd.Parameters.AddWithValue("@now",  DateTime.UtcNow.ToString("O"));
            cmd.ExecuteNonQuery();
        }
        catch { }
    }

    private async Task SendSyncCommandAsync(string peerBlackID, DcsPacket packet)
    {
        try { await _dcsService.SendRawPacketAsync(peerBlackID, packet); }
        catch { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _dcsService.FileReceived -= OnFileReceived; } catch { }
    }
}
