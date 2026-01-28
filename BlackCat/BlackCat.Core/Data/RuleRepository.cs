using Microsoft.Data.Sqlite;
using BlackCat.Shared.Models;
using BlackCat.Shared.Enums;
using NetworkProtocol = BlackCat.Shared.Enums.ProtocolType;

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
        InitializeDefaultRules();
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
                CreatedAt TEXT NOT NULL,
                PortRange TEXT,
                ApplicationPath TEXT,
                ProcessName TEXT,
                Description TEXT,
                Tags TEXT
            );

            CREATE INDEX IF NOT EXISTS idx_priority ON FilterRules(Priority);
            CREATE INDEX IF NOT EXISTS idx_enabled ON FilterRules(IsEnabled);
            CREATE INDEX IF NOT EXISTS idx_process ON FilterRules(ProcessName);
        ";

        using var command = new SqliteCommand(createTableSql, connection);
        command.ExecuteNonQuery();

        // Додати нові колонки до існуючих таблиць (міграція)
        try
        {
            using var alterCmd1 = new SqliteCommand("ALTER TABLE FilterRules ADD COLUMN PortRange TEXT", connection);
            alterCmd1.ExecuteNonQuery();
        }
        catch { /* Колонка вже існує */ }

        try
        {
            using var alterCmd2 = new SqliteCommand("ALTER TABLE FilterRules ADD COLUMN ApplicationPath TEXT", connection);
            alterCmd2.ExecuteNonQuery();
        }
        catch { /* Колонка вже існує */ }

        try
        {
            using var alterCmd3 = new SqliteCommand("ALTER TABLE FilterRules ADD COLUMN ProcessName TEXT", connection);
            alterCmd3.ExecuteNonQuery();
        }
        catch { /* Колонка вже існує */ }

        try
        {
            using var alterCmd4 = new SqliteCommand("ALTER TABLE FilterRules ADD COLUMN Description TEXT", connection);
            alterCmd4.ExecuteNonQuery();
        }
        catch { /* Колонка вже існує */ }

        try
        {
            using var alterCmd5 = new SqliteCommand("ALTER TABLE FilterRules ADD COLUMN Tags TEXT", connection);
            alterCmd5.ExecuteNonQuery();
        }
        catch { /* Колонка вже існує */ }
    }

    /// <summary>
    /// Ініціалізація дефолтних правил (якщо БД порожня)
    /// </summary>
    private void InitializeDefaultRules()
    {
        var existingRules = GetAllRules();
        if (existingRules.Count > 0) return; // Вже є правила

        // Дефолтне правило 1: Дозволити localhost
        AddRule(new FilterRule
        {
            Name = "Дозволити localhost",
            IPAddress = "127.0.0.1",
            Port = 0,
            Protocol = NetworkProtocol.Any,
            Action = FilterAction.Allow,
            Direction = TrafficDirection.Both,
            IsEnabled = true,
            Priority = 1
        });

        // Дефолтне правило 2: Дозволити локальну мережу 192.168.x.x
        AddRule(new FilterRule
        {
            Name = "Дозволити локальну мережу 192.168.x.x",
            IPAddress = "192.168.0.0/16",
            Port = 0,
            Protocol = NetworkProtocol.Any,
            Action = FilterAction.Allow,
            Direction = TrafficDirection.Both,
            IsEnabled = true,
            Priority = 10
        });

        // Дефолтне правило 3: Дозволити локальну мережу 10.x.x.x
        AddRule(new FilterRule
        {
            Name = "Дозволити локальну мережу 10.x.x.x",
            IPAddress = "10.0.0.0/8",
            Port = 0,
            Protocol = NetworkProtocol.Any,
            Action = FilterAction.Allow,
            Direction = TrafficDirection.Both,
            IsEnabled = true,
            Priority = 10
        });

        // Дефолтне правило 4: Заблокувати підозрілі порти
        AddRule(new FilterRule
        {
            Name = "Блокувати підозрілі порти (telnet)",
            IPAddress = "",
            Port = 23,
            Protocol = NetworkProtocol.TCP,
            Action = FilterAction.Block,
            Direction = TrafficDirection.Inbound,
            IsEnabled = true,
            Priority = 5
        });
    }

    /// <summary>
    /// Додати правило
    /// </summary>
    public int AddRule(FilterRule rule)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        string sql = @"
            INSERT INTO FilterRules (Name, IPAddress, Port, Protocol, Action, Direction, IsEnabled, Priority, CreatedAt,
                                    PortRange, ApplicationPath, ProcessName, Description, Tags)
            VALUES (@Name, @IPAddress, @Port, @Protocol, @Action, @Direction, @IsEnabled, @Priority, @CreatedAt,
                    @PortRange, @ApplicationPath, @ProcessName, @Description, @Tags);
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
        command.Parameters.AddWithValue("@PortRange", rule.PortRange ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@ApplicationPath", rule.ApplicationPath ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@ProcessName", rule.ProcessName ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@Description", rule.Description ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@Tags", rule.Tags ?? (object)DBNull.Value);

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
                Priority = @Priority,
                PortRange = @PortRange,
                ApplicationPath = @ApplicationPath,
                ProcessName = @ProcessName,
                Description = @Description,
                Tags = @Tags
            WHERE Id = @Id
        ";

        using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("@Id", rule.Id);
        command.Parameters.AddWithValue("@Name", rule.Name);
        command.Parameters.AddWithValue("@IPAddress", rule.IPAddress ?? string.Empty);
        command.Parameters.AddWithValue("@Port", rule.Port);
        command.Parameters.AddWithValue("@Protocol", (int)rule.Protocol);
        command.Parameters.AddWithValue("@PortRange", rule.PortRange ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@ApplicationPath", rule.ApplicationPath ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@ProcessName", rule.ProcessName ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@Description", rule.Description ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@Tags", rule.Tags ?? (object)DBNull.Value);
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
            Id = reader.GetInt32(reader.GetOrdinal("Id")),
            Name = reader.GetString(reader.GetOrdinal("Name")),
            IPAddress = reader.GetString(reader.GetOrdinal("IPAddress")),
            Port = reader.GetInt32(reader.GetOrdinal("Port")),
            Protocol = (NetworkProtocol)reader.GetInt32(reader.GetOrdinal("Protocol")),
            Action = (FilterAction)reader.GetInt32(reader.GetOrdinal("Action")),
            Direction = (TrafficDirection)reader.GetInt32(reader.GetOrdinal("Direction")),
            IsEnabled = reader.GetInt32(reader.GetOrdinal("IsEnabled")) == 1,
            Priority = reader.GetInt32(reader.GetOrdinal("Priority")),
            CreatedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("CreatedAt"))),
            PortRange = GetNullableString(reader, "PortRange"),
            ApplicationPath = GetNullableString(reader, "ApplicationPath"),
            ProcessName = GetNullableString(reader, "ProcessName"),
            Description = GetNullableString(reader, "Description"),
            Tags = GetNullableString(reader, "Tags")
        };
    }

    /// <summary>
    /// Helper для читання nullable string
    /// </summary>
    private static string? GetNullableString(SqliteDataReader reader, string columnName)
    {
        try
        {
            int ordinal = reader.GetOrdinal(columnName);
            if (reader.IsDBNull(ordinal))
                return null;
            return reader.GetString(ordinal);
        }
        catch
        {
            // Колонка не існує (стара версія БД)
            return null;
        }
    }

    public void Dispose()
    {
        // Cleanup if needed
    }
}
