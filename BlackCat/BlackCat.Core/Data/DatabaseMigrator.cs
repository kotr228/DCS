using Microsoft.Data.Sqlite;
using System;

namespace BlackCat.Core.Data;

/// <summary>
/// Система міграцій БД - автоматично оновлює структуру
/// </summary>
public class DatabaseMigrator
{
    private readonly string _connectionString;
    private readonly string _dbPath;

    public DatabaseMigrator(string dbPath)
    {
        _dbPath = dbPath;
        _connectionString = $"Data Source={_dbPath}";
    }

    /// <summary>
    /// Виконати всі необхідні міграції
    /// </summary>
    public void RunMigrations()
    {
        Console.WriteLine("=== ВИКОНАННЯ МІГРАЦІЙ БД ===");

        var validator = new DatabaseValidator(_dbPath);
        int currentVersion = validator.GetDatabaseVersion();

        Console.WriteLine($"📌 Поточна версія БД: {currentVersion}");
        Console.WriteLine($"📌 Цільова версія БД: {DatabaseSchema.CurrentVersion}");

        if (currentVersion >= DatabaseSchema.CurrentVersion)
        {
            Console.WriteLine("✅ База даних актуальна. Міграції не потрібні.");
            return;
        }

        // Спочатку переконуємось що всі таблиці існують
        EnsureTablesExist();

        // Виконуємо міграції послідовно
        if (currentVersion < 1)
        {
            MigrateToVersion1();
        }

        if (currentVersion < 2)
        {
            MigrateToVersion2();
        }

        Console.WriteLine("=== МІГРАЦІЇ ЗАВЕРШЕНО ===\n");
    }

    /// <summary>
    /// Переконатися що всі таблиці існують
    /// </summary>
    private void EnsureTablesExist()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        Console.WriteLine("➡ Перевірка наявності таблиць...");

        foreach (var sql in DatabaseSchema.CreateTables)
        {
            try
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = sql;
                cmd.ExecuteNonQuery();

                // Виводимо ім'я таблиці якщо вона була створена
                if (sql.Contains("CREATE TABLE IF NOT EXISTS"))
                {
                    var tableName = ExtractTableName(sql);
                    if (!string.IsNullOrEmpty(tableName))
                    {
                        Console.WriteLine($"   ✓ {tableName}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ⚠ Помилка створення таблиці: {ex.Message}");
            }
        }

        // Створюємо індекси
        foreach (var sql in DatabaseSchema.CreateIndexes)
        {
            try
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = sql;
                cmd.ExecuteNonQuery();
            }
            catch
            {
                // Індекс вже існує - ігноруємо
            }
        }
    }

    /// <summary>
    /// Міграція до версії 1: Початкова схема з довідниками
    /// </summary>
    private void MigrateToVersion1()
    {
        Console.WriteLine("➡ Міграція до версії 1...");

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        try
        {
            // Додаємо дефолтні значення довідників
            var seeder = new DataSeeder(new BlackCatDatabase(_dbPath));
            seeder.SeedAll();

            // Записуємо версію
            RecordVersion(1, "Початкова нормалізована схема з довідниками");

            Console.WriteLine("✅ Міграція до версії 1 завершена");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Помилка міграції до версії 1: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Міграція до версії 2: Додавання полів для нормалізації
    /// </summary>
    private void MigrateToVersion2()
    {
        Console.WriteLine("➡ Міграція до версії 2...");

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        try
        {
            // Додаємо нові поля якщо їх немає

            // LocalBlackID: RoleId, CityId, SignatureCreatedAt
            AddColumnIfNotExists("LocalBlackID", "RoleId", "INTEGER", "1");
            AddColumnIfNotExists("LocalBlackID", "CityId", "INTEGER", "1");
            AddColumnIfNotExists("LocalBlackID", "SignatureCreatedAt", "TEXT", "CURRENT_TIMESTAMP");

            // PeerNodes: StatusId
            AddColumnIfNotExists("PeerNodes", "StatusId", "INTEGER");

            // ConnectionEvents: EventTypeId, InitiatorBlackID, TargetBlackID
            AddColumnIfNotExists("ConnectionEvents", "EventTypeId", "INTEGER", "1");
            AddColumnIfNotExists("ConnectionEvents", "InitiatorBlackID", "TEXT");
            AddColumnIfNotExists("ConnectionEvents", "TargetBlackID", "TEXT");

            // Заповнюємо RoleId та CityId для існуючих записів
            UpdateExistingBlackIDs();

            // Записуємо версію
            RecordVersion(2, "Нормалізація з FK та модуль мапи серверів");

            Console.WriteLine("✅ Міграція до версії 2 завершена");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Помилка міграції до версії 2: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Оновити існуючі Black-ID з RoleId та CityId
    /// </summary>
    private void UpdateExistingBlackIDs()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        Console.WriteLine("   ➡ Оновлення існуючих Black-ID з FK...");

        try
        {
            // Оновлюємо RoleId на основі текстового поля Role
            var updateRolesSql = @"
                UPDATE LocalBlackID
                SET RoleId = (
                    SELECT Id FROM Roles
                    WHERE Roles.Name = LocalBlackID.Role
                )
                WHERE RoleId IS NULL OR RoleId = 1;
            ";

            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = updateRolesSql;
                int rows = cmd.ExecuteNonQuery();
                Console.WriteLine($"   ✓ Оновлено RoleId для {rows} записів");
            }

            // Оновлюємо CityId на основі текстового поля City
            var updateCitiesSql = @"
                UPDATE LocalBlackID
                SET CityId = (
                    SELECT Id FROM Cities
                    WHERE Cities.Name = LocalBlackID.City
                )
                WHERE CityId IS NULL OR CityId = 1;
            ";

            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = updateCitiesSql;
                int rows = cmd.ExecuteNonQuery();
                Console.WriteLine($"   ✓ Оновлено CityId для {rows} записів");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   ⚠ Помилка оновлення FK: {ex.Message}");
        }
    }

    /// <summary>
    /// Додати колонку якщо її немає
    /// </summary>
    private void AddColumnIfNotExists(string tableName, string columnName, string columnType, string? defaultValue = null)
    {
        if (ColumnExists(tableName, columnName))
        {
            return;
        }

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var cmd = connection.CreateCommand();
        string alterSql = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnType}";

        if (!string.IsNullOrEmpty(defaultValue))
        {
            alterSql += $" DEFAULT {defaultValue}";
        }

        cmd.CommandText = alterSql + ";";

        try
        {
            cmd.ExecuteNonQuery();
            Console.WriteLine($"   ✓ Додано колонку {tableName}.{columnName}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   ⚠ Помилка додавання колонки: {ex.Message}");
        }
    }

    /// <summary>
    /// Перевірити чи існує колонка
    /// </summary>
    private bool ColumnExists(string tableName, string columnName)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({tableName});";
        using var reader = cmd.ExecuteReader();

        while (reader.Read())
        {
            string colName = reader.GetString(1);
            if (colName.Equals(columnName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Перевірити чи існує таблиця
    /// </summary>
    private bool TableExists(string tableName)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT name FROM sqlite_master WHERE type='table' AND name='{tableName}';";
        var result = cmd.ExecuteScalar();

        return result != null;
    }

    /// <summary>
    /// Записати версію БД
    /// </summary>
    private void RecordVersion(int version, string description)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO DatabaseVersion (Version, AppliedAt, Description)
            VALUES (@version, @appliedAt, @description);
        ";
        cmd.Parameters.AddWithValue("@version", version);
        cmd.Parameters.AddWithValue("@appliedAt", DateTime.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("@description", description);

        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Витягти ім'я таблиці з SQL
    /// </summary>
    private string ExtractTableName(string sql)
    {
        try
        {
            var start = sql.IndexOf("EXISTS") + 7;
            var end = sql.IndexOf("(");
            return sql.Substring(start, end - start).Trim();
        }
        catch
        {
            return string.Empty;
        }
    }
}
