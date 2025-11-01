namespace CatSuite.Installer.Services
{
    public static class DatabaseSchema
    {
        public static readonly string[] CreateTables = new[]
        {
            // ========== Встановлені пакети ==========
            @"CREATE TABLE IF NOT EXISTS InstalledPackages (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                PackageId TEXT NOT NULL UNIQUE,
                Name TEXT NOT NULL,
                Version TEXT NOT NULL,
                InstallPath TEXT,
                InstalledAt DATETIME DEFAULT CURRENT_TIMESTAMP,
                UpdatedAt DATETIME,
                MsiProductCode TEXT
            );",

            // ========== Логи встановлень ==========
            @"CREATE TABLE IF NOT EXISTS InstallationLogs (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                PackageId TEXT NOT NULL,
                Action TEXT NOT NULL,
                Version TEXT NOT NULL,
                Success INTEGER NOT NULL DEFAULT 0,
                ErrorMessage TEXT,
                Timestamp DATETIME DEFAULT CURRENT_TIMESTAMP,
                LogFilePath TEXT
            );",

            // ========== Залежності між пакетами ==========
            @"CREATE TABLE IF NOT EXISTS DependencyRelations (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                PackageId TEXT NOT NULL,
                DependsOnPackageId TEXT NOT NULL,
                IsRequired INTEGER NOT NULL DEFAULT 1,
                FOREIGN KEY(PackageId) REFERENCES InstalledPackages(PackageId),
                FOREIGN KEY(DependsOnPackageId) REFERENCES InstalledPackages(PackageId)
            );",

            // ========== Кеш завантажених файлів ==========
            @"CREATE TABLE IF NOT EXISTS DownloadCache (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Url TEXT NOT NULL UNIQUE,
                LocalPath TEXT NOT NULL,
                Sha256Hash TEXT,
                DownloadedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
                FileSize INTEGER,
                IsValid INTEGER DEFAULT 1
            );",

            // ========== Налаштування інсталятора ==========
            @"CREATE TABLE IF NOT EXISTS InstallerSettings (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                SettingKey TEXT NOT NULL UNIQUE,
                SettingValue TEXT NOT NULL,
                Description TEXT,
                UpdatedAt DATETIME DEFAULT CURRENT_TIMESTAMP
            );",

            // ========== Історія оновлень ядра ==========
            @"CREATE TABLE IF NOT EXISTS CoreUpdateHistory (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                FromVersion TEXT NOT NULL,
                ToVersion TEXT NOT NULL,
                UpdatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
                Success INTEGER NOT NULL DEFAULT 1,
                ErrorMessage TEXT
            );",

            // ========== Індекси для швидкого пошуку ==========
            @"CREATE INDEX IF NOT EXISTS idx_installed_packageid 
              ON InstalledPackages(PackageId);",

            @"CREATE INDEX IF NOT EXISTS idx_logs_packageid 
              ON InstallationLogs(PackageId);",

            @"CREATE INDEX IF NOT EXISTS idx_logs_timestamp 
              ON InstallationLogs(Timestamp);",

            @"CREATE INDEX IF NOT EXISTS idx_dependencies_package 
              ON DependencyRelations(PackageId);",

            @"CREATE INDEX IF NOT EXISTS idx_cache_url 
              ON DownloadCache(Url);"
        };

        // ========== Початкові налаштування ==========
        public static readonly string[] InsertDefaultSettings = new[]
        {
            @"INSERT OR IGNORE INTO InstallerSettings (SettingKey, SettingValue, Description) 
              VALUES ('ManifestUrl', 'https://drive.google.com/uc?export=download&id=YOUR_ID', 'URL маніфесту пакетів');",

            @"INSERT OR IGNORE INTO InstallerSettings (SettingKey, SettingValue, Description) 
              VALUES ('AutoCheckUpdates', 'true', 'Автоматична перевірка оновлень при запуску');",

            @"INSERT OR IGNORE INTO InstallerSettings (SettingKey, SettingValue, Description) 
              VALUES ('KeepDownloadCache', 'true', 'Зберігати завантажені MSI для повторного використання');",

            @"INSERT OR IGNORE INTO InstallerSettings (SettingKey, SettingValue, Description) 
              VALUES ('LogRetentionDays', '90', 'Скільки днів зберігати логи встановлень');",

            @"INSERT OR IGNORE INTO InstallerSettings (SettingKey, SettingValue, Description) 
              VALUES ('InstallMode', 'Silent', 'Режим встановлення: Silent, Interactive');"
        };
    }
}