using Microsoft.Data.Sqlite;

namespace AsmodayCat.Core.Data;

public class DatabaseMigrator
{
    private readonly AsmodayDatabase _db;

    public DatabaseMigrator(AsmodayDatabase db) => _db = db;

    public void Initialize()
    {
        using var conn = _db.CreateConnection();
        conn.Open();

        Execute(conn, "PRAGMA journal_mode=WAL;");
        Execute(conn, "PRAGMA foreign_keys=ON;");

        foreach (var sql in DatabaseSchema.CreateTables)
            Execute(conn, sql);

        foreach (var sql in DatabaseSchema.CreateIndexes)
        {
            try { Execute(conn, sql); }
            catch { }
        }

        // Seed singleton HardwareSettings row
        Execute(conn, @"
            INSERT OR IGNORE INTO HardwareSettings
                (Id, PreferredDevice, ContextWindowSize, MaxVramAllocationMb, CpuThreads, AllowCpuFallback, UpdatedAt)
            VALUES (1, 'Auto', 4096, 0, 4, 1, @now);",
            ("@now", DateTime.UtcNow.ToString("O")));
    }

    private static void Execute(SqliteConnection conn, string sql,
        params (string Name, object Value)[] parameters)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value);
        cmd.ExecuteNonQuery();
    }
}
