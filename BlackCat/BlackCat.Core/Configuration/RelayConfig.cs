using System.Text.Json;

namespace BlackCat.Core.Configuration;

public class RelayConfig
{
    public string Host         { get; set; } = string.Empty;
    public int    Port         { get; set; } = 9997;
    public bool   Enabled      { get; set; } = false;
    public bool   RunAsServer  { get; set; } = false;
    public int    ServerPort   { get; set; } = 9997;

    private static readonly string _path = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "relay_config.json");

    public static RelayConfig Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                var json = File.ReadAllText(_path);
                return JsonSerializer.Deserialize<RelayConfig>(json) ?? new RelayConfig();
            }
        }
        catch { }
        return new RelayConfig();
    }

    public void Save()
    {
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_path, json);
    }
}
