using AsmodayCat.Shared.Models;

namespace AsmodayCat.Shared.Interfaces;

public interface IModelRegistry
{
    Task<IReadOnlyList<string>> GetLocalModelsAsync(CancellationToken cancellationToken = default);
    Task PullModelAsync(string modelId, IProgress<ModelPullProgress> progress,
        CancellationToken cancellationToken = default);
    Task<bool> IsModelAvailableAsync(string modelId, CancellationToken cancellationToken = default);
}
