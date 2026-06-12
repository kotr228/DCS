using AsmodayCat.Agent.Pipelines;
using AsmodayCat.Agent.Watchers;
using AsmodayCat.Agent.Workspace;
using AsmodayCat.Shared.Interfaces;
using AsmodayCat.Shared.Models;

namespace AsmodayCat.Agent;

public class AgentController : IAgentController
{
    private readonly ILLMEngine _llm;
    private readonly AccessManager _access;
    private readonly Dictionary<string, (FolderObserver Observer, CancellationTokenSource Cts)> _active = new();

    public AgentController(ILLMEngine llm, AccessManager access)
    {
        _llm = llm;
        _access = access;
    }

    public Task StartWatching(AgentFolderConfig config, CancellationToken cancellationToken = default)
    {
        if (_active.ContainsKey(config.Path))
            return Task.CompletedTask;

        if (!_access.CanAccess(config.Path))
            throw new UnauthorizedAccessException($"No write access to: {config.Path}");

        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var observer = new FolderObserver(config);
        observer.Start();

        var processor = new FilePipelineProcessor(_llm);
        _ = processor.RunAsync(observer, cts.Token);

        _active[config.Path] = (observer, cts);
        return Task.CompletedTask;
    }

    public Task StopWatching(string folderPath, CancellationToken cancellationToken = default)
    {
        if (!_active.TryGetValue(folderPath, out var entry))
            return Task.CompletedTask;

        entry.Cts.Cancel();
        entry.Observer.Dispose();
        _active.Remove(folderPath);
        return Task.CompletedTask;
    }
}
