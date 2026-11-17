using AsmodayCat.Shared.Models;

namespace AsmodayCat.Shared.Interfaces;

public interface IModelRegistry
{
    Task<IReadOnlyList<string>> GetLocalModelsAsync(CancellationToken cancellationToken = default);
    Task PullModelAsync(string modelId, IProgress<ModelPullProgress> progress,
        CancellationToken cancellationToken = default);
    Task<bool> IsModelAvailableAsync(string modelId, CancellationToken cancellationToken = default);

    // Models currently loaded in memory (Ollama /api/ps)
    Task<IReadOnlyList<string>> GetLoadedModelsAsync(CancellationToken cancellationToken = default);

    // Loaded model → bytes it occupies in VRAM
    Task<IReadOnlyDictionary<string, long>> GetLoadedModelVramAsync(CancellationToken cancellationToken = default);

    Task DeleteModelAsync(string modelId, CancellationToken cancellationToken = default);
}
