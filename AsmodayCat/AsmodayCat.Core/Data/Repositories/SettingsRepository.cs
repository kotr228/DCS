using Microsoft.Data.Sqlite;

namespace AsmodayCat.Core.Data.Repositories;

public class SettingsRepository
{
    private readonly AsmodayDatabase _db;

    public SettingsRepository(AsmodayDatabase db) => _db = db;

    public string? GetSetting(string key)
    {
        using var conn = _db.CreateConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Value FROM AppSettings WHERE Key = @key;";
        cmd.Parameters.AddWithValue("@key", key);
        return cmd.ExecuteScalar() as string;
    }

    public void SetSetting(string key, string value)
    {
        using var conn = _db.CreateConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT OR REPLACE INTO AppSettings (Key, Value, UpdatedAt)
            VALUES (@key, @value, @now);";
        cmd.Parameters.AddWithValue("@key",   key);
        cmd.Parameters.AddWithValue("@value", value);
        cmd.Parameters.AddWithValue("@now",   DateTime.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
    }
}
