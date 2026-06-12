using AsmodayCat.Shared.Models;

namespace AsmodayCat.Core.Hardware;

public class ResourceManager
{
    private readonly Dictionary<string, int> _modelVramUsage = new();
    private readonly object _lock = new();

    public void RegisterModel(string modelId, int vramMb)
    {
        lock (_lock)
            _modelVramUsage[modelId] = vramMb;
    }

    public void UnregisterModel(string modelId)
    {
        lock (_lock)
            _modelVramUsage.Remove(modelId);
    }

    public int GetTotalVramUsedMb()
    {
        lock (_lock)
            return _modelVramUsage.Values.Sum();
    }

    public NodeResourceStatus GetCurrentStatus()
    {
        return new NodeResourceStatus
        {
            CpuLoad = GetCpuLoad(),
            RamFree = GetFreeRamBytes(),
            GpuLoad = 0,
            VramFree = 0
        };
    }

    private static double GetCpuLoad()
    {
        try
        {
#if WINDOWS
            using var counter = new System.Diagnostics.PerformanceCounter(
                "Processor", "% Processor Time", "_Total");
            counter.NextValue();
            Thread.Sleep(100);
            return counter.NextValue();
#else
            return 0;
#endif
        }
        catch { return 0; }
    }

    private static long GetFreeRamBytes()
    {
        try
        {
            var info = GC.GetGCMemoryInfo();
            return info.TotalAvailableMemoryBytes - info.HeapSizeBytes;
        }
        catch { return 0; }
    }
}
