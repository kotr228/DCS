using System.Text.Json;
using AsmodayCat.Shared.Models;

namespace AsmodayCat.Service;

public class HardwareConfigStore
{
    private static readonly string ConfigPath =
        Path.Combine(AppContext.BaseDirectory, "hardware_config.json");

    private static readonly JsonSerializerOptions _opts =
        new() { WriteIndented = true };

    private HardwareConfigDto _current = new();

    public HardwareConfigDto Current => _current;

    public HardwareConfigDto Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                _current = JsonSerializer.Deserialize<HardwareConfigDto>(json) ?? new();
            }
        }
        catch { /* corrupt config — use defaults */ }

        return _current;
    }

    public void Save(HardwareConfigDto config)
    {
        _current = config;
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(config, _opts));
    }
}
