namespace AsmodayCat.Core.Data;

public static class DatabaseSchema
{
    public static readonly string[] CreateTables =
    [
        @"CREATE TABLE IF NOT EXISTS HardwareSettings (
            Id                  INTEGER PRIMARY KEY,
            PreferredDevice     TEXT    NOT NULL DEFAULT 'Auto',
            ContextWindowSize   INTEGER NOT NULL DEFAULT 4096,
            MaxVramAllocationMb INTEGER NOT NULL DEFAULT 0,
            CpuThreads          INTEGER NOT NULL DEFAULT 4,
            AllowCpuFallback    INTEGER NOT NULL DEFAULT 1,
            UpdatedAt           TEXT
        );",

        @"CREATE TABLE IF NOT EXISTS AppSettings (
            Key       TEXT PRIMARY KEY,
            Value     TEXT,
            UpdatedAt TEXT
        );",

        @"CREATE TABLE IF NOT EXISTS AgentWorkspaces (
            Id                  TEXT    PRIMARY KEY,
            InputPath           TEXT    NOT NULL,
            OutputPath          TEXT    NOT NULL DEFAULT '',
            ActionType          TEXT    NOT NULL DEFAULT 'CreateReport',
            SystemPrompt        TEXT    NOT NULL DEFAULT '',
            AllowedExtensions   TEXT    NOT NULL DEFAULT '*.*',
            AllowInternetAccess INTEGER NOT NULL DEFAULT 0,
            IsActive            INTEGER NOT NULL DEFAULT 1,
            CreatedAt           TEXT    NOT NULL
        );",

        @"CREATE TABLE IF NOT EXISTS ChatSessions (
            Id        TEXT PRIMARY KEY,
            Title     TEXT NOT NULL,
            CreatedAt TEXT NOT NULL,
            UpdatedAt TEXT NOT NULL
        );",

        @"CREATE TABLE IF NOT EXISTS ChatMessages (
            Id              TEXT PRIMARY KEY,
            SessionId       TEXT NOT NULL REFERENCES ChatSessions(Id) ON DELETE CASCADE,
            Role            TEXT NOT NULL,
            Content         TEXT NOT NULL,
            Timestamp       TEXT NOT NULL,
            AttachmentsJson TEXT
        );",

        @"CREATE TABLE IF NOT EXISTS KnownModels (
            Id              TEXT PRIMARY KEY,
            Name            TEXT NOT NULL UNIQUE,
            RecommendedTask TEXT NOT NULL DEFAULT '',
            IsCustom        INTEGER NOT NULL DEFAULT 0
        );"
    ];

    public static readonly string[] CreateIndexes =
    [
        "CREATE INDEX IF NOT EXISTS idx_workspaces_active ON AgentWorkspaces(IsActive);",
        "CREATE INDEX IF NOT EXISTS idx_messages_session  ON ChatMessages(SessionId);",
        "CREATE INDEX IF NOT EXISTS idx_messages_ts       ON ChatMessages(Timestamp);",
        "CREATE INDEX IF NOT EXISTS idx_sessions_updated  ON ChatSessions(UpdatedAt DESC);",
        "CREATE INDEX IF NOT EXISTS idx_models_custom     ON KnownModels(IsCustom);"
    ];
}
