using Microsoft.Data.Sqlite;
using System;
using System.IO;

namespace DocControlService.Models
{
    public class DatabaseManager
    {
        private readonly string _dbPath;
        private readonly string _connectionString;
        private readonly DatabaseValidator _validator;

        public DatabaseManager(string dbFileName = "DocControl.db")
        {
            _dbPath = Path.Combine(AppContext.BaseDirectory, dbFileName);
            _connectionString = $"Data Source={_dbPath}";
            _validator = new DatabaseValidator(_dbPath);

            InitializeDatabase();
        }

        private void InitializeDatabase()
        {
            Console.WriteLine("=== ІНІЦІАЛІЗАЦІЯ БАЗИ ДАНИХ ===");

            try
            {
                bool exists = _validator.DatabaseExists();
                Console.WriteLine($"📂 Перевірка існування БД → {(exists ? "існує" : "не існує")}");

                if (!exists)
                {
                    Console.WriteLine("➡ Створюємо нову БД...");
                    CreateSchema();
                }
                else
                {
                    Console.WriteLine("➡ Перевірка валідності існуючої БД...");

                    bool valid = false;
                    try
                    {
                        valid = _validator.ValidateStructure();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"❌ Помилка перевірки структури: {ex.Message}");
                    }

                    if (!valid)
                    {
                        Console.WriteLine("⚠️ База пошкоджена або структура не відповідає.");
                        Console.WriteLine("➡ Видаляємо існуючу БД...");

                        // Закриваємо всі відкриті конекшени
                        GC.Collect();
                        GC.WaitForPendingFinalizers();

                        try
                        {
                            _validator.DropDatabase();
                            Console.WriteLine("🗑️ Існуюча БД видалена.");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"❌ Не вдалося видалити БД: {ex.Message}");
                        }

                        Console.WriteLine("➡ Створюємо нову БД...");
                        CreateSchema();
                    }
                    else
                    {
                        Console.WriteLine("✅ База існує і структура валідна.");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Критична помилка при ініціалізації БД: {ex.Message}");
            }

            Console.WriteLine("=== ІНІЦІАЛІЗАЦІЯ ЗАВЕРШЕНА ===\n");
        }


        private void CreateSchema()
        {
            Console.WriteLine("➡ Створення структури таблиць...");

            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            foreach (var sql in DatabaseSchema.CreateTables)
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = sql;
                cmd.ExecuteNonQuery();

                // маленький лайфхак: вичищаю перший рядок до CREATE TABLE щоб в логах було видно назву
                var firstLine = cmd.CommandText.Split('(')[0].Trim();
                Console.WriteLine($"   + {firstLine} ✓");
            }

            Console.WriteLine("✅ Усі таблиці створено.");
        }

        // 🔹 метод перевірки запитів
        public bool ExecuteTestQuery()
        {
            Console.WriteLine("➡ Виконую тестовий запит...");

            try
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();

                using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM directory;";
                var count = Convert.ToInt32(cmd.ExecuteScalar());

                Console.WriteLine($"✅ Перевірка запиту успішна. В таблиці directory {count} записів.");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Помилка при перевірці БД: {ex.Message}");
                return false;
            }
        }

        public SqliteConnection GetConnection()
        {
            return new SqliteConnection(_connectionString);
        }

    }
}
