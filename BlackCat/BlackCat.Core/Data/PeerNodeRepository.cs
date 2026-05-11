using Microsoft.Data.Sqlite;
using BlackCat.Shared.Models;

namespace BlackCat.Core.Data;

/// <summary>
/// Репозиторій для роботи з віддаленими вузлами (телефонна книга)
/// </summary>
public class PeerNodeRepository
{
    private readonly string _connectionString;

    public PeerNodeRepository(BlackCatDatabase database)
    {
        _connectionString = database.ConnectionString;
    }

    /// <summary>
    /// Додати новий вузол
    /// </summary>
    public int AddPeerNode(PeerNode node)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        string sql = @"
            INSERT INTO PeerNodes (BlackID, Address, Port, DisplayName, Description, IsTrusted,
                                   CreatedAt, IsActive, StatusId, PublicKey, Tags)
            VALUES (@BlackID, @Address, @Port, @DisplayName, @Description, @IsTrusted,
                    @CreatedAt, @IsActive, @StatusId, @PublicKey, @Tags);
            SELECT last_insert_rowid();
        ";

        using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("@BlackID", node.BlackID);
        command.Parameters.AddWithValue("@Address", node.Address);
        command.Parameters.AddWithValue("@Port", node.Port);
        command.Parameters.AddWithValue("@DisplayName", node.DisplayName);
        command.Parameters.AddWithValue("@Description", node.Description ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@IsTrusted", node.IsTrusted ? 1 : 0);
        command.Parameters.AddWithValue("@CreatedAt", node.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("@IsActive", node.IsActive ? 1 : 0);
        command.Parameters.AddWithValue("@StatusId", node.StatusId ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@PublicKey", node.PublicKey ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@Tags", node.Tags ?? (object)DBNull.Value);

        return Convert.ToInt32(command.ExecuteScalar());
    }

    /// <summary>
    /// Оновити вузол
    /// </summary>
    public void UpdatePeerNode(PeerNode node)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        string sql = @"
            UPDATE PeerNodes
            SET BlackID = @BlackID,
                Address = @Address,
                Port = @Port,
                DisplayName = @DisplayName,
                Description = @Description,
                IsTrusted = @IsTrusted,
                IsActive = @IsActive,
                StatusId = @StatusId,
                PublicKey = @PublicKey,
                Tags = @Tags,
                LastConnectedAt = @LastConnectedAt,
                SuccessfulConnections = @SuccessfulConnections,
                FailedConnections = @FailedConnections
            WHERE Id = @Id
        ";

        using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("@Id", node.Id);
        command.Parameters.AddWithValue("@BlackID", node.BlackID);
        command.Parameters.AddWithValue("@Address", node.Address);
        command.Parameters.AddWithValue("@Port", node.Port);
        command.Parameters.AddWithValue("@DisplayName", node.DisplayName);
        command.Parameters.AddWithValue("@Description", node.Description ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@IsTrusted", node.IsTrusted ? 1 : 0);
        command.Parameters.AddWithValue("@IsActive", node.IsActive ? 1 : 0);
        command.Parameters.AddWithValue("@StatusId", node.StatusId ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@PublicKey", node.PublicKey ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@Tags", node.Tags ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@LastConnectedAt",
            node.LastConnectedAt?.ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@SuccessfulConnections", node.SuccessfulConnections);
        command.Parameters.AddWithValue("@FailedConnections", node.FailedConnections);

        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Видалити вузол
    /// </summary>
    public void DeletePeerNode(int id)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        string sql = "DELETE FROM PeerNodes WHERE Id = @Id";
        using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("@Id", id);
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Отримати вузол за ID
    /// </summary>
    public PeerNode? GetPeerNode(int id)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        string sql = "SELECT * FROM PeerNodes WHERE Id = @Id";
        using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("@Id", id);

        using var reader = command.ExecuteReader();
        if (reader.Read())
        {
            return MapToPeerNode(reader);
        }

        return null;
    }

    /// <summary>
    /// Отримати вузол за Black-ID
    /// </summary>
    public PeerNode? GetPeerNodeByBlackID(string blackID)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        string sql = "SELECT * FROM PeerNodes WHERE BlackID = @BlackID";
        using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("@BlackID", blackID);

        using var reader = command.ExecuteReader();
        if (reader.Read())
        {
            return MapToPeerNode(reader);
        }

        return null;
    }

    /// <summary>
    /// Отримати всі активні вузли
    /// </summary>
    public List<PeerNode> GetActivePeerNodes()
    {
        var list = new List<PeerNode>();

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        string sql = "SELECT * FROM PeerNodes WHERE IsActive = 1 ORDER BY DisplayName";
        using var command = new SqliteCommand(sql, connection);
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            list.Add(MapToPeerNode(reader));
        }

        return list;
    }

    /// <summary>
    /// Отримати всі вузли
    /// </summary>
    public List<PeerNode> GetAllPeerNodes()
    {
        var list = new List<PeerNode>();

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        string sql = "SELECT * FROM PeerNodes ORDER BY DisplayName";
        using var command = new SqliteCommand(sql, connection);
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            list.Add(MapToPeerNode(reader));
        }

        return list;
    }

    /// <summary>
    /// Отримати довірені вузли
    /// </summary>
    public List<PeerNode> GetTrustedPeerNodes()
    {
        var list = new List<PeerNode>();

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        string sql = "SELECT * FROM PeerNodes WHERE IsTrusted = 1 AND IsActive = 1 ORDER BY DisplayName";
        using var command = new SqliteCommand(sql, connection);
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            list.Add(MapToPeerNode(reader));
        }

        return list;
    }

    /// <summary>
    /// Зафіксувати успішне з'єднання
    /// </summary>
    public void RecordSuccessfulConnection(int nodeId)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        string sql = @"
            UPDATE PeerNodes
            SET LastConnectedAt = @Now,
                SuccessfulConnections = SuccessfulConnections + 1
            WHERE Id = @Id
        ";

        using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("@Id", nodeId);
        command.Parameters.AddWithValue("@Now", DateTime.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Зафіксувати невдале з'єднання
    /// </summary>
    public void RecordFailedConnection(int nodeId)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        string sql = @"
            UPDATE PeerNodes
            SET FailedConnections = FailedConnections + 1
            WHERE Id = @Id
        ";

        using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("@Id", nodeId);
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Мапити з reader в модель
    /// </summary>
    private PeerNode MapToPeerNode(SqliteDataReader reader)
    {
        int? statusId = null;
        var statusIdOrdinal = reader.GetOrdinal("StatusId");
        if (!reader.IsDBNull(statusIdOrdinal))
        {
            statusId = reader.GetInt32(statusIdOrdinal);
        }

        return new PeerNode
        {
            Id = reader.GetInt32(reader.GetOrdinal("Id")),
            BlackID = reader.GetString(reader.GetOrdinal("BlackID")),
            Address = reader.GetString(reader.GetOrdinal("Address")),
            Port = reader.GetInt32(reader.GetOrdinal("Port")),
            DisplayName = reader.GetString(reader.GetOrdinal("DisplayName")),
            Description = reader.IsDBNull(reader.GetOrdinal("Description"))
                ? null
                : reader.GetString(reader.GetOrdinal("Description")),
            IsTrusted = reader.GetInt32(reader.GetOrdinal("IsTrusted")) == 1,
            LastConnectedAt = reader.IsDBNull(reader.GetOrdinal("LastConnectedAt"))
                ? null
                : DateTime.Parse(reader.GetString(reader.GetOrdinal("LastConnectedAt"))),
            CreatedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("CreatedAt"))),
            IsActive = reader.GetInt32(reader.GetOrdinal("IsActive")) == 1,
            StatusId = statusId,
            SuccessfulConnections = reader.GetInt32(reader.GetOrdinal("SuccessfulConnections")),
            FailedConnections = reader.GetInt32(reader.GetOrdinal("FailedConnections")),
            PublicKey = reader.IsDBNull(reader.GetOrdinal("PublicKey"))
                ? null
                : reader.GetString(reader.GetOrdinal("PublicKey")),
            Tags = reader.IsDBNull(reader.GetOrdinal("Tags"))
                ? null
                : reader.GetString(reader.GetOrdinal("Tags"))
        };
    }
}
