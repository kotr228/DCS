using AsmodayCat.Shared.Models;

namespace AsmodayCat.Core.Hardware;

// Збирає метрики CPU/GPU/RAM/VRAM + зберігає 60-секундну історію для графіків
public class MetricsCollector
{
    private const int HistorySize = 60;

    private readonly CircularBuffer<ResourceDataPoint>  _cpuHistory      = new(HistorySize);
    private readonly CircularBuffer<ResourceDataPoint>  _gpuHistory      = new(HistorySize);
    private readonly CircularBuffer<ResourceDataPoint>  _ramHistory      = new(HistorySize);
    private readonly CircularBuffer<ResourceDataPoint>  _vramHistory     = new(HistorySize);
    private readonly CircularBuffer<ResourceDataPoint>  _tpsHistory      = new(HistorySize);
    private readonly CircularBuffer<AgentActivityLogDto> _activityHistory = new(10);

    private readonly ResourceManager _resources;
    private readonly Random _rng = new();

    // Активна модель (оновлюється з ModelSwitcher)
    public string ActiveModelName   { get; set; } = string.Empty;
    public string ActiveModelStatus { get; set; } = "Idle";
    public double LastTokensPerSec  { get; set; }

    public MetricsCollector(ResourceManager resources)
    {
        _resources = resources;
    }

    // Викликається Worker'ом щосекунди
    public void Tick()
    {
        var now    = DateTime.Now;
        var status = _resources.GetCurrentStatus();

        var cpuLoad = status.CpuLoad;
        // GPU: mock-дані (доки NVML не підключений) — плавне випадкове значення
        var gpuLoad = GetMockGpuLoad();
        var ramFree = status.RamFree / 1024.0 / 1024.0;
        var vramFree = (double)status.VramFree;

        _cpuHistory.Add(new ResourceDataPoint(cpuLoad, now));
        _gpuHistory.Add(new ResourceDataPoint(gpuLoad, now));
        _ramHistory.Add(new ResourceDataPoint(ramFree, now));
        _vramHistory.Add(new ResourceDataPoint(vramFree, now));
        _tpsHistory.Add(new ResourceDataPoint(LastTokensPerSec, now));
    }

    public SystemStatusDto GetSnapshot(int availableNodes)
    {
        var status = _resources.GetCurrentStatus();

        return new SystemStatusDto
        {
            CpuLoadPercent   = status.CpuLoad,
            RamFreeMb        = status.RamFree / 1024 / 1024,
            RamTotalMb       = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1024 / 1024,
            GpuLoadPercent   = _gpuHistory.Last?.Value ?? 0,
            VramUsedMb       = _resources.GetTotalVramUsedMb(),
            VramTotalMb      = 8192, // Placeholder — замінюється NVML
            ActiveModelName   = ActiveModelName,
            ActiveModelStatus = ActiveModelStatus,
            AvailableNodesCount = availableNodes,
            CpuHistory     = _cpuHistory.ToList(),
            GpuHistory     = _gpuHistory.ToList(),
            RamHistory     = _ramHistory.ToList(),
            VramHistory    = _vramHistory.ToList(),
            TpsHistory     = _tpsHistory.ToList(),
            RecentActivity = _activityHistory.ToList()
        };
    }

    public void LogActivity(string description, string status)
    {
        _activityHistory.Add(new AgentActivityLogDto
        {
            Timestamp         = DateTime.Now,
            ActionDescription = description,
            Status            = status
        });
    }

    private double _mockGpu;
    private double GetMockGpuLoad()
    {
        _mockGpu += (_rng.NextDouble() - 0.5) * 8;
        _mockGpu = Math.Clamp(_mockGpu, 0, 95);
        return _mockGpu;
    }
}

// O(1) кільцевий буфер — той самий патерн що в BlackCat
internal sealed class CircularBuffer<T>
{
    private readonly T[] _data;
    private int _head;
    private int _count;

    public CircularBuffer(int capacity) => _data = new T[capacity];

    public int Count    => _count;
    public int Capacity => _data.Length;
    public T?  Last     => _count > 0 ? _data[(_head - 1 + _data.Length) % _data.Length] : default;

    public void Add(T item)
    {
        _data[_head] = item;
        _head = (_head + 1) % _data.Length;
        if (_count < _data.Length) _count++;
    }

    public List<T> ToList()
    {
        var result = new List<T>(_count);
        int start  = _count < _data.Length ? 0 : _head;
        for (int i = 0; i < _count; i++)
            result.Add(_data[(start + i) % _data.Length]);
        return result;
    }
}
