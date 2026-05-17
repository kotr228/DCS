using BlackCat.Shared.Models;

namespace BlackCat.Core.Services;

/// <summary>
/// Runs on a background Task — calls GetActiveTcpConnections() (expensive P/Invoke) off the UI thread
/// and packages results into TrafficDataPoint structs, then invokes <paramref name="onPoint"/> for
/// lock-free handoff to the UI ViewModel.
/// </summary>
public sealed class BackgroundTrafficCollector : IDisposable
{
    private readonly Action<TrafficDataPoint> _onPoint;
    private readonly ProcessLookupService _processLookupService;
    private readonly Func<FirewallStatistics?> _getStats;

    private CancellationTokenSource? _cts;
    private Task? _task;

    // Single instance per collector — only accessed from the one background Task
    private readonly Random _rng = new();

    public BackgroundTrafficCollector(
        Action<TrafficDataPoint> onPoint,
        ProcessLookupService processLookupService,
        Func<FirewallStatistics?> getStats)
    {
        _onPoint              = onPoint;
        _processLookupService = processLookupService;
        _getStats             = getStats;
    }

    public void Start()
    {
        _cts  = new CancellationTokenSource();
        _task = Task.Run(() => CollectLoopAsync(_cts.Token));
    }

    public void Stop() => _cts?.Cancel();

    private async Task CollectLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var stats = _getStats();
                if (stats != null)
                    _onPoint(BuildPoint(stats));
            }
            catch { /* individual errors are non-fatal */ }

            try { await Task.Delay(1000, ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    private TrafficDataPoint BuildPoint(FirewallStatistics stats)
    {
        // P/Invoke — executed here, never on UI thread
        var connections = _processLookupService.GetActiveTcpConnections();

        // Single GroupBy pass shared by both chart series and stats table
        var processEntries = connections
            .Where(c => !string.IsNullOrEmpty(c.ProcessName))
            .GroupBy(c => c.ProcessName)
            .Select(g =>
            {
                int count        = g.Count();
                long estimatedBytes = count * 1024L * _rng.Next(1, 100);
                return new ProcessTrafficEntry(g.Key, count * 10.0, count, estimatedBytes);
            })
            .OrderByDescending(e => e.EstimatedKBps)
            .Take(5)
            .ToArray();

        return new TrafficDataPoint(
            speedKBps:       stats.BytesPerSecond / 1024.0,
            totalPackets:    stats.TotalPackets,
            allowedPackets:  stats.AllowedPackets,
            blockedPackets:  stats.BlockedPackets,
            tunneledPackets: stats.TunneledPackets,
            uptime:          stats.Uptime,
            timestamp:       DateTime.Now,
            processTraffic:  processEntries);
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
    }
}
