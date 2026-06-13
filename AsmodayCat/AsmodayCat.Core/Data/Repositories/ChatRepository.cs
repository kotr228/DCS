using System.Text.Json;
using Microsoft.Data.Sqlite;
using AsmodayCat.Shared.Models;

namespace AsmodayCat.Core.Data.Repositories;

public record ChatSessionRecord(Guid Id, string Title, DateTime CreatedAt, DateTime UpdatedAt);

public class ChatRepository
{
    private readonly AsmodayDatabase _db;

    public ChatRepository(AsmodayDatabase db) => _db = db;

    public Guid CreateSession(string title)
    {
        var id  = Guid.NewGuid();
        var now = DateTime.UtcNow.ToString("O");

        using var conn = _db.CreateConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO ChatSessions (Id, Title, CreatedAt, UpdatedAt)
            VALUES (@id, @title, @now, @now);";
        cmd.Parameters.AddWithValue("@id",    id.ToString());
        cmd.Parameters.AddWithValue("@title", title);
        cmd.Parameters.AddWithValue("@now",   now);
        cmd.ExecuteNonQuery();
        return id;
    }

    public List<ChatSessionRecord> GetSessions()
    {
        using var conn = _db.CreateConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM ChatSessions ORDER BY UpdatedAt DESC;";
        using var r = cmd.ExecuteReader();
        var list = new List<ChatSessionRecord>();
        while (r.Read())
        {
            list.Add(new ChatSessionRecord(
                Guid.Parse(r.GetString(r.GetOrdinal("Id"))),
                r.GetString(r.GetOrdinal("Title")),
                DateTime.Parse(r.GetString(r.GetOrdinal("CreatedAt"))),
                DateTime.Parse(r.GetString(r.GetOrdinal("UpdatedAt")))));
        }
        return list;
    }

    public List<ChatMessageDto> GetMessagesBySession(Guid sessionId)
    {
        using var conn = _db.CreateConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM ChatMessages WHERE SessionId = @sid ORDER BY Timestamp;";
        cmd.Parameters.AddWithValue("@sid", sessionId.ToString());
        using var r = cmd.ExecuteReader();
        var list = new List<ChatMessageDto>();
        while (r.Read()) list.Add(MapMessage(r));
        return list;
    }

    public void AddMessage(Guid sessionId, ChatMessageDto message)
    {
        var now        = DateTime.UtcNow.ToString("O");
        var attachJson = message.AttachmentPaths.Count > 0
            ? JsonSerializer.Serialize(message.AttachmentPaths)
            : null;

        using var conn = _db.CreateConnection();
        conn.Open();

        using var ins = conn.CreateCommand();
        ins.CommandText = @"
            INSERT INTO ChatMessages (Id, SessionId, Role, Content, Timestamp, AttachmentsJson)
            VALUES (@id, @sid, @role, @content, @ts, @att);";
        ins.Parameters.AddWithValue("@id",      message.Id);
        ins.Parameters.AddWithValue("@sid",     sessionId.ToString());
        ins.Parameters.AddWithValue("@role",    message.Role.ToString());
        ins.Parameters.AddWithValue("@content", message.Content);
        ins.Parameters.AddWithValue("@ts",      message.Timestamp.ToString("O"));
        ins.Parameters.AddWithValue("@att",     (object?)attachJson ?? DBNull.Value);
        ins.ExecuteNonQuery();

        using var upd = conn.CreateCommand();
        upd.CommandText = "UPDATE ChatSessions SET UpdatedAt = @now WHERE Id = @sid;";
        upd.Parameters.AddWithValue("@now", now);
        upd.Parameters.AddWithValue("@sid", sessionId.ToString());
        upd.ExecuteNonQuery();
    }

    private static ChatMessageDto MapMessage(SqliteDataReader r)
    {
        var dto = new ChatMessageDto
        {
            Id        = r.GetString(r.GetOrdinal("Id")),
            Role      = Enum.Parse<ChatRole>(r.GetString(r.GetOrdinal("Role"))),
            Content   = r.GetString(r.GetOrdinal("Content")),
            Timestamp = DateTime.Parse(r.GetString(r.GetOrdinal("Timestamp")))
        };
        var attOrd = r.GetOrdinal("AttachmentsJson");
        if (!r.IsDBNull(attOrd))
            dto.AttachmentPaths = JsonSerializer.Deserialize<List<string>>(r.GetString(attOrd)) ?? [];
        return dto;
    }
}
