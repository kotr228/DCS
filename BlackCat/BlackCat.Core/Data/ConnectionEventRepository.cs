using Microsoft.Data.Sqlite;
using BlackCat.Shared.Models;

namespace BlackCat.Core.Data;

/// <summary>
/// Репозиторій для логування подій з'єднань
/// </summary>
public class ConnectionEventRepository
{
    private readonly string _connectionString;

    public ConnectionEventRepository(BlackCatDatabase database)
    {
        _connectionString = database.ConnectionString;
    }

    /// <summary>
    /// Додати нову подію
    /// </summary>
    public void LogEvent(ConnectionEvent ev)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        // Мапимо EventType enum на EventTypeId через назву події
        int eventTypeId = (int)ev.EventType; // fallback
        string eventTypeName = ev.EventType.ToString();

        string getEventTypeIdSql = "SELECT Id FROM EventTypes WHERE Name = @EventTypeName LIMIT 1";
        using (var getEventTypeCmd = new SqliteCommand(getEventTypeIdSql, connection))
        {
            getEventTypeCmd.Parameters.AddWithValue("@EventTypeName", eventTypeName);
            var result = getEventTypeCmd.ExecuteScalar();
            if (result != null)
            {
                eventTypeId = Convert.ToInt32(result);
            }
        }

        string sql = @"
            INSERT INTO ConnectionEvents (RemoteBlackID, RemoteIP, RemotePort, InitiatorBlackID,
                                         TargetBlackID, EventTypeId, EventType, Direction,
                                         Message, ErrorDetails, IsAuthenticated, Timestamp,
                                         DurationSeconds, BytesSent, BytesReceived)
            VALUES (@RemoteBlackID, @RemoteIP, @RemotePort, @InitiatorBlackID,
                    @TargetBlackID, @EventTypeId, @EventType, @Direction,
                    @Message, @ErrorDetails, @IsAuthenticated, @Timestamp,
                    @DurationSeconds, @BytesSent, @BytesReceived)
        ";

        using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("@RemoteBlackID", ev.RemoteBlackID ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@RemoteIP", ev.RemoteIP);
        command.Parameters.AddWithValue("@RemotePort", ev.RemotePort);
        command.Parameters.AddWithValue("@InitiatorBlackID", ev.InitiatorBlackID ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@TargetBlackID", ev.TargetBlackID ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@EventTypeId", eventTypeId);
        command.Parameters.AddWithValue("@EventType", (int)ev.EventType);
        command.Parameters.AddWithValue("@Direction", (int)ev.Direction);
        command.Parameters.AddWithValue("@Message", ev.Message);
        command.Parameters.AddWithValue("@ErrorDetails", ev.ErrorDetails ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@IsAuthenticated", ev.IsAuthenticated ? 1 : 0);
        command.Parameters.AddWithValue("@Timestamp", ev.Timestamp.ToString("O"));
        command.Parameters.AddWithValue("@DurationSeconds",
            ev.Duration?.TotalSeconds ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@BytesSent", ev.BytesSent);
        command.Parameters.AddWithValue("@BytesReceived", ev.BytesReceived);

        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Отримати останні події
    /// </summary>
    public List<ConnectionEvent> GetRecentEvents(int count = 100)
    {
        var list = new List<ConnectionEvent>();

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        string sql = "SELECT * FROM ConnectionEvents ORDER BY Timestamp DESC LIMIT @Count";
        using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("@Count", count);

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            list.Add(MapToConnectionEvent(reader));
        }

        return list;
    }

    /// <summary>
    /// Отримати події за період
    /// </summary>
    public List<ConnectionEvent> GetEventsByDateRange(DateTime from, DateTime to)
    {
        var list = new List<ConnectionEvent>();

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        string sql = @"
            SELECT * FROM ConnectionEvents
            WHERE Timestamp >= @From AND Timestamp <= @To
            ORDER BY Timestamp DESC
        ";

        using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("@From", from.ToString("O"));
        command.Parameters.AddWithValue("@To", to.ToString("O"));

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            list.Add(MapToConnectionEvent(reader));
        }

        return list;
    }

    /// <summary>
    /// Отримати події для конкретного Black-ID
    /// </summary>
    public List<ConnectionEvent> GetEventsByBlackID(string blackID, int limit = 100)
    {
        var list = new List<ConnectionEvent>();

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        string sql = @"
            SELECT * FROM ConnectionEvents
            WHERE RemoteBlackID = @BlackID
            ORDER BY Timestamp DESC
            LIMIT @Limit
        ";

        using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("@BlackID", blackID);
        command.Parameters.AddWithValue("@Limit", limit);

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            list.Add(MapToConnectionEvent(reader));
        }

        return list;
    }

    /// <summary>
    /// Отримати події за типом
    /// </summary>
    public List<ConnectionEvent> GetEventsByType(ConnectionEventType eventType, int limit = 100)
    {
        var list = new List<ConnectionEvent>();

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        string sql = @"
            SELECT * FROM ConnectionEvents
            WHERE EventType = @EventType
            ORDER BY Timestamp DESC
            LIMIT @Limit
        ";

        using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("@EventType", (int)eventType);
        command.Parameters.AddWithValue("@Limit", limit);

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            list.Add(MapToConnectionEvent(reader));
        }

        return list;
    }

    /// <summary>
    /// Отримати підозрілі події (authentication failed, access denied)
    /// </summary>
    public List<ConnectionEvent> GetSuspiciousEvents(int limit = 50)
    {
        var list = new List<ConnectionEvent>();

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        string sql = @"
            SELECT * FROM ConnectionEvents
            WHERE EventType IN (@AuthFailed, @AccessDenied, @Suspicious)
            ORDER BY Timestamp DESC
            LIMIT @Limit
        ";

        using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("@AuthFailed", (int)ConnectionEventType.AuthenticationFailed);
        command.Parameters.AddWithValue("@AccessDenied", (int)ConnectionEventType.AccessDenied);
        command.Parameters.AddWithValue("@Suspicious", (int)ConnectionEventType.SuspiciousActivity);
        command.Parameters.AddWithValue("@Limit", limit);

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            list.Add(MapToConnectionEvent(reader));
        }

        return list;
    }

    /// <summary>
    /// Видалити старі події (cleanup)
    /// </summary>
    public int DeleteOldEvents(DateTime beforeDate)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        string sql = "DELETE FROM ConnectionEvents WHERE Timestamp < @BeforeDate";
        using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("@BeforeDate", beforeDate.ToString("O"));

        return command.ExecuteNonQuery();
    }

    /// <summary>
    /// Отримати статистику за період
    /// </summary>
    public ConnectionStatistics GetStatistics(DateTime from, DateTime to)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        string sql = @"
            SELECT
                COUNT(*) as TotalEvents,
                SUM(CASE WHEN EventType = @Connected THEN 1 ELSE 0 END) as SuccessfulConnections,
                SUM(CASE WHEN EventType = @AuthFailed THEN 1 ELSE 0 END) as FailedAuthentications,
                SUM(CASE WHEN IsAuthenticated = 1 THEN 1 ELSE 0 END) as AuthenticatedEvents,
                SUM(BytesSent) as TotalBytesSent,
                SUM(BytesReceived) as TotalBytesReceived
            FROM ConnectionEvents
            WHERE Timestamp >= @From AND Timestamp <= @To
        ";

        using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("@From", from.ToString("O"));
        command.Parameters.AddWithValue("@To", to.ToString("O"));
        command.Parameters.AddWithValue("@Connected", (int)ConnectionEventType.Connected);
        command.Parameters.AddWithValue("@AuthFailed", (int)ConnectionEventType.AuthenticationFailed);

        using var reader = command.ExecuteReader();
        if (reader.Read())
        {
            return new ConnectionStatistics
            {
                TotalEvents = reader.GetInt32(0),
                SuccessfulConnections = reader.GetInt32(1),
                FailedAuthentications = reader.GetInt32(2),
                AuthenticatedEvents = reader.GetInt32(3),
                TotalBytesSent = reader.IsDBNull(4) ? 0 : reader.GetInt64(4),
                TotalBytesReceived = reader.IsDBNull(5) ? 0 : reader.GetInt64(5)
            };
        }

        return new ConnectionStatistics();
    }

    /// <summary>
    /// Мапити з reader в модель
    /// </summary>
    private ConnectionEvent MapToConnectionEvent(SqliteDataReader reader)
    {
        TimeSpan? duration = null;
        if (!reader.IsDBNull(reader.GetOrdinal("DurationSeconds")))
        {
            duration = TimeSpan.FromSeconds(reader.GetDouble(reader.GetOrdinal("DurationSeconds")));
        }

        int eventTypeId = 1;
        var eventTypeIdOrdinal = reader.GetOrdinal("EventTypeId");
        if (!reader.IsDBNull(eventTypeIdOrdinal))
        {
            eventTypeId = reader.GetInt32(eventTypeIdOrdinal);
        }

        string? initiatorBlackID = null;
        var initiatorOrdinal = reader.GetOrdinal("InitiatorBlackID");
        if (!reader.IsDBNull(initiatorOrdinal))
        {
            initiatorBlackID = reader.GetString(initiatorOrdinal);
        }

        string? targetBlackID = null;
        var targetOrdinal = reader.GetOrdinal("TargetBlackID");
        if (!reader.IsDBNull(targetOrdinal))
        {
            targetBlackID = reader.GetString(targetOrdinal);
        }

        return new ConnectionEvent
        {
            Id = reader.GetInt32(reader.GetOrdinal("Id")),
            RemoteBlackID = reader.IsDBNull(reader.GetOrdinal("RemoteBlackID"))
                ? null
                : reader.GetString(reader.GetOrdinal("RemoteBlackID")),
            RemoteIP = reader.GetString(reader.GetOrdinal("RemoteIP")),
            RemotePort = reader.GetInt32(reader.GetOrdinal("RemotePort")),
            InitiatorBlackID = initiatorBlackID,
            TargetBlackID = targetBlackID,
            EventTypeId = eventTypeId,
            EventType = (ConnectionEventType)reader.GetInt32(reader.GetOrdinal("EventType")),
            Direction = (ConnectionDirection)reader.GetInt32(reader.GetOrdinal("Direction")),
            Message = reader.GetString(reader.GetOrdinal("Message")),
            ErrorDetails = reader.IsDBNull(reader.GetOrdinal("ErrorDetails"))
                ? null
                : reader.GetString(reader.GetOrdinal("ErrorDetails")),
            IsAuthenticated = reader.GetInt32(reader.GetOrdinal("IsAuthenticated")) == 1,
            Timestamp = DateTime.Parse(reader.GetString(reader.GetOrdinal("Timestamp"))),
            Duration = duration,
            BytesSent = reader.GetInt64(reader.GetOrdinal("BytesSent")),
            BytesReceived = reader.GetInt64(reader.GetOrdinal("BytesReceived"))
        };
    }
}

/// <summary>
/// Статистика з'єднань
/// </summary>
public class ConnectionStatistics
{
    public int TotalEvents { get; set; }
    public int SuccessfulConnections { get; set; }
    public int FailedAuthentications { get; set; }
    public int AuthenticatedEvents { get; set; }
    public long TotalBytesSent { get; set; }
    public long TotalBytesReceived { get; set; }
}
