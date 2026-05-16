using Microsoft.Data.Sqlite;

namespace BlackCat.Core.Data;

/// <summary>
/// Заповнення довідників початковими даними
/// </summary>
public class DataSeeder
{
    private readonly string _connectionString;

    public DataSeeder(string connectionString)
    {
        _connectionString = connectionString;
    }

    /// <summary>
    /// Запустити ініціалізацію всіх довідників
    /// </summary>
    public void SeedAll()
    {
        SeedRoles();
        SeedCities();
        SeedEventTypes();
        SeedConnectionStatuses();
    }

    /// <summary>
    /// Заповнити довідник ролей
    /// </summary>
    public void SeedRoles()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        var roles = new[]
        {
            new { Name = "SKLAD", Description = "Складське приміщення", SortOrder = 1 },
            new { Name = "OFFICE", Description = "Офісне приміщення", SortOrder = 2 },
            new { Name = "MAIN", Description = "Головний вузол", SortOrder = 3 },
            new { Name = "BACKUP", Description = "Резервний вузол", SortOrder = 4 },
            new { Name = "SERVER", Description = "Серверне обладнання", SortOrder = 5 },
            new { Name = "WORKSTATION", Description = "Робоча станція", SortOrder = 6 }
        };

        foreach (var role in roles)
        {
            var sql = @"
                INSERT OR IGNORE INTO Roles (Name, Description, IsActive, SortOrder)
                VALUES (@Name, @Description, 1, @SortOrder)";

            using var command = new SqliteCommand(sql, connection);
            command.Parameters.AddWithValue("@Name", role.Name);
            command.Parameters.AddWithValue("@Description", role.Description);
            command.Parameters.AddWithValue("@SortOrder", role.SortOrder);
            command.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// Заповнити довідник міст
    /// </summary>
    public void SeedCities()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        var cities = new[]
        {
            new { Name = "KYIV", Region = "Київська", Lat = 50.4501, Lon = 30.5234, Tz = 2, Sort = 1 },
            new { Name = "ODESA", Region = "Одеська", Lat = 46.4825, Lon = 30.7233, Tz = 2, Sort = 2 },
            new { Name = "LVIV", Region = "Львівська", Lat = 49.8397, Lon = 24.0297, Tz = 2, Sort = 3 },
            new { Name = "KHARKIV", Region = "Харківська", Lat = 49.9935, Lon = 36.2304, Tz = 2, Sort = 4 },
            new { Name = "DNIPRO", Region = "Дніпропетровська", Lat = 48.4647, Lon = 35.0462, Tz = 2, Sort = 5 },
            new { Name = "ZAPORIZHZHIA", Region = "Запорізька", Lat = 47.8388, Lon = 35.1396, Tz = 2, Sort = 6 },
            new { Name = "KRYVYIRIH", Region = "Дніпропетровська", Lat = 47.9102, Lon = 33.3919, Tz = 2, Sort = 7 },
            new { Name = "MYKOLAIV", Region = "Миколаївська", Lat = 46.9750, Lon = 32.0050, Tz = 2, Sort = 8 },
            new { Name = "MARIUPOL", Region = "Донецька", Lat = 47.0956, Lon = 37.5489, Tz = 2, Sort = 9 },
            new { Name = "VINNYTSIA", Region = "Вінницька", Lat = 49.2328, Lon = 28.4680, Tz = 2, Sort = 10 },
            new { Name = "KHERSON", Region = "Херсонська", Lat = 46.6354, Lon = 32.6169, Tz = 2, Sort = 11 },
            new { Name = "POLTAVA", Region = "Полтавська", Lat = 49.5883, Lon = 34.5514, Tz = 2, Sort = 12 }
        };

        foreach (var city in cities)
        {
            var sql = @"
                INSERT OR IGNORE INTO Cities (Name, Region, Latitude, Longitude, TimezoneOffset, IsActive, SortOrder)
                VALUES (@Name, @Region, @Lat, @Lon, @Tz, 1, @Sort)";

            using var command = new SqliteCommand(sql, connection);
            command.Parameters.AddWithValue("@Name", city.Name);
            command.Parameters.AddWithValue("@Region", city.Region);
            command.Parameters.AddWithValue("@Lat", city.Lat);
            command.Parameters.AddWithValue("@Lon", city.Lon);
            command.Parameters.AddWithValue("@Tz", city.Tz);
            command.Parameters.AddWithValue("@Sort", city.Sort);
            command.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// Заповнити довідник типів подій
    /// </summary>
    public void SeedEventTypes()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        var eventTypes = new[]
        {
            new { Name = "ConnectionAttempt", Desc = "Спроба підключення", Cat = "Connection", Sev = "Info" },
            new { Name = "Connected", Desc = "Успішне з'єднання", Cat = "Connection", Sev = "Info" },
            new { Name = "Disconnected", Desc = "Розрив з'єднання", Cat = "Connection", Sev = "Info" },
            new { Name = "HelloReceived", Desc = "Отримано HELLO повідомлення", Cat = "Handshake", Sev = "Info" },
            new { Name = "HelloSent", Desc = "Відправлено HELLO повідомлення", Cat = "Handshake", Sev = "Info" },
            new { Name = "ChallengeReceived", Desc = "Отримано CHALLENGE", Cat = "Handshake", Sev = "Info" },
            new { Name = "ChallengeSent", Desc = "Відправлено CHALLENGE", Cat = "Handshake", Sev = "Info" },
            new { Name = "ResponseReceived", Desc = "Отримано RESPONSE", Cat = "Handshake", Sev = "Info" },
            new { Name = "ResponseSent", Desc = "Відправлено RESPONSE", Cat = "Handshake", Sev = "Info" },
            new { Name = "HandshakeReceived", Desc = "Отримано HANDSHAKE підтвердження", Cat = "Handshake", Sev = "Info" },
            new { Name = "HandshakeSent", Desc = "Відправлено HANDSHAKE підтвердження", Cat = "Handshake", Sev = "Info" },
            new { Name = "AccessDenied", Desc = "Відмова в доступі", Cat = "Security", Sev = "Warning" },
            new { Name = "AuthenticationFailed", Desc = "Невдала автентифікація", Cat = "Security", Sev = "Warning" },
            new { Name = "AuthenticationSuccess", Desc = "Успішна автентифікація", Cat = "Security", Sev = "Info" },
            new { Name = "InvalidFingerprint", Desc = "Невірний Hardware Fingerprint", Cat = "Security", Sev = "Error" },
            new { Name = "InvalidSignature", Desc = "Невірний підпис", Cat = "Security", Sev = "Error" },
            new { Name = "ConnectionError", Desc = "Помилка з'єднання", Cat = "Error", Sev = "Error" },
            new { Name = "SuspiciousActivity", Desc = "Підозріла активність", Cat = "Security", Sev = "Warning" },
            new { Name = "DataTransferred",   Desc = "Передача даних",            Cat = "Data",     Sev = "Info"    },
            new { Name = "PacketDropped",      Desc = "Пакет відкинуто",           Cat = "Data",     Sev = "Warning" },

            // DCS інтеграція
            new { Name = "DcsFileSyncStart",   Desc = "Початок передачі файлу DCS",       Cat = "DCS", Sev = "Info"    },
            new { Name = "DcsFileSyncSuccess", Desc = "Успішна передача файлу DCS",        Cat = "DCS", Sev = "Info"    },
            new { Name = "DcsFileSyncError",   Desc = "Помилка передачі файлу DCS",        Cat = "DCS", Sev = "Error"   },
            new { Name = "DcsFileLocked",      Desc = "Файл DCS заблоковано іншим вузлом", Cat = "DCS", Sev = "Warning" },
            new { Name = "DcsFolderShared",    Desc = "Папка DCS відкрита для обміну",     Cat = "DCS", Sev = "Info"    }
        };

        foreach (var evt in eventTypes)
        {
            var sql = @"
                INSERT OR IGNORE INTO EventTypes (Name, Description, Category, Severity, IsActive)
                VALUES (@Name, @Desc, @Cat, @Sev, 1)";

            using var command = new SqliteCommand(sql, connection);
            command.Parameters.AddWithValue("@Name", evt.Name);
            command.Parameters.AddWithValue("@Desc", evt.Desc);
            command.Parameters.AddWithValue("@Cat", evt.Cat);
            command.Parameters.AddWithValue("@Sev", evt.Sev);
            command.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// Заповнити довідник статусів з'єднання
    /// </summary>
    public void SeedConnectionStatuses()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        var statuses = new[]
        {
            new { Name = "Connected", Desc = "Підключено", Color = "#4EC9B0", Icon = "✅", Final = 1 },
            new { Name = "Disconnected", Desc = "Відключено", Color = "#808080", Icon = "⭕", Final = 1 },
            new { Name = "Connecting", Desc = "Підключення...", Color = "#569CD6", Icon = "🔄", Final = 0 },
            new { Name = "Authenticating", Desc = "Автентифікація...", Color = "#DCDCAA", Icon = "🔐", Final = 0 },
            new { Name = "Failed", Desc = "Помилка", Color = "#F44747", Icon = "❌", Final = 1 },
            new { Name = "Rejected", Desc = "Відхилено", Color = "#CE9178", Icon = "🚫", Final = 1 },
            new { Name = "Timeout", Desc = "Тайм-аут", Color = "#D16969", Icon = "⏱️", Final = 1 },
            new { Name = "Unknown", Desc = "Невідомо", Color = "#6A6A6A", Icon = "❓", Final = 0 }
        };

        foreach (var status in statuses)
        {
            var sql = @"
                INSERT OR IGNORE INTO ConnectionStatuses (Name, Description, Color, Icon, IsFinal, IsActive)
                VALUES (@Name, @Desc, @Color, @Icon, @Final, 1)";

            using var command = new SqliteCommand(sql, connection);
            command.Parameters.AddWithValue("@Name", status.Name);
            command.Parameters.AddWithValue("@Desc", status.Desc);
            command.Parameters.AddWithValue("@Color", status.Color);
            command.Parameters.AddWithValue("@Icon", status.Icon);
            command.Parameters.AddWithValue("@Final", status.Final);
            command.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// Перевірити чи довідники заповнені
    /// </summary>
    public bool AreReferencesSeeded()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        var tables = new[] { "Roles", "Cities", "EventTypes", "ConnectionStatuses" };

        foreach (var table in tables)
        {
            var sql = $"SELECT COUNT(*) FROM {table}";
            using var command = new SqliteCommand(sql, connection);
            var count = Convert.ToInt32(command.ExecuteScalar());

            if (count == 0)
                return false;
        }

        return true;
    }
}
