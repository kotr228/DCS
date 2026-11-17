using System.Diagnostics;
using System.Globalization;

namespace AsmodayCat.Core.Hardware;

// Real GPU numbers from nvidia-smi (installed with the NVIDIA driver).
// Without an NVIDIA GPU everything stays at zero and IsAvailable is false.
public sealed class GpuMonitor
{
    private readonly object _lock = new();
    private bool _checked;
    private string? _smiPath;

    public bool   IsAvailable  { get; private set; }
    public string Name         { get; private set; } = string.Empty;
    public double LoadPercent  { get; private set; }
    public long   VramUsedMb   { get; private set; }
    public long   VramTotalMb  { get; private set; }
    public long   VramFreeMb   => Math.Max(0, VramTotalMb - VramUsedMb);

    // Called by the service worker every couple of seconds
    public void Refresh()
    {
        lock (_lock)
        {
            if (!_checked)
            {
                _checked = true;
                _smiPath = FindSmi();
            }
            if (_smiPath is null) return;

            try
            {
                var psi = new ProcessStartInfo(_smiPath,
                    "--query-gpu=name,utilization.gpu,memory.used,memory.total --format=csv,noheader,nounits")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute        = false,
                    CreateNoWindow         = true
                };
                using var p = Process.Start(psi)!;
                var line = p.StandardOutput.ReadLine();
                p.WaitForExit(3000);
                if (string.IsNullOrWhiteSpace(line)) return;

                // First GPU only: "NVIDIA GeForce RTX 3060 Ti, 12, 6403, 8192"
                var parts = line.Split(',').Select(s => s.Trim()).ToArray();
                if (parts.Length < 4) return;

                Name        = parts[0];
                LoadPercent = double.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out var load) ? load : 0;
                VramUsedMb  = long.TryParse(parts[2], out var used) ? used : 0;
                VramTotalMb = long.TryParse(parts[3], out var total) ? total : 0;
                IsAvailable = VramTotalMb > 0;
            }
            catch (Exception)
            {
                IsAvailable = false;
            }
        }
    }

    private static string? FindSmi()
    {
        var candidates = new List<string>
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "nvidia-smi.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "NVIDIA Corporation", "NVSMI", "nvidia-smi.exe")
        };
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            if (dir.Length > 0) candidates.Add(Path.Combine(dir.Trim('"'), "nvidia-smi.exe"));
        return candidates.FirstOrDefault(File.Exists);
    }
}
