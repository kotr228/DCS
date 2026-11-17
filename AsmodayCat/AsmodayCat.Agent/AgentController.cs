using AsmodayCat.Shared.Text;
using System.Collections.Concurrent;
using AsmodayCat.Agent.Memory;
using AsmodayCat.Agent.Pipelines;
using AsmodayCat.Agent.Tools;
using AsmodayCat.Agent.Watchers;
using AsmodayCat.Agent.Workspace;
using AsmodayCat.Core.Engine;
using AsmodayCat.Shared.Interfaces;
using AsmodayCat.Shared.Models;
using Microsoft.Extensions.Logging;

namespace AsmodayCat.Agent;

public class AgentController : IAgentController
{
    public const string DefaultOutputFolderName = "AsmodayCat_Output";

    private readonly AgentRunner      _runner;
    private readonly ModelSwitcher    _models;
    private readonly AccessManager    _access;
    private readonly IApprovalService _approvals;
    private readonly IActivityLog     _activity;
    private readonly AssistantMemory  _memory;
    private readonly INotificationSink _notify;
    private readonly ILoggerFactory   _loggerFactory;
    private readonly ILogger          _logger;

    private readonly ConcurrentDictionary<Guid, (FolderObserver Observer, CancellationTokenSource Cts)> _active = new();
    private readonly object _startLock = new();

    public AgentController(
        AgentRunner runner, ModelSwitcher models, AccessManager access,
        IApprovalService approvals, IActivityLog activity, AssistantMemory memory, INotificationSink notify,
        ILoggerFactory loggerFactory)
    {
        _notify        = notify;
        _memory        = memory;
        _runner        = runner;
        _models        = models;
        _access        = access;
        _approvals     = approvals;
        _activity      = activity;
        _loggerFactory = loggerFactory;
        _logger        = loggerFactory.CreateLogger<AgentController>();
    }

    public static string ResolveOutputPath(AgentFolderConfig config) =>
        string.IsNullOrWhiteSpace(config.OutputPath)
            ? Path.Combine(config.Path, DefaultOutputFolderName)
            : Path.GetFullPath(config.OutputPath);

    public Task StartWatching(AgentFolderConfig config, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(config.Path))
            throw new ArgumentException(L.T("Вкажіть теку, за якою стежити.", "Choose a folder to watch.", "Укажите папку для наблюдения."));

        config.Path = Path.GetFullPath(config.Path);
        var outputPath = ResolveOutputPath(config);

        lock (_startLock)
        {
            if (_active.ContainsKey(config.Id))
                return Task.CompletedTask;

            if (_active.Values.Any(a => string.Equals(a.Observer.Config.Path, config.Path, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException(L.T($"За цією текою вже стежить інше правило: {config.Path}", $"Another rule already watches this folder: {config.Path}", $"За этой папкой уже следит другое правило: {config.Path}"));

            if (!Directory.Exists(config.Path))
                throw new DirectoryNotFoundException(L.T($"Теку не знайдено: {config.Path}", $"Folder not found: {config.Path}", $"Папка не найдена: {config.Path}"));

            if (!_access.CanAccess(config.Path))
                throw new UnauthorizedAccessException(L.T($"Немає прав на запис у теку: {config.Path}", $"No permission to write to the folder: {config.Path}", $"Нет прав на запись в папку: {config.Path}"));

            Directory.CreateDirectory(outputPath);

            // Not linked to the caller's token: an IPC request ending must not stop the watcher
            var cts      = new CancellationTokenSource();
            var logger   = _loggerFactory.CreateLogger("AsmodayCat.Agent." + Path.GetFileName(config.Path.TrimEnd('\\')));
            var observer = new FolderObserver(config, outputPath, logger);
            observer.Start();

            var processor = new FilePipelineProcessor(_runner, _models, _approvals, _activity, _memory, _notify, logger);
            _ = processor.RunAsync(observer, cts.Token);

            _active[config.Id] = (observer, cts);

            if (config.CatchUpSince is { } since)
            {
                var queued = observer.EnqueueExisting(since == DateTime.MinValue ? null : since, force: false);
                if (queued > 0) _logger.LogInformation("Queued {Count} existing file(s) in {Path}", queued, config.Path);
            }
        }

        _logger.LogInformation("Watching {Path} → {Output}", config.Path, outputPath);
        return Task.CompletedTask;
    }

    public Task StopWatching(Guid ruleId, CancellationToken cancellationToken = default)
    {
        if (_active.TryRemove(ruleId, out var entry))
        {
            entry.Cts.Cancel();
            entry.Observer.Dispose();
            _logger.LogInformation("Stopped watching {Path}", entry.Observer.Config.Path);
        }
        return Task.CompletedTask;
    }

    public async Task StopWatchingPath(string folderPath, CancellationToken cancellationToken = default)
    {
        var full = Path.GetFullPath(folderPath);
        foreach (var (id, entry) in _active)
        {
            if (string.Equals(entry.Observer.Config.Path, full, StringComparison.OrdinalIgnoreCase))
                await StopWatching(id, cancellationToken);
        }
    }

    public bool IsWatching(Guid ruleId) => _active.ContainsKey(ruleId);

    public int Reprocess(Guid ruleId)
    {
        if (!_active.TryGetValue(ruleId, out var entry))
            throw new InvalidOperationException(L.T("Агент цієї теки на паузі — спершу увімкніть його.", "This folder's agent is paused — turn it on first.", "Агент этой папки на паузе — сначала включите его."));
        var queued = entry.Observer.EnqueueExisting(null, force: true);
        _logger.LogInformation("Reprocessing {Count} file(s) in {Path}", queued, entry.Observer.Config.Path);
        return queued;
    }

    // FR6.3: Kill Switch — stops all watchers and cancels files being processed
    public async Task CancelAllAsync()
    {
        foreach (var id in _active.Keys.ToList())
            await StopWatching(id);
    }
}
