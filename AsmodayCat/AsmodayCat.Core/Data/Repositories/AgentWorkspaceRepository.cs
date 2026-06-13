using Microsoft.Data.Sqlite;
using AsmodayCat.Shared.Models;

namespace AsmodayCat.Core.Data.Repositories;

public class AgentWorkspaceRepository
{
    private readonly AsmodayDatabase _db;

    public AgentWorkspaceRepository(AsmodayDatabase db) => _db = db;

    public List<AgentRuleDto> GetAll()
    {
        using var conn = _db.CreateConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM AgentWorkspaces ORDER BY CreatedAt;";
        using var r = cmd.ExecuteReader();
        var list = new List<AgentRuleDto>();
        while (r.Read()) list.Add(MapToDto(r));
        return list;
    }

    public List<AgentRuleDto> GetActive()
    {
        using var conn = _db.CreateConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM AgentWorkspaces WHERE IsActive = 1 ORDER BY CreatedAt;";
        using var r = cmd.ExecuteReader();
        var list = new List<AgentRuleDto>();
        while (r.Read()) list.Add(MapToDto(r));
        return list;
    }

    public AgentRuleDto? Get(Guid id)
    {
        using var conn = _db.CreateConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM AgentWorkspaces WHERE Id = @id;";
        cmd.Parameters.AddWithValue("@id", id.ToString());
        using var r = cmd.ExecuteReader();
        return r.Read() ? MapToDto(r) : null;
    }

    public void Add(AgentRuleDto rule)
    {
        using var conn = _db.CreateConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO AgentWorkspaces
                (Id, InputPath, OutputPath, ActionType, SystemPrompt, AllowedExtensions, AllowInternetAccess, IsActive, CreatedAt)
            VALUES (@id, @in, @out, @action, @prompt, @ext, @internet, @active, @now);";
        cmd.Parameters.AddWithValue("@id",       rule.Id.ToString());
        cmd.Parameters.AddWithValue("@in",       rule.InputPath);
        cmd.Parameters.AddWithValue("@out",      rule.OutputPath);
        cmd.Parameters.AddWithValue("@action",   rule.ActionType);
        cmd.Parameters.AddWithValue("@prompt",   rule.SystemPrompt);
        cmd.Parameters.AddWithValue("@ext",      rule.AllowedExtensions);
        cmd.Parameters.AddWithValue("@internet", rule.AllowInternetAccess ? 1 : 0);
        cmd.Parameters.AddWithValue("@active",   rule.IsActive ? 1 : 0);
        cmd.Parameters.AddWithValue("@now",      DateTime.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    public void Delete(Guid id)
    {
        using var conn = _db.CreateConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM AgentWorkspaces WHERE Id = @id;";
        cmd.Parameters.AddWithValue("@id", id.ToString());
        cmd.ExecuteNonQuery();
    }

    public void Update(AgentRuleDto rule)
    {
        using var conn = _db.CreateConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE AgentWorkspaces SET
                InputPath = @in, OutputPath = @out, ActionType = @action,
                SystemPrompt = @prompt, AllowedExtensions = @ext,
                AllowInternetAccess = @internet, IsActive = @active
            WHERE Id = @id;";
        cmd.Parameters.AddWithValue("@id",       rule.Id.ToString());
        cmd.Parameters.AddWithValue("@in",       rule.InputPath);
        cmd.Parameters.AddWithValue("@out",      rule.OutputPath);
        cmd.Parameters.AddWithValue("@action",   rule.ActionType);
        cmd.Parameters.AddWithValue("@prompt",   rule.SystemPrompt);
        cmd.Parameters.AddWithValue("@ext",      rule.AllowedExtensions);
        cmd.Parameters.AddWithValue("@internet", rule.AllowInternetAccess ? 1 : 0);
        cmd.Parameters.AddWithValue("@active",   rule.IsActive ? 1 : 0);
        cmd.ExecuteNonQuery();
    }

    private static AgentRuleDto MapToDto(SqliteDataReader r) => new()
    {
        Id                  = Guid.Parse(r.GetString(r.GetOrdinal("Id"))),
        InputPath           = r.GetString(r.GetOrdinal("InputPath")),
        OutputPath          = r.GetString(r.GetOrdinal("OutputPath")),
        ActionType          = r.GetString(r.GetOrdinal("ActionType")),
        SystemPrompt        = r.GetString(r.GetOrdinal("SystemPrompt")),
        AllowedExtensions   = r.GetString(r.GetOrdinal("AllowedExtensions")),
        AllowInternetAccess = r.GetInt32(r.GetOrdinal("AllowInternetAccess")) == 1,
        IsActive            = r.GetInt32(r.GetOrdinal("IsActive")) == 1
    };
}
