namespace BlackCat.Shared.Models;

/// <summary>
/// Value-type snapshot passed through ConcurrentQueue — zero GC pressure.
/// </summary>
public readonly struct TrafficDataPoint
{
    public readonly double SpeedKBps;
    public readonly long TotalPackets;
    public readonly long AllowedPackets;
    public readonly long BlockedPackets;
    public readonly long TunneledPackets;
    public readonly TimeSpan Uptime;
    public readonly DateTime Timestamp;
    public readonly ProcessTrafficEntry[] ProcessTraffic;

    public TrafficDataPoint(
        double speedKBps,
        long totalPackets,
        long allowedPackets,
        long blockedPackets,
        long tunneledPackets,
        TimeSpan uptime,
        DateTime timestamp,
        ProcessTrafficEntry[] processTraffic)
    {
        SpeedKBps       = speedKBps;
        TotalPackets    = totalPackets;
        AllowedPackets  = allowedPackets;
        BlockedPackets  = blockedPackets;
        TunneledPackets = tunneledPackets;
        Uptime          = uptime;
        Timestamp       = timestamp;
        ProcessTraffic  = processTraffic;
    }
}

/// <summary>
/// Per-process traffic entry — value type, no heap allocation per entry.
/// </summary>
public readonly struct ProcessTrafficEntry
{
    public readonly string ProcessName;
    public readonly double EstimatedKBps;
    public readonly int ConnectionCount;
    public readonly long EstimatedBytes;

    public ProcessTrafficEntry(string processName, double estimatedKBps, int connectionCount, long estimatedBytes)
    {
        ProcessName     = processName;
        EstimatedKBps   = estimatedKBps;
        ConnectionCount = connectionCount;
        EstimatedBytes  = estimatedBytes;
    }
}
