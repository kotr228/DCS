using AsmodayCat.Shared.Text;
using System.Collections.Concurrent;
using System.Threading.Channels;
using AsmodayCat.Shared.Models;
using Microsoft.Extensions.Logging;

namespace AsmodayCat.Agent.Watchers;

// Watches one folder and emits each new or changed file once, after it has stopped
// changing and can be opened. Skips temp files and the agent's own output.
public sealed class FolderObserver : IDisposable
{
    public const string ResultSuffix = ".result.md";
    public const string ErrorSuffix  = ".error.txt";

    private static readonly TimeSpan QuietPeriod  = TimeSpan.FromSeconds(1.5);
    private static readonly TimeSpan MaxLockWait  = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    private static readonly HashSet<string> TempExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".tmp", ".temp", ".crdownload", ".part", ".partial", ".download", ".swp", ".lock" };

    private readonly ILogger _logger;
    private readonly Channel<string> _queue = Channel.CreateUnbounded<string>(
        new UnboundedChannelOptions { SingleReader = true });

    // path → (first event, last event)
    private readonly ConcurrentDictionary<string, (DateTime First, DateTime Last)> _pending =
        new(StringComparer.OrdinalIgnoreCase);

    // path → state when it was queued, to skip events that don't change the file
    private readonly ConcurrentDictionary<string, (DateTime Write, long Length)> _queued =
        new(StringComparer.OrdinalIgnoreCase);

    // Files written by the agent itself
    private readonly ConcurrentDictionary<string, byte> _ignored = new(StringComparer.OrdinalIgnoreCase);

    private FileSystemWatcher? _watcher;
    private Timer?             _timer;
    private int                _ticking;

    public FolderObserver(AgentFolderConfig config, string outputPath, ILogger logger)
    {
        Config     = config;
        OutputPath = outputPath;
        _logger    = logger;
    }

    public AgentFolderConfig      Config       { get; }
    public string                 OutputPath   { get; }
    public ChannelReader<string>  PendingFiles => _queue.Reader;

    public void Start()
    {
        if (!Directory.Exists(Config.Path))
            throw new DirectoryNotFoundException(L.T($"Теку не знайдено: {Config.Path}", $"Folder not found: {Config.Path}", $"Папка не найдена: {Config.Path}"));

        _watcher = new FileSystemWatcher(Config.Path)
        {
            NotifyFilter          = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            IncludeSubdirectories = false,
            InternalBufferSize    = 64 * 1024
        };

        _watcher.Created += (_, e) => OnFileEvent(e.FullPath);
        _watcher.Changed += (_, e) => OnFileEvent(e.FullPath);
        _watcher.Renamed += (_, e) => { _queued.TryRemove(e.OldFullPath, out var _); OnFileEvent(e.FullPath); };
        _watcher.Deleted += (_, e) => _queued.TryRemove(e.FullPath, out var _);   // a re-added copy is new work
        _watcher.Error   += (_, e) => _logger.LogWarning(e.GetException(), "Folder watcher error in {Path}", Config.Path);
        _watcher.EnableRaisingEvents = true;

        _timer = new Timer(_ => Tick(), null, PollInterval, PollInterval);
    }

    // Queues files already in the folder. force = all of them; otherwise only those without an
    // up-to-date result and (if given) changed after `since`. Returns how many were queued.
    public int EnqueueExisting(DateTime? since, bool force)
    {
        var count = 0;
        foreach (var file in new DirectoryInfo(Config.Path).EnumerateFiles())
        {
            if (ShouldSkip(file.FullName)) continue;
            if ((file.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;

            if (!force)
            {
                if (since is { } s && file.LastWriteTime < s) continue;
                var result = new FileInfo(Path.Combine(OutputPath, file.Name + ResultSuffix));
                if (result.Exists && result.LastWriteTime >= file.LastWriteTime) continue;
            }

            _queued[file.FullName] = (file.LastWriteTimeUtc, file.Length);
            _queue.Writer.TryWrite(file.FullName);
            count++;
        }
        return count;
    }

    // Marks a file the agent is about to write so its events are ignored.
    public void Ignore(string fullPath) => _ignored[Path.GetFullPath(fullPath)] = 0;

    private void OnFileEvent(string path)
    {
        if (ShouldSkip(path)) return;
        var now = DateTime.UtcNow;
        _pending.AddOrUpdate(path, (now, now), (_, v) => (v.First, now));
    }

    private bool ShouldSkip(string path)
    {
        var name = Path.GetFileName(path);
        if (name.StartsWith("~$") || name.StartsWith('.')) return true;
        if (TempExtensions.Contains(Path.GetExtension(name))) return true;
        if (name.EndsWith(ResultSuffix, StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith(ErrorSuffix, StringComparison.OrdinalIgnoreCase)) return true;
        if (_ignored.ContainsKey(path)) return true;
        if (Directory.Exists(path)) return true;
        return !IsAllowedExtension(path);
    }

    private bool IsAllowedExtension(string path)
    {
        var allowed = Config.FileExtensionsAllowed;
        if (allowed.Count == 0 || allowed.Any(e => e is "*.*" or "*")) return true;
        var ext = Path.GetExtension(path);
        return allowed.Any(a => string.Equals(a.TrimStart('*'), ext, StringComparison.OrdinalIgnoreCase));
    }

    private void Tick()
    {
        if (Interlocked.Exchange(ref _ticking, 1) == 1) return;
        try
        {
            var now = DateTime.UtcNow;
            foreach (var (path, times) in _pending)
            {
                if (now - times.Last < QuietPeriod) continue;

                if (!File.Exists(path))
                {
                    _pending.TryRemove(path, out _);
                    continue;
                }

                if (!CanOpenExclusively(path))
                {
                    if (now - times.First > MaxLockWait)
                    {
                        _pending.TryRemove(path, out _);
                        _logger.LogWarning("Skipped {File}: still locked after {Minutes} min", path, MaxLockWait.TotalMinutes);
                    }
                    continue;
                }

                _pending.TryRemove(path, out _);

                var info  = new FileInfo(path);
                var state = (info.LastWriteTimeUtc, info.Length);
                if (_queued.TryGetValue(path, out var prev) && prev == state) continue;

                _queued[path] = state;
                _queue.Writer.TryWrite(path);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Folder observer tick failed for {Path}", Config.Path);
        }
        finally
        {
            Interlocked.Exchange(ref _ticking, 0);
        }
    }

    private static bool CanOpenExclusively(string path)
    {
        try
        {
            using var _ = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
            return true;
        }
        catch (IOException)                 { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
        _watcher?.Dispose();
        _watcher = null;
        _queue.Writer.TryComplete();
    }

    public void Dispose() => Stop();
}
