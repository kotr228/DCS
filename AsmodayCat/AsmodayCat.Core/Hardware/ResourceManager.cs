using System.Runtime.InteropServices;
using AsmodayCat.Shared.Models;

namespace AsmodayCat.Core.Hardware;

// System load: CPU (GetSystemTimes), RAM (GlobalMemoryStatusEx) and GPU (nvidia-smi).
public class ResourceManager
{
    private readonly GpuMonitor _gpu;
    private readonly object _lock = new();

    private ulong _lastIdle, _lastKernel, _lastUser;
    private double _cpuLoad;

    public ResourceManager(GpuMonitor gpu) => _gpu = gpu;

    public GpuMonitor Gpu => _gpu;

    // VRAM in use on the GPU (all processes), MB
    public int GetTotalVramUsedMb() => (int)_gpu.VramUsedMb;

    // Called every second by the worker: CPU load is the change since the previous sample
    public void Sample()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user)) return;
        lock (_lock)
        {
            var idleDelta  = idle.Value   - _lastIdle;
            var totalDelta = (kernel.Value - _lastKernel) + (user.Value - _lastUser);   // kernel time includes idle
            if (_lastKernel != 0 && totalDelta > 0)
                _cpuLoad = Math.Clamp(100.0 * (totalDelta - idleDelta) / totalDelta, 0, 100);
            (_lastIdle, _lastKernel, _lastUser) = (idle.Value, kernel.Value, user.Value);
        }
    }

    public double CpuLoad { get { lock (_lock) return _cpuLoad; } }

    public static (long FreeBytes, long TotalBytes) GetRam()
    {
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        return GlobalMemoryStatusEx(ref status)
            ? ((long)status.AvailPhys, (long)status.TotalPhys)
            : (0, 0);
    }

    public NodeResourceStatus GetCurrentStatus()
    {
        var (free, _) = GetRam();
        return new NodeResourceStatus
        {
            CpuLoad  = CpuLoad,
            RamFree  = free,
            GpuLoad  = _gpu.LoadPercent,
            VramFree = _gpu.VramFreeMb * 1024 * 1024
        };
    }

    // ── Win32 ────────────────────────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public uint Low, High;
        public ulong Value => ((ulong)High << 32) | Low;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint  Length, MemoryLoad;
        public ulong TotalPhys, AvailPhys, TotalPageFile, AvailPageFile, TotalVirtual, AvailVirtual, AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out FileTime idle, out FileTime kernel, out FileTime user);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);
}
