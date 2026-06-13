using Microsoft.Data.Sqlite;
using AsmodayCat.Shared.Models;

namespace AsmodayCat.Core.Data.Repositories;

public class HardwareSettingsRepository
{
    private readonly AsmodayDatabase _db;

    public HardwareSettingsRepository(AsmodayDatabase db) => _db = db;

    public HardwareConfigDto GetConfig()
    {
        using var conn = _db.CreateConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM HardwareSettings WHERE Id = 1;";
        using var r = cmd.ExecuteReader();
        if (r.Read()) return MapToDto(r);

        var defaults = new HardwareConfigDto();
        SaveConfig(defaults);
        return defaults;
    }

    public void SaveConfig(HardwareConfigDto dto)
    {
        using var conn = _db.CreateConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT OR REPLACE INTO HardwareSettings
                (Id, PreferredDevice, ContextWindowSize, MaxVramAllocationMb, CpuThreads, AllowCpuFallback, UpdatedAt)
            VALUES (1, @device, @ctx, @vram, @threads, @fallback, @now);";
        cmd.Parameters.AddWithValue("@device",   dto.PreferredDevice);
        cmd.Parameters.AddWithValue("@ctx",      dto.ContextWindowSize);
        cmd.Parameters.AddWithValue("@vram",     dto.MaxVramAllocationMb);
        cmd.Parameters.AddWithValue("@threads",  dto.CpuThreads);
        cmd.Parameters.AddWithValue("@fallback", dto.AllowCpuFallback ? 1 : 0);
        cmd.Parameters.AddWithValue("@now",      DateTime.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    private static HardwareConfigDto MapToDto(SqliteDataReader r) => new()
    {
        PreferredDevice     = r.GetString(r.GetOrdinal("PreferredDevice")),
        ContextWindowSize   = r.GetInt32(r.GetOrdinal("ContextWindowSize")),
        MaxVramAllocationMb = r.GetInt32(r.GetOrdinal("MaxVramAllocationMb")),
        CpuThreads          = r.GetInt32(r.GetOrdinal("CpuThreads")),
        AllowCpuFallback    = r.GetInt32(r.GetOrdinal("AllowCpuFallback")) == 1
    };
}
