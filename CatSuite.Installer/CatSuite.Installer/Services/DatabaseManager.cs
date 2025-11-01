using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;
using CatSuite.Installer.Models;

namespace CatSuite.Installer.Services
{
    public class DatabaseManager
    {
        private readonly string _dbPath;
        private readonly string _connectionString;

        public DatabaseManager(string dbFileName = "CatSuiteInstaller.db")
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string catSuiteFolder = Path.Combine(appData, "CatSuite");

            if (!Directory.Exists(catSuiteFolder))
                Directory.CreateDirectory(catSuiteFolder);

            _dbPath = Path.Combine(catSuiteFolder, dbFileName);
            _connectionString = $"Data Source={_dbPath}";
        }

        public void InitializeDatabase()
        {
            Console.WriteLine("=== ІНІЦІАЛІЗАЦІЯ БД ІНСТАЛЯТОРА ===");
            Console.WriteLine($"📂 Шлях до БД: {_dbPath}");

            try
            {
                bool exists = File.Exists(_dbPath);

                if (!exists)
                {
                    Console.WriteLine("➡ Створення нової БД...");
                    CreateSchema();
                }
                else
                {
                    Console.WriteLine("✅ БД існує.");
                    ValidateAndMigrate();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Критична помилка: {ex.Message}");
                throw;
            }

            Console.WriteLine("=== ІНІЦІАЛІЗАЦІЯ ЗАВЕРШЕНА ===\n");
        }

        private void CreateSchema()
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            foreach (var sql in DatabaseSchema.CreateTables)
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = sql;
                cmd.ExecuteNonQuery();

                var tableName = ExtractTableName(sql);
                Console.WriteLine($"   + {tableName} ✓");
            }

            Console.WriteLine("✅ Схема створена.");
        }

        private void ValidateAndMigrate()
        {
            // Перевірка та оновлення схеми за необхідності
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();

                var requiredTables = new[] { "InstalledPackages", "InstallationLogs", "DependencyRelations" };

                foreach (var table in requiredTables)
                {
                    using var cmd = connection.CreateCommand();
                    cmd.CommandText = $"SELECT name FROM sqlite_master WHERE type='table' AND name='{table}';";
                    var result = cmd.ExecuteScalar();

                    if (result == null)
                    {
                        Console.WriteLine($"⚠️ Таблиця {table} відсутня. Міграція...");
                        CreateSchema();
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Помилка валідації: {ex.Message}");
            }
        }

        private string ExtractTableName(string sql)
        {
            var parts = sql.Split(new[] { "TABLE", "(" }, StringSplitOptions.None);
            if (parts.Length > 1)
            {
                return parts[1].Trim().Split(' ')[0];
            }
            return "Unknown";
        }

        public SqliteConnection GetConnection()
        {
            return new SqliteConnection(_connectionString);
        }

        // ========== CRUD для InstalledPackages ==========

        public void SaveInstalledPackage(InstalledPackage package)
        {
            using var connection = GetConnection();
            connection.Open();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                INSERT OR REPLACE INTO InstalledPackages 
                (PackageId, Name, Version, InstallPath, InstalledAt, UpdatedAt, MsiProductCode)
                VALUES (@PackageId, @Name, @Version, @InstallPath, @InstalledAt, @UpdatedAt, @MsiProductCode)";

            cmd.Parameters.AddWithValue("@PackageId", package.PackageId);
            cmd.Parameters.AddWithValue("@Name", package.Name);
            cmd.Parameters.AddWithValue("@Version", package.Version);
            cmd.Parameters.AddWithValue("@InstallPath", package.InstallPath ?? "");
            cmd.Parameters.AddWithValue("@InstalledAt", package.InstalledAt);
            cmd.Parameters.AddWithValue("@UpdatedAt", package.UpdatedAt ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@MsiProductCode", package.MsiProductCode ?? "");

            cmd.ExecuteNonQuery();
        }

        public InstalledPackage GetInstalledPackage(string packageId)
        {
            using var connection = GetConnection();
            connection.Open();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT * FROM InstalledPackages WHERE PackageId = @PackageId";
            cmd.Parameters.AddWithValue("@PackageId", packageId);

            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                return new InstalledPackage
                {
                    Id = reader.GetInt32(0),
                    PackageId = reader.GetString(1),
                    Name = reader.GetString(2),
                    Version = reader.GetString(3),
                    InstallPath = reader.GetString(4),
                    InstalledAt = reader.GetDateTime(5),
                    UpdatedAt = reader.IsDBNull(6) ? null : reader.GetDateTime(6),
                    MsiProductCode = reader.GetString(7)
                };
            }

            return null;
        }

        public List<InstalledPackage> GetAllInstalledPackages()
        {
            var packages = new List<InstalledPackage>();

            using var connection = GetConnection();
            connection.Open();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT * FROM InstalledPackages ORDER BY InstalledAt DESC";

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                packages.Add(new InstalledPackage
                {
                    Id = reader.GetInt32(0),
                    PackageId = reader.GetString(1),
                    Name = reader.GetString(2),
                    Version = reader.GetString(3),
                    InstallPath = reader.GetString(4),
                    InstalledAt = reader.GetDateTime(5),
                    UpdatedAt = reader.IsDBNull(6) ? null : reader.GetDateTime(6),
                    MsiProductCode = reader.GetString(7)
                });
            }

            return packages;
        }

        public void RemoveInstalledPackage(string packageId)
        {
            using var connection = GetConnection();
            connection.Open();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = "DELETE FROM InstalledPackages WHERE PackageId = @PackageId";
            cmd.Parameters.AddWithValue("@PackageId", packageId);
            cmd.ExecuteNonQuery();
        }

        // ========== Логи ==========

        public void AddInstallationLog(InstallationLog log)
        {
            using var connection = GetConnection();
            connection.Open();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO InstallationLogs 
                (PackageId, Action, Version, Success, ErrorMessage, Timestamp, LogFilePath)
                VALUES (@PackageId, @Action, @Version, @Success, @ErrorMessage, @Timestamp, @LogFilePath)";

            cmd.Parameters.AddWithValue("@PackageId", log.PackageId);
            cmd.Parameters.AddWithValue("@Action", log.Action);
            cmd.Parameters.AddWithValue("@Version", log.Version);
            cmd.Parameters.AddWithValue("@Success", log.Success);
            cmd.Parameters.AddWithValue("@ErrorMessage", log.ErrorMessage ?? "");
            cmd.Parameters.AddWithValue("@Timestamp", log.Timestamp);
            cmd.Parameters.AddWithValue("@LogFilePath", log.LogFilePath ?? "");

            cmd.ExecuteNonQuery();
        }

        public List<InstallationLog> GetInstallationLogs(string packageId = null, int limit = 100)
        {
            var logs = new List<InstallationLog>();

            using var connection = GetConnection();
            connection.Open();

            using var cmd = connection.CreateCommand();

            if (string.IsNullOrEmpty(packageId))
            {
                cmd.CommandText = $"SELECT * FROM InstallationLogs ORDER BY Timestamp DESC LIMIT {limit}";
            }
            else
            {
                cmd.CommandText = $"SELECT * FROM InstallationLogs WHERE PackageId = @PackageId ORDER BY Timestamp DESC LIMIT {limit}";
                cmd.Parameters.AddWithValue("@PackageId", packageId);
            }

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                logs.Add(new InstallationLog
                {
                    Id = reader.GetInt32(0),
                    PackageId = reader.GetString(1),
                    Action = reader.GetString(2),
                    Version = reader.GetString(3),
                    Success = reader.GetBoolean(4),
                    ErrorMessage = reader.GetString(5),
                    Timestamp = reader.GetDateTime(6),
                    LogFilePath = reader.GetString(7)
                });
            }

            return logs;
        }
    }
}