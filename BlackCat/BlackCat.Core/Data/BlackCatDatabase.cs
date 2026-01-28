using Microsoft.Data.Sqlite;
using BlackCat.Shared.Models;

namespace BlackCat.Core.Data;

/// <summary>
/// Головний клас для роботи з базою даних BlackCat
/// </summary>
public class BlackCatDatabase : IDisposable
{
    private readonly string _connectionString;

    public BlackCatDatabase(string databasePath = "blackcat.db")
    {
        _connectionString = $"Data Source={databasePath}";
        InitializeDatabase();
    }

    /// <summary>
    /// Ініціалізація всіх таблиць
    /// </summary>
    private void InitializeDatabase()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        string createTablesSql = @"
            -- Таблиця для зберігання локального Black-ID
            CREATE TABLE IF NOT EXISTS LocalBlackID (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                FullID TEXT NOT NULL UNIQUE,
                Role TEXT NOT NULL,
                City TEXT NOT NULL,
                Name TEXT NOT NULL,
                Code TEXT NOT NULL,
                HardwareFingerprint TEXT NOT NULL,
                Signature TEXT NOT NULL,
                CreatedAt TEXT NOT NULL,
                IsActive INTEGER DEFAULT 1
            );

            -- Таблиця віддалених вузлів (телефонна книга)
            CREATE TABLE IF NOT EXISTS PeerNodes (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                BlackID TEXT NOT NULL UNIQUE,
                Address TEXT NOT NULL,
                Port INTEGER NOT NULL DEFAULT 9999,
                DisplayName TEXT NOT NULL,
                Description TEXT,
                IsTrusted INTEGER DEFAULT 0,
                LastConnectedAt TEXT,
                CreatedAt TEXT NOT NULL,
                IsActive INTEGER DEFAULT 1,
                SuccessfulConnections INTEGER DEFAULT 0,
                FailedConnections INTEGER DEFAULT 0,
                PublicKey TEXT,
                Tags TEXT
            );

            -- Таблиця логів з'єднань
            CREATE TABLE IF NOT EXISTS ConnectionEvents (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                RemoteBlackID TEXT,
                RemoteIP TEXT NOT NULL,
                RemotePort INTEGER NOT NULL,
                EventType INTEGER NOT NULL,
                Direction INTEGER NOT NULL,
                Message TEXT NOT NULL,
                ErrorDetails TEXT,
                IsAuthenticated INTEGER DEFAULT 0,
                Timestamp TEXT NOT NULL,
                DurationSeconds REAL,
                BytesSent INTEGER DEFAULT 0,
                BytesReceived INTEGER DEFAULT 0
            );

            -- Індекси для прискорення запитів
            CREATE INDEX IF NOT EXISTS idx_peernodes_blackid ON PeerNodes(BlackID);
            CREATE INDEX IF NOT EXISTS idx_peernodes_active ON PeerNodes(IsActive);
            CREATE INDEX IF NOT EXISTS idx_events_timestamp ON ConnectionEvents(Timestamp);
            CREATE INDEX IF NOT EXISTS idx_events_blackid ON ConnectionEvents(RemoteBlackID);
            CREATE INDEX IF NOT EXISTS idx_events_type ON ConnectionEvents(EventType);
        ";

        using var command = new SqliteCommand(createTablesSql, connection);
        command.ExecuteNonQuery();
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
