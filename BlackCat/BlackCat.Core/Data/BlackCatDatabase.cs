using Microsoft.Data.Sqlite;
using BlackCat.Shared.Models;

namespace BlackCat.Core.Data;

/// <summary>
/// Головний клас для роботи з базою даних BlackCat
/// </summary>
public class BlackCatDatabase : IDisposable
{
    private readonly string _connectionString;
    private readonly string _databasePath;

    public BlackCatDatabase(string databasePath = "blackcat.db")
    {
        _databasePath = databasePath;
        _connectionString = $"Data Source={databasePath}";
        InitializeDatabase();
    }

    /// <summary>
    /// Ініціалізація бази даних з підтримкою міграцій
    /// </summary>
    private void InitializeDatabase()
    {
        var validator = new DatabaseValidator(_databasePath);
        var migrator = new DatabaseMigrator(_databasePath);

        if (!validator.DatabaseExists())
        {
            Console.WriteLine("🆕 База даних не знайдена. Створення нової...");
            CreateFreshDatabase();

            var seeder = new DataSeeder(this);
            seeder.SeedAll();

            Console.WriteLine("✅ Нова база даних створена успішно.");
            return;
        }

        Console.WriteLine("📂 База даних знайдена. Перевірка версії...");

        try
        {
            migrator.RunMigrations();
            Console.WriteLine("✅ База даних готова до роботи.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Помилка міграції: {ex.Message}");
            Console.WriteLine("🔄 Перестворення бази даних...");

            validator.DropDatabase();
            CreateFreshDatabase();

            var seeder = new DataSeeder(this);
            seeder.SeedAll();

            Console.WriteLine("✅ База даних перестворена успішно.");
        }
    }

    /// <summary>
    /// Створити нову базу даних з актуальною схемою
    /// </summary>
    private void CreateFreshDatabase()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        // Створюємо всі таблиці
        foreach (var sql in DatabaseSchema.CreateTables)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }

        // Створюємо всі індекси
        foreach (var sql in DatabaseSchema.CreateIndexes)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }

        // Записуємо початкову версію
        using var versionCmd = connection.CreateCommand();
        versionCmd.CommandText = @"
            INSERT INTO DatabaseVersion (Version, AppliedAt, Description)
            VALUES (@version, @appliedAt, @description);
        ";
        versionCmd.Parameters.AddWithValue("@version", DatabaseSchema.CurrentVersion);
        versionCmd.Parameters.AddWithValue("@appliedAt", DateTime.UtcNow.ToString("O"));
        versionCmd.Parameters.AddWithValue("@description", $"Створення нової БД зі схемою версії {DatabaseSchema.CurrentVersion}");
        versionCmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Отримати connection string
    /// </summary>
    public string ConnectionString => _connectionString;

    /// <summary>
    /// Створити нове з'єднання
    /// </summary>
    public SqliteConnection CreateConnection()
    {
        return new SqliteConnection(_connectionString);
    }

    public void Dispose()
    {
        // Cleanup if needed
    }
}
