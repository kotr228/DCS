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
        SeedReferenceData();
    }

    /// <summary>
    /// Заповнити довідники початковими даними
    /// </summary>
    private void SeedReferenceData()
    {
        var seeder = new DataSeeder(this);

        // Перевірити чи вже заповнені
        if (!seeder.AreReferencesSeeded())
        {
            seeder.SeedAll();
        }
    }

    /// <summary>
    /// Ініціалізація всіх таблиць
    /// </summary>
    private void InitializeDatabase()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        string createTablesSql = @"
            -- ============================================
            -- ДОВІДНИКИ (Reference Tables)
            -- ============================================

            -- Довідник ролей
            CREATE TABLE IF NOT EXISTS Roles (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL UNIQUE,
                Description TEXT,
                IsActive INTEGER DEFAULT 1,
                SortOrder INTEGER DEFAULT 0
            );

            -- Довідник міст
            CREATE TABLE IF NOT EXISTS Cities (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL UNIQUE,
                Region TEXT,
                Latitude REAL,
                Longitude REAL,
                TimezoneOffset INTEGER,
                IsActive INTEGER DEFAULT 1,
                SortOrder INTEGER DEFAULT 0
            );

            -- Довідник типів подій
            CREATE TABLE IF NOT EXISTS EventTypes (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL UNIQUE,
                Description TEXT NOT NULL,
                Category TEXT,
                Severity TEXT,
                IsActive INTEGER DEFAULT 1
            );

            -- Довідник статусів з'єднання
            CREATE TABLE IF NOT EXISTS ConnectionStatuses (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL UNIQUE,
                Description TEXT NOT NULL,
                Color TEXT,
                Icon TEXT,
                IsFinal INTEGER DEFAULT 0,
                IsActive INTEGER DEFAULT 1
            );

            -- ============================================
            -- ОСНОВНІ ТАБЛИЦІ (Main Tables)
            -- ============================================

            -- Таблиця для зберігання локального Black-ID (НОРМАЛІЗОВАНА)
            CREATE TABLE IF NOT EXISTS LocalBlackID (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                FullID TEXT NOT NULL UNIQUE,
                RoleId INTEGER NOT NULL,
                CityId INTEGER NOT NULL,
                Role TEXT NOT NULL,  -- Денормалізовано для backward compatibility
                City TEXT NOT NULL,  -- Денормалізовано для backward compatibility
                Name TEXT NOT NULL,
                Code TEXT NOT NULL,
                HardwareFingerprint TEXT NOT NULL,
                Signature TEXT NOT NULL,
                CreatedAt TEXT NOT NULL,
                SignatureCreatedAt TEXT NOT NULL,
                IsActive INTEGER DEFAULT 1,
                FOREIGN KEY (RoleId) REFERENCES Roles(Id),
                FOREIGN KEY (CityId) REFERENCES Cities(Id)
            );

            -- Таблиця віддалених вузлів (телефонна книга) - НОРМАЛІЗОВАНА
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
                StatusId INTEGER,
                SuccessfulConnections INTEGER DEFAULT 0,
                FailedConnections INTEGER DEFAULT 0,
                PublicKey TEXT,
                Tags TEXT,
                FOREIGN KEY (StatusId) REFERENCES ConnectionStatuses(Id)
            );

            -- Таблиця логів з'єднань - НОРМАЛІЗОВАНА
            CREATE TABLE IF NOT EXISTS ConnectionEvents (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                RemoteBlackID TEXT,
                RemoteIP TEXT NOT NULL,
                RemotePort INTEGER NOT NULL,
                InitiatorBlackID TEXT,
                TargetBlackID TEXT,
                EventTypeId INTEGER NOT NULL,
                EventType INTEGER NOT NULL,  -- Денормалізовано для backward compatibility (enum)
                Direction INTEGER NOT NULL,
                Message TEXT NOT NULL,
                ErrorDetails TEXT,
                IsAuthenticated INTEGER DEFAULT 0,
                Timestamp TEXT NOT NULL,
                DurationSeconds REAL,
                BytesSent INTEGER DEFAULT 0,
                BytesReceived INTEGER DEFAULT 0,
                FOREIGN KEY (EventTypeId) REFERENCES EventTypes(Id)
            );

            -- ============================================
            -- МОДУЛЬ МАПИ СЕРВЕРІВ (Server Map Module)
            -- ============================================

            -- Таблиця серверів
            CREATE TABLE IF NOT EXISTS Servers (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                BlackID TEXT NOT NULL UNIQUE,
                HardwareFingerprint TEXT NOT NULL,
                StatusId INTEGER NOT NULL,
                DisplayName TEXT NOT NULL,
                Description TEXT,
                OperatingSystem TEXT,
                FirewallVersion TEXT,
                LastSeenAt TEXT,
                CreatedAt TEXT NOT NULL,
                IsActive INTEGER DEFAULT 1,
                Metadata TEXT,
                FOREIGN KEY (StatusId) REFERENCES ConnectionStatuses(Id)
            );

            -- Таблиця локацій серверів
            CREATE TABLE IF NOT EXISTS ServerLocations (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ServerId INTEGER NOT NULL UNIQUE,
                Latitude REAL NOT NULL,
                Longitude REAL NOT NULL,
                IPAddress TEXT NOT NULL,
                Port INTEGER DEFAULT 9999,
                Address TEXT,
                CityId INTEGER,
                CountryCode TEXT,
                Region TEXT,
                PostalCode TEXT,
                AccuracyMeters REAL,
                UpdatedAt TEXT NOT NULL,
                CreatedAt TEXT NOT NULL,
                FOREIGN KEY (ServerId) REFERENCES Servers(Id) ON DELETE CASCADE,
                FOREIGN KEY (CityId) REFERENCES Cities(Id)
            );

            -- ============================================
            -- ІНДЕКСИ (Indexes)
            -- ============================================

            -- LocalBlackID
            CREATE INDEX IF NOT EXISTS idx_localblackid_role ON LocalBlackID(RoleId);
            CREATE INDEX IF NOT EXISTS idx_localblackid_city ON LocalBlackID(CityId);
            CREATE INDEX IF NOT EXISTS idx_localblackid_active ON LocalBlackID(IsActive);

            -- PeerNodes
            CREATE INDEX IF NOT EXISTS idx_peernodes_blackid ON PeerNodes(BlackID);
            CREATE INDEX IF NOT EXISTS idx_peernodes_active ON PeerNodes(IsActive);
            CREATE INDEX IF NOT EXISTS idx_peernodes_status ON PeerNodes(StatusId);

            -- ConnectionEvents
            CREATE INDEX IF NOT EXISTS idx_events_timestamp ON ConnectionEvents(Timestamp);
            CREATE INDEX IF NOT EXISTS idx_events_remote_blackid ON ConnectionEvents(RemoteBlackID);
            CREATE INDEX IF NOT EXISTS idx_events_initiator ON ConnectionEvents(InitiatorBlackID);
            CREATE INDEX IF NOT EXISTS idx_events_target ON ConnectionEvents(TargetBlackID);
            CREATE INDEX IF NOT EXISTS idx_events_type ON ConnectionEvents(EventTypeId);

            -- Servers
            CREATE INDEX IF NOT EXISTS idx_servers_blackid ON Servers(BlackID);
            CREATE INDEX IF NOT EXISTS idx_servers_status ON Servers(StatusId);
            CREATE INDEX IF NOT EXISTS idx_servers_active ON Servers(IsActive);

            -- ServerLocations
            CREATE INDEX IF NOT EXISTS idx_serverlocations_server ON ServerLocations(ServerId);
            CREATE INDEX IF NOT EXISTS idx_serverlocations_city ON ServerLocations(CityId);
            CREATE INDEX IF NOT EXISTS idx_serverlocations_coords ON ServerLocations(Latitude, Longitude);
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
