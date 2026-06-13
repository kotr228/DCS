using Microsoft.Data.Sqlite;
using AsmodayCat.Shared.Models;

namespace AsmodayCat.Core.Data.Repositories;

public class ModelPoolRepository
{
    private readonly AsmodayDatabase _db;

    public ModelPoolRepository(AsmodayDatabase db) => _db = db;

    public List<LlmModelDto> GetKnownModels()
    {
        using var conn = _db.CreateConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM KnownModels ORDER BY IsCustom, Name;";
        using var r = cmd.ExecuteReader();
        var list = new List<LlmModelDto>();
        while (r.Read())
        {
            list.Add(new LlmModelDto
            {
                Name            = r.GetString(r.GetOrdinal("Name")),
                RecommendedTask = r.GetString(r.GetOrdinal("RecommendedTask"))
            });
        }
        return list;
    }

    public void AddCustomModel(string name)
    {
        using var conn = _db.CreateConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT OR IGNORE INTO KnownModels (Id, Name, RecommendedTask, IsCustom)
            VALUES (@id, @name, 'Custom', 1);";
        cmd.Parameters.AddWithValue("@id",   Guid.NewGuid().ToString());
        cmd.Parameters.AddWithValue("@name", name);
        cmd.ExecuteNonQuery();
    }

    public void RemoveCustomModel(string name)
    {
        using var conn = _db.CreateConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM KnownModels WHERE Name = @name AND IsCustom = 1;";
        cmd.Parameters.AddWithValue("@name", name);
        cmd.ExecuteNonQuery();
    }
}
