using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows.Threading;
using BlackCat.Shared.Models;
using BlackCat.UI;
using LiveCharts;
using LiveCharts.Wpf;

namespace BlackCat.UI.ViewModels;

/// <summary>
/// O(1) circular buffer — fixed-capacity ring that overwrites oldest entries.
/// Not thread-safe; accessed exclusively from the UI-thread DispatcherTimer.
/// </summary>
internal sealed class CircularBuffer<T>
{
    private readonly T[] _data;
    private int _head;
    private int _count;

    public CircularBuffer(int capacity) => _data = new T[capacity];

    public int Count    => _count;
    public int Capacity => _data.Length;

    /// O(1) — overwrites the oldest slot when full.
    public void Add(T item)
    {
        _data[_head] = item;
        _head = (_head + 1) % _data.Length;
        if (_count < _data.Length) _count++;
    }

    /// Returns items in oldest-first order without allocating an intermediate list.
    public T[] ToArray()
    {
        var result = new T[_count];
        int start  = _count < _data.Length ? 0 : _head;
        for (int i = 0; i < _count; i++)
            result[i] = _data[(start + i) % _data.Length];
        return result;
    }
}

/// <summary>
/// MVVM ViewModel that owns all chart state.
///
/// Data flow:
///   BackgroundTrafficCollector (background Task)
///       → ConcurrentQueue&lt;TrafficDataPoint&gt;  (lock-free handoff)
///           → 50 ms DispatcherTimer (UI thread)
///               → ChartValues slid via RemoveAt(0)+Add (2 events/tick at 1 Hz)
///
/// Memory:
///   • CircularBuffer keeps exactly MaxHistory doubles — no RemoveAt(0) O(n) shifts.
///   • TrafficDataPoint is a value type — no GC pressure in the queue.
///   • ProcessStatItem pool: existing items are mutated in-place; ObservableCollection
///     is never cleared (no spurious CollectionChanged storms).
/// </summary>
public sealed class TrafficChartViewModel : INotifyPropertyChanged, IDisposable
{
    private const int MaxHistory  = 60;   // seconds of rolling history
    private const int ThrottleMs  = 50;   // 20 Hz — one chart refresh per 50 ms

    // ── Lock-free input queue ────────────────────────────────────────────────
    private readonly ConcurrentQueue<TrafficDataPoint> _queue = new();

    // ── Circular history buffers (UI thread only) ────────────────────────────
    private readonly CircularBuffer<double> _speedHistory = new(MaxHistory);
    private readonly CircularBuffer<string> _labelHistory = new(MaxHistory);

    // ── LiveCharts data sources ───────────────────────────────────────────────
    public SeriesCollection Series    { get; }
    public ChartValues<double> SpeedValues { get; } = new();

    // X-axis label array — replaced atomically on the UI thread; LabelFormatter
    // captures the reference, so no locking is required.
    private string[] _labels = Array.Empty<string>();
    public Func<double, string> LabelFormatter { get; }

    // Per-process series — keyed by process name
    private readonly Dictionary<string, ChartValues<double>> _processValues = new();
    private readonly Dictionary<string, CircularBuffer<double>> _processHistory = new();
    private readonly Dictionary<string, Color> _processColors = new();
    private readonly List<Color> _availableColors =
    [
        Color.FromRgb(106, 153, 85),
        Color.FromRgb(244, 135, 113),
        Color.FromRgb(86, 156, 214),
        Color.FromRgb(220, 220, 170),
        Color.FromRgb(197, 134, 192),
        Color.FromRgb(78, 201, 176),
        Color.FromRgb(206, 145, 120),
        Color.FromRgb(156, 220, 254),
    ];
    private int _colorIndex;

    // ── Process stats table (object pooling — no Clear/re-add) ───────────────
    public ObservableCollection<ProcessStatItem> ProcessStats { get; } = new();
    private readonly Dictionary<string, ProcessStatItem> _processStatPool = new();

    // ── Observable stat-label strings ────────────────────────────────────────
    private string _totalPackets    = "0";
    private string _allowedPackets  = "0";
    private string _blockedPackets  = "0";
    private string _tunneledPackets = "0";
    private string _speedText       = "0 KB/s";
    private string _uptimeText      = "Час роботи: 00:00:00";

    public string TotalPackets    { get => _totalPackets;    private set => Set(ref _totalPackets,    value); }
    public string AllowedPackets  { get => _allowedPackets;  private set => Set(ref _allowedPackets,  value); }
    public string BlockedPackets  { get => _blockedPackets;  private set => Set(ref _blockedPackets,  value); }
    public string TunneledPackets { get => _tunneledPackets; private set => Set(ref _tunneledPackets, value); }
    public string SpeedText       { get => _speedText;       private set => Set(ref _speedText,       value); }
    public string UptimeText      { get => _uptimeText;      private set => Set(ref _uptimeText,      value); }

    // ── Throttle timer ────────────────────────────────────────────────────────
    private readonly DispatcherTimer _throttleTimer;

    public TrafficChartViewModel()
    {
        LabelFormatter = value =>
        {
            var lbl = _labels;   // capture current reference — no lock needed
            var idx = (int)value;
            return (uint)idx < (uint)lbl.Length ? lbl[idx] : string.Empty;
        };

        Series = new SeriesCollection
        {
            new ColumnSeries
            {
                Title          = "Швидкість (KB/s)",
                Values         = SpeedValues,
                Fill           = new SolidColorBrush(Color.FromArgb(180, 78, 201, 176)),
                MaxColumnWidth = 30,
                ColumnPadding  = 4,
            }
        };

        _throttleTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(ThrottleMs)
        };
        _throttleTimer.Tick += OnThrottleTick;
    }

    // ── Public control ────────────────────────────────────────────────────────

    public void Start() => _throttleTimer.Start();

    public void Stop()
    {
        _throttleTimer.Stop();
        // Drain leftover items so the queue doesn't hold stale data on restart
        while (_queue.TryDequeue(out _)) { }
    }

    /// Called from BackgroundTrafficCollector — lock-free, never blocks.
    public void Enqueue(TrafficDataPoint point) => _queue.Enqueue(point);

    // ── Throttle tick (UI thread, 20 Hz) ─────────────────────────────────────

    private void OnThrottleTick(object? sender, EventArgs e)
    {
        if (_queue.IsEmpty) return;

        // Drain ALL pending points; keep only the most recent.
        // Intermediate points are discarded — this is intentional: we only
        // display one sample per render tick regardless of how many arrived.
        TrafficDataPoint latest = default;
        bool hasData = false;
        while (_queue.TryDequeue(out var point))
        {
            latest  = point;
            hasData = true;
        }
        if (!hasData) return;

        // ── Update circular history buffers ───────────────────────────────
        _speedHistory.Add(latest.SpeedKBps);
        _labelHistory.Add(latest.Timestamp.ToString("mm:ss"));

        // Swap label array atomically (reference write is atomic on x64)
        _labels = _labelHistory.ToArray();

        // ── Slide speed chart window: RemoveAt(0) + Add = 2 CollectionChanged ──
        // Background collector fires at 1 Hz, so this path runs at most once
        // per second — RemoveAt(0) on 60 items is ~60 shifts, negligible at 1 Hz.
        if (SpeedValues.Count >= MaxHistory)
            SpeedValues.RemoveAt(0);
        SpeedValues.Add(latest.SpeedKBps);

        // ── Update per-process series ─────────────────────────────────────
        UpdateProcessSeries(latest.ProcessTraffic);

        // ── Update process stats table (pool — no Clear/re-add) ──────────
        UpdateProcessStatsTable(latest.ProcessTraffic);

        // ── Update stat labels (simple string assignment) ─────────────────
        TotalPackets    = latest.TotalPackets.ToString("N0");
        AllowedPackets  = latest.AllowedPackets.ToString("N0");
        BlockedPackets  = latest.BlockedPackets.ToString("N0");
        TunneledPackets = latest.TunneledPackets.ToString("N0");
        SpeedText       = $"{latest.SpeedKBps:F2} KB/s";
        UptimeText      = $"Час роботи: {latest.Uptime:hh\\:mm\\:ss}";
    }

    // ── Per-process series management ─────────────────────────────────────────

    private void UpdateProcessSeries(ProcessTrafficEntry[] entries)
    {
        var active = new HashSet<string>(entries.Length);

        foreach (ref readonly var entry in entries.AsSpan())
        {
            active.Add(entry.ProcessName);

            if (!_processValues.TryGetValue(entry.ProcessName, out var values))
            {
                values = CreateProcessSeries(entry.ProcessName);
            }

            _processHistory[entry.ProcessName].Add(entry.EstimatedKBps);

            if (values.Count >= MaxHistory)
                values.RemoveAt(0);
            values.Add(entry.EstimatedKBps);
        }

        // Advance inactive process histories and prune stale series
        var toRemove = new List<string>();
        foreach (var (name, buf) in _processHistory)
        {
            if (active.Contains(name)) continue;

            buf.Add(0.0);

            // Remove series after 10 consecutive zero samples
            var snap = buf.ToArray();
            int recentZeros = 0;
            for (int i = snap.Length - 1; i >= 0 && snap[i] == 0.0; i--)
                recentZeros++;

            if (recentZeros >= 10)
                toRemove.Add(name);
            else
            {
                var values = _processValues[name];
                if (values.Count >= MaxHistory)
                    values.RemoveAt(0);
                values.Add(0.0);
            }
        }

        foreach (var name in toRemove)
            RemoveProcessSeries(name);
    }

    private ChartValues<double> CreateProcessSeries(string processName)
    {
        var color = _availableColors[_colorIndex++ % _availableColors.Count];
        _processColors[processName] = color;

        var buf    = new CircularBuffer<double>(MaxHistory);
        var values = new ChartValues<double>();

        // Pre-fill with zeros to align with existing speed history
        for (int i = 0; i < _speedHistory.Count; i++)
        {
            buf.Add(0.0);
            values.Add(0.0);
        }

        _processHistory[processName] = buf;
        _processValues[processName]  = values;

        Series.Add(new LineSeries
        {
            Title          = processName,
            Values         = values,
            Stroke         = new SolidColorBrush(color),
            Fill           = System.Windows.Media.Brushes.Transparent,
            PointGeometry  = null,
            LineSmoothness = 0.3,
        });

        return values;
    }

    private void RemoveProcessSeries(string name)
    {
        for (int i = 0; i < Series.Count; i++)
        {
            if (Series[i].Title == name)
            {
                Series.RemoveAt(i);
                break;
            }
        }
        _processValues.Remove(name);
        _processHistory.Remove(name);
        _processColors.Remove(name);
    }

    // ── Process stats table (in-place mutation — avoids ObservableCollection churn) ──

    private void UpdateProcessStatsTable(ProcessTrafficEntry[] entries)
    {
        // Update existing pooled items in-place; add only genuinely new processes
        var seen = new HashSet<string>(entries.Length);

        foreach (ref readonly var entry in entries.AsSpan())
        {
            seen.Add(entry.ProcessName);

            if (_processStatPool.TryGetValue(entry.ProcessName, out var item))
            {
                item.PacketCount = entry.ConnectionCount;
                item.TotalBytes += entry.EstimatedBytes;
                item.UpdateTrafficDisplay();
            }
            else
            {
                var newItem = new ProcessStatItem
                {
                    ProcessName = entry.ProcessName,
                    PacketCount = entry.ConnectionCount,
                    TotalBytes  = entry.EstimatedBytes,
                };
                newItem.UpdateTrafficDisplay();
                _processStatPool[entry.ProcessName] = newItem;
                ProcessStats.Add(newItem);
            }
        }

        // Remove items for processes that have disappeared entirely
        var toRemove = _processStatPool.Keys.Where(k => !seen.Contains(k)).ToList();
        foreach (var key in toRemove)
        {
            if (_processStatPool.TryGetValue(key, out var item))
                ProcessStats.Remove(item);
            _processStatPool.Remove(key);
        }

        // Sort in-place by swapping ObservableCollection entries (no Clear → no flicker)
        SortProcessStats();
    }

    private void SortProcessStats()
    {
        for (int i = 0; i < ProcessStats.Count - 1; i++)
        {
            int maxIdx = i;
            for (int j = i + 1; j < ProcessStats.Count; j++)
            {
                if (ProcessStats[j].TotalBytes > ProcessStats[maxIdx].TotalBytes)
                    maxIdx = j;
            }
            if (maxIdx != i)
            {
                // Swap via Move — triggers 2 CollectionChanged(Move) instead of Clear+re-add
                ProcessStats.Move(maxIdx, i);
            }
        }
    }

    // ── INotifyPropertyChanged ────────────────────────────────────────────────

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    // ── IDisposable ───────────────────────────────────────────────────────────

    public void Dispose()
    {
        _throttleTimer.Stop();
        while (_queue.TryDequeue(out _)) { }
    }
}
