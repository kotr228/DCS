using AsmodayCat.Shared.Models;

namespace AsmodayCat.Shared.Interfaces;

public interface IAgentController
{
    Task StartWatching(AgentFolderConfig config, CancellationToken cancellationToken = default);
    Task StopWatching(string folderPath, CancellationToken cancellationToken = default);
    Task CancelAllAsync();
}
