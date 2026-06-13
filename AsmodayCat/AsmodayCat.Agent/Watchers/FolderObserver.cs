using System.Threading.Channels;
using AsmodayCat.Shared.Models;

namespace AsmodayCat.Agent.Watchers;

public sealed class FolderObserver : IDisposable
{
    private FileSystemWatcher? _watcher;
    private readonly Channel<string> _queue = Channel.CreateUnbounded<string>(
        new UnboundedChannelOptions { SingleReader = true });

    public ChannelReader<string> PendingFiles => _queue.Reader;

    public AgentFolderConfig Config { get; }

    public FolderObserver(AgentFolderConfig config)
    {
        Config = config;
    }

    public void Start()
    {
        if (!Directory.Exists(Config.Path))
            throw new DirectoryNotFoundException($"Folder not found: {Config.Path}");

        _watcher = new FileSystemWatcher(Config.Path)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
            EnableRaisingEvents = true,
            IncludeSubdirectories = false
        };

        _watcher.Created += OnFileEvent;
        _watcher.Changed += OnFileEvent;
    }

    public void Stop()
    {
        _watcher?.Dispose();
        _watcher = null;
        _queue.Writer.TryComplete();
    }

    private void OnFileEvent(object sender, FileSystemEventArgs e)
    {
        if (!IsAllowed(e.FullPath)) return;
        _queue.Writer.TryWrite(e.FullPath);
    }

    private bool IsAllowed(string path)
    {
        if (Config.FileExtensionsAllowed.Count == 0) return true;
        if (Config.FileExtensionsAllowed.Any(e => e is "*.*" or "*")) return true;
        var ext = Path.GetExtension(path);
        return Config.FileExtensionsAllowed.Contains(ext, StringComparer.OrdinalIgnoreCase);
    }

    public void Dispose() => Stop();
}
