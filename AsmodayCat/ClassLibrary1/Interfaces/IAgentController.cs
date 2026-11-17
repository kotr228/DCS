using AsmodayCat.Shared.Models;

namespace AsmodayCat.Shared.Interfaces;

public interface IAgentController
{
    Task StartWatching(AgentFolderConfig config, CancellationToken cancellationToken = default);
    Task StopWatching(Guid ruleId, CancellationToken cancellationToken = default);
    Task StopWatchingPath(string folderPath, CancellationToken cancellationToken = default);
    bool IsWatching(Guid ruleId);

    // Queue every file in the rule's folder again, even if it already has a result; returns how many
    int Reprocess(Guid ruleId);
    Task CancelAllAsync();
}
