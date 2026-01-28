using Microsoft.Data.Sqlite;
using BlackCat.Shared.Models;
using BlackCat.Shared.Enums;

namespace BlackCat.Core.Data;

/// <summary>
/// Репозиторій для роботи з правилами фільтрації в SQLite
/// </summary>
public class RuleRepository : IDisposable
{
    private readonly string _connectionString;

    public RuleRepository(string databasePath = "blackcat.db")
    {
        _connectionString = $"Data Source={databasePath}";
        InitializeDatabase();
    }

    /// <summary>
    /// Ініціалізація схеми бази даних
    /// </summary>
    private void InitializeDatabase()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        string createTableSql = @"
            CREATE TABLE IF NOT EXISTS FilterRules (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                IPAddress TEXT,
                Port INTEGER DEFAULT 0,
                Protocol INTEGER DEFAULT 0,
                Action INTEGER NOT NULL,
                Direction INTEGER NOT NULL,
                IsEnabled INTEGER DEFAULT 1,
                Priority INTEGER DEFAULT 100,
                CreatedAt TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_priority ON FilterRules(Priority);
            CREATE INDEX IF NOT EXISTS idx_enabled ON FilterRules(IsEnabled);
        ";

        using var command = new SqliteCommand(createTableSql, connection);
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Додати правило
    /// </summary>
    public int AddRule(FilterRule rule)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        string sql = @"
            INSERT INTO FilterRules (Name, IPAddress, Port, Protocol, Action, Direction, IsEnabled, Priority, CreatedAt)
            VALUES (@Name, @IPAddress, @Port, @Protocol, @Action, @Direction, @IsEnabled, @Priority, @CreatedAt);
            SELECT last_insert_rowid();
        ";

        using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("@Name", rule.Name);
        command.Parameters.AddWithValue("@IPAddress", rule.IPAddress ?? string.Empty);
        command.Parameters.AddWithValue("@Port", rule.Port);
        command.Parameters.AddWithValue("@Protocol", (int)rule.Protocol);
        command.Parameters.AddWithValue("@Action", (int)rule.Action);
        command.Parameters.AddWithValue("@Direction", (int)rule.Direction);
        command.Parameters.AddWithValue("@IsEnabled", rule.IsEnabled ? 1 : 0);
        command.Parameters.AddWithValue("@Priority", rule.Priority);
        command.Parameters.AddWithValue("@CreatedAt", rule.CreatedAt.ToString("O"));

        return Convert.ToInt32(command.ExecuteScalar());
    }

    /// <summary>
    /// Оновити правило
    /// </summary>
    public bool UpdateRule(FilterRule rule)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        string sql = @"
            UPDATE FilterRules
            SET Name = @Name,
                IPAddress = @IPAddress,
                Port = @Port,
                Protocol = @Protocol,
                Action = @Action,
                Direction = @Direction,
                IsEnabled = @IsEnabled,
                Priority = @Priority
            WHERE Id = @Id
        ";

        using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("@Id", rule.Id);
        command.Parameters.AddWithValue("@Name", rule.Name);
        command.Parameters.AddWithValue("@IPAddress", rule.IPAddress ?? string.Empty);
        command.Parameters.AddWithValue("@Port", rule.Port);
        command.Parameters.AddWithValue("@Protocol", (int)rule.Protocol);
        command.Parameters.AddWithValue("@Action", (int)rule.Action);
        command.Parameters.AddWithValue("@Direction", (int)rule.Direction);
        command.Parameters.AddWithValue("@IsEnabled", rule.IsEnabled ? 1 : 0);
        command.Parameters.AddWithValue("@Priority", rule.Priority);

        return command.ExecuteNonQuery() > 0;
    }

    /// <summary>
    /// Видалити правило
    /// </summary>
    public bool DeleteRule(int ruleId)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        string sql = "DELETE FROM FilterRules WHERE Id = @Id";

        using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("@Id", ruleId);

        return command.ExecuteNonQuery() > 0;
    }

    /// <summary>
    /// Отримати правило за ID
    /// </summary>
    public FilterRule? GetRule(int ruleId)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        string sql = "SELECT * FROM FilterRules WHERE Id = @Id";

        using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("@Id", ruleId);

        using var reader = command.ExecuteReader();
        return reader.Read() ? MapToFilterRule(reader) : null;
    }

    /// <summary>
    /// Отримати всі правила
    /// </summary>
    public List<FilterRule> GetAllRules()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        string sql = "SELECT * FROM FilterRules ORDER BY Priority ASC";

        using var command = new SqliteCommand(sql, connection);
        using var reader = command.ExecuteReader();

        var rules = new List<FilterRule>();
        while (reader.Read())
        {
            rules.Add(MapToFilterRule(reader));
        }

        return rules;
    }

    /// <summary>
    /// Отримати активні правила
    /// </summary>
    public List<FilterRule> GetActiveRules()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        string sql = "SELECT * FROM FilterRules WHERE IsEnabled = 1 ORDER BY Priority ASC";

        using var command = new SqliteCommand(sql, connection);
        using var reader = command.ExecuteReader();

        var rules = new List<FilterRule>();
        while (reader.Read())
        {
            rules.Add(MapToFilterRule(reader));
        }

        return rules;
    }

    /// <summary>
    /// Маппінг з SqliteDataReader в FilterRule
    /// </summary>
    private FilterRule MapToFilterRule(SqliteDataReader reader)
    {
        return new FilterRule
        {
            Id = reader.GetInt32(0),
            Name = reader.GetString(1),
            IPAddress = reader.GetString(2),
            Port = reader.GetInt32(3),
            Protocol = (BlackCat.Shared.Models.ProtocolType)reader.GetInt32(4),
            Action = (FilterAction)reader.GetInt32(5),
            Direction = (TrafficDirection)reader.GetInt32(6),
            IsEnabled = reader.GetInt32(7) == 1,
            Priority = reader.GetInt32(8),
            CreatedAt = DateTime.Parse(reader.GetString(9))
        };
    }

    public void Dispose()
    {
        // Cleanup if needed
    }
}
