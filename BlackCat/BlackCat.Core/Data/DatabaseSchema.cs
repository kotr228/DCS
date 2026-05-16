namespace BlackCat.Core.Data;

/// <summary>
/// Схема бази даних BlackCat Firewall
/// </summary>
public static class DatabaseSchema
{
    /// <summary>
    /// Версія схеми БД
    /// </summary>
    public const int CurrentVersion = 3;

    /// <summary>
    /// SQL команди для створення всіх таблиць
    /// </summary>
    public static readonly string[] CreateTables = new[]
    {
        // ============================================
        // ДОВІДНИКИ (Reference Tables)
        // ============================================

        @"CREATE TABLE IF NOT EXISTS Roles (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            Name TEXT NOT NULL UNIQUE,
            Description TEXT,
            IsActive INTEGER DEFAULT 1,
            SortOrder INTEGER DEFAULT 0
        );",

        @"CREATE TABLE IF NOT EXISTS Cities (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            Name TEXT NOT NULL UNIQUE,
            Region TEXT,
            Latitude REAL,
            Longitude REAL,
            TimezoneOffset INTEGER,
            IsActive INTEGER DEFAULT 1,
            SortOrder INTEGER DEFAULT 0
        );",

        @"CREATE TABLE IF NOT EXISTS EventTypes (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            Name TEXT NOT NULL UNIQUE,
            Description TEXT NOT NULL,
            Category TEXT,
            Severity TEXT,
            IsActive INTEGER DEFAULT 1
        );",

        @"CREATE TABLE IF NOT EXISTS ConnectionStatuses (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            Name TEXT NOT NULL UNIQUE,
            Description TEXT NOT NULL,
            Color TEXT,
            Icon TEXT,
            IsFinal INTEGER DEFAULT 0,
            IsActive INTEGER DEFAULT 1
        );",

        // ============================================
        // ОСНОВНІ ТАБЛИЦІ (Main Tables)
        // ============================================

        @"CREATE TABLE IF NOT EXISTS LocalBlackID (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            FullID TEXT NOT NULL UNIQUE,
            RoleId INTEGER NOT NULL,
            CityId INTEGER NOT NULL,
            Role TEXT NOT NULL,
            City TEXT NOT NULL,
            Name TEXT NOT NULL,
            Code TEXT NOT NULL,
            HardwareFingerprint TEXT NOT NULL,
            Signature TEXT NOT NULL,
            CreatedAt TEXT NOT NULL,
            SignatureCreatedAt TEXT NOT NULL,
            IsActive INTEGER DEFAULT 1,
            FOREIGN KEY (RoleId) REFERENCES Roles(Id),
            FOREIGN KEY (CityId) REFERENCES Cities(Id)
        );",

        @"CREATE TABLE IF NOT EXISTS PeerNodes (
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
        );",

        @"CREATE TABLE IF NOT EXISTS ConnectionEvents (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            RemoteBlackID TEXT,
            RemoteIP TEXT NOT NULL,
            RemotePort INTEGER NOT NULL,
            InitiatorBlackID TEXT,
            TargetBlackID TEXT,
            EventTypeId INTEGER NOT NULL,
            EventType INTEGER NOT NULL,
            Direction INTEGER NOT NULL,
            Message TEXT NOT NULL,
            ErrorDetails TEXT,
            IsAuthenticated INTEGER DEFAULT 0,
            Timestamp TEXT NOT NULL,
            DurationSeconds REAL,
            BytesSent INTEGER DEFAULT 0,
            BytesReceived INTEGER DEFAULT 0,
            FOREIGN KEY (EventTypeId) REFERENCES EventTypes(Id)
        );",

        // ============================================
        // МОДУЛЬ МАПИ СЕРВЕРІВ (Server Map Module)
        // ============================================

        @"CREATE TABLE IF NOT EXISTS Servers (
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
        );",

        @"CREATE TABLE IF NOT EXISTS ServerLocations (
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
        );",

        // ============================================
        // DCS ІНТЕГРАЦІЯ
        // ============================================

        @"CREATE TABLE IF NOT EXISTS DcsTransfers (
            Id                INTEGER PRIMARY KEY AUTOINCREMENT,
            ConnectionEventId INTEGER,
            FilePath          TEXT NOT NULL,
            FileSize          INTEGER NOT NULL DEFAULT 0,
            TargetFolder      TEXT,
            SyncStatusId      INTEGER,
            ChecksumSHA256    TEXT,
            PeerBlackID       TEXT,
            TransferredAt     TEXT NOT NULL,
            FOREIGN KEY (ConnectionEventId) REFERENCES ConnectionEvents(Id) ON DELETE CASCADE,
            FOREIGN KEY (SyncStatusId)      REFERENCES ConnectionStatuses(Id)
        );",

        // ============================================
        // ТАБЛИЦЯ ВЕРСІЙ БД
        // ============================================

        @"CREATE TABLE IF NOT EXISTS DatabaseVersion (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            Version INTEGER NOT NULL,
            AppliedAt TEXT NOT NULL,
            Description TEXT
        );"
    };

    /// <summary>
    /// SQL команди для створення індексів
    /// </summary>
    public static readonly string[] CreateIndexes = new[]
    {
        // LocalBlackID
        "CREATE INDEX IF NOT EXISTS idx_localblackid_role ON LocalBlackID(RoleId);",
        "CREATE INDEX IF NOT EXISTS idx_localblackid_city ON LocalBlackID(CityId);",
        "CREATE INDEX IF NOT EXISTS idx_localblackid_active ON LocalBlackID(IsActive);",

        // PeerNodes
        "CREATE INDEX IF NOT EXISTS idx_peernodes_blackid ON PeerNodes(BlackID);",
        "CREATE INDEX IF NOT EXISTS idx_peernodes_active ON PeerNodes(IsActive);",
        "CREATE INDEX IF NOT EXISTS idx_peernodes_status ON PeerNodes(StatusId);",

        // ConnectionEvents
        "CREATE INDEX IF NOT EXISTS idx_events_timestamp ON ConnectionEvents(Timestamp);",
        "CREATE INDEX IF NOT EXISTS idx_events_remote_blackid ON ConnectionEvents(RemoteBlackID);",
        "CREATE INDEX IF NOT EXISTS idx_events_initiator ON ConnectionEvents(InitiatorBlackID);",
        "CREATE INDEX IF NOT EXISTS idx_events_target ON ConnectionEvents(TargetBlackID);",
        "CREATE INDEX IF NOT EXISTS idx_events_type ON ConnectionEvents(EventTypeId);",

        // Servers
        "CREATE INDEX IF NOT EXISTS idx_servers_blackid ON Servers(BlackID);",
        "CREATE INDEX IF NOT EXISTS idx_servers_status ON Servers(StatusId);",
        "CREATE INDEX IF NOT EXISTS idx_servers_active ON Servers(IsActive);",

        // ServerLocations
        "CREATE INDEX IF NOT EXISTS idx_serverlocations_server ON ServerLocations(ServerId);",
        "CREATE INDEX IF NOT EXISTS idx_serverlocations_city ON ServerLocations(CityId);",
        "CREATE INDEX IF NOT EXISTS idx_serverlocations_coords ON ServerLocations(Latitude, Longitude);",

        // DcsTransfers
        "CREATE INDEX IF NOT EXISTS idx_dcstransfers_peer ON DcsTransfers(PeerBlackID);",
        "CREATE INDEX IF NOT EXISTS idx_dcstransfers_time ON DcsTransfers(TransferredAt);"
    };
}
