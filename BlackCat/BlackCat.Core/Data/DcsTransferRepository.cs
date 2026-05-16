using Microsoft.Data.Sqlite;

namespace BlackCat.Core.Data;

/// <summary>Запис про передачу файлу через DCS-інтеграцію.</summary>
public class DcsTransferRecord
{
    public int      Id                { get; set; }
    public int?     ConnectionEventId { get; set; }
    public string   FilePath          { get; set; } = string.Empty;
    public long     FileSize          { get; set; }
    public string?  TargetFolder      { get; set; }
    public int?     SyncStatusId      { get; set; }
    public string?  ChecksumSHA256    { get; set; }
    public string?  PeerBlackID       { get; set; }
    public DateTime TransferredAt     { get; set; }
}

/// <summary>
/// CRUD для таблиці DcsTransfers.
/// Всі методи загорнуті в try/catch — помилки БД не зупиняють роботу тунелів.
/// </summary>
public class DcsTransferRepository
{
    private readonly string _connectionString;

    public DcsTransferRepository(BlackCatDatabase database)
    {
        _connectionString = database.ConnectionString;
    }

    public void LogTransfer(DcsTransferRecord rec)
    {
        try
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO DcsTransfers
                    (ConnectionEventId, FilePath, FileSize, TargetFolder,
                     SyncStatusId, ChecksumSHA256, PeerBlackID, TransferredAt)
                VALUES
                    (@evId, @path, @size, @folder,
                     @statusId, @checksum, @peer, @at)";
            cmd.Parameters.AddWithValue("@evId",     (object?)rec.ConnectionEventId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@path",     rec.FilePath);
            cmd.Parameters.AddWithValue("@size",     rec.FileSize);
            cmd.Parameters.AddWithValue("@folder",   (object?)rec.TargetFolder   ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@statusId", (object?)rec.SyncStatusId   ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@checksum", (object?)rec.ChecksumSHA256 ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@peer",     (object?)rec.PeerBlackID    ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@at",       rec.TransferredAt.ToString("O"));
            cmd.ExecuteNonQuery();
        }
        catch { /* non-critical */ }
    }

    public List<DcsTransferRecord> GetRecentTransfers(int count = 50)
    {
        var list = new List<DcsTransferRecord>();
        try
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT Id, ConnectionEventId, FilePath, FileSize, TargetFolder,
                       SyncStatusId, ChecksumSHA256, PeerBlackID, TransferredAt
                FROM DcsTransfers
                ORDER BY TransferredAt DESC
                LIMIT @count";
            cmd.Parameters.AddWithValue("@count", count);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add(new DcsTransferRecord
                {
                    Id                = r.GetInt32(0),
                    ConnectionEventId = r.IsDBNull(1) ? null : r.GetInt32(1),
                    FilePath          = r.GetString(2),
                    FileSize          = r.GetInt64(3),
                    TargetFolder      = r.IsDBNull(4) ? null : r.GetString(4),
                    SyncStatusId      = r.IsDBNull(5) ? null : r.GetInt32(5),
                    ChecksumSHA256    = r.IsDBNull(6) ? null : r.GetString(6),
                    PeerBlackID       = r.IsDBNull(7) ? null : r.GetString(7),
                    TransferredAt     = DateTime.Parse(r.GetString(8))
                });
        }
        catch { }
        return list;
    }

    public void DeleteOldTransfers(DateTime beforeDate)
    {
        try
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM DcsTransfers WHERE TransferredAt < @before";
            cmd.Parameters.AddWithValue("@before", beforeDate.ToString("O"));
            cmd.ExecuteNonQuery();
        }
        catch { }
    }
}
