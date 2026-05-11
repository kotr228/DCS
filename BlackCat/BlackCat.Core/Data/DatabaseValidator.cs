using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.IO;

namespace BlackCat.Core.Data;

/// <summary>
/// Валідатор структури бази даних
/// </summary>
public class DatabaseValidator
{
    private readonly string _dbPath;
    private readonly string _connectionString;

    public DatabaseValidator(string dbPath)
    {
        _dbPath = dbPath;
        _connectionString = $"Data Source={_dbPath}";
    }

    /// <summary>
    /// Перевірити чи існує файл БД
    /// </summary>
    public bool DatabaseExists()
    {
        bool exists = File.Exists(_dbPath);
        Console.WriteLine(exists ? "ℹ Файл БД знайдено." : "ℹ Файл БД відсутній.");
        return exists;
    }

    /// <summary>
    /// Перевірити структуру БД
    /// </summary>
    public bool ValidateStructure()
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var requiredTables = new List<string>
            {
                // Довідники
                "Roles",
                "Cities",
                "EventTypes",
                "ConnectionStatuses",
                // Основні таблиці
                "LocalBlackID",
                "PeerNodes",
                "ConnectionEvents",
                // Модуль мапи
                "Servers",
                "ServerLocations",
                // Версії
                "DatabaseVersion"
            };

            bool allTablesExist = true;

            foreach (var table in requiredTables)
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = $"SELECT name FROM sqlite_master WHERE type='table' AND name='{table}';";
                var result = cmd.ExecuteScalar();

                if (result == null)
                {
                    Console.WriteLine($"❌ Не знайдено таблицю: {table}");
                    allTablesExist = false;
                }
                else
                {
                    Console.WriteLine($"✅ Таблиця {table} знайдена.");
                }
            }

            return allTablesExist;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Помилка при перевірці структури: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Отримати поточну версію БД
    /// </summary>
    public int GetDatabaseVersion()
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            // Перевіряємо чи існує таблиця версій
            using var checkCmd = connection.CreateCommand();
            checkCmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='DatabaseVersion';";
            var tableExists = checkCmd.ExecuteScalar();

            if (tableExists == null)
            {
                return 0; // База даних версії 0 (до системи міграцій)
            }

            // Отримуємо останню версію
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT MAX(Version) FROM DatabaseVersion;";
            var result = cmd.ExecuteScalar();

            if (result == null || result == DBNull.Value)
            {
                return 0;
            }

            return Convert.ToInt32(result);
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// Видалити базу даних (використовувати обережно!)
    /// </summary>
    public void DropDatabase()
    {
        if (File.Exists(_dbPath))
        {
            try
            {
                // Закриваємо всі з'єднання
                SqliteConnection.ClearAllPools();
                GC.Collect();
                GC.WaitForPendingFinalizers();

                File.Delete(_dbPath);
                Console.WriteLine("⚠ Стара БД видалена.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Помилка видалення БД: {ex.Message}");
                throw;
            }
        }
    }

    /// <summary>
    /// Перевірити чи таблиця містить дані
    /// </summary>
    public bool TableHasData(string tableName)
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = $"SELECT COUNT(*) FROM {tableName};";
            var count = Convert.ToInt32(cmd.ExecuteScalar());

            return count > 0;
        }
        catch
        {
            return false;
        }
    }
}
