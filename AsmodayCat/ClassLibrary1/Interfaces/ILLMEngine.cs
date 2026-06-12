using AsmodayCat.Shared.Models;

namespace AsmodayCat.Shared.Interfaces;

public interface ILLMEngine
{
    Task LoadModelAsync(string modelPath, CancellationToken cancellationToken = default);
    Task<LlmResponse> GenerateAsync(LlmRequest request, CancellationToken cancellationToken = default);
    Task UnloadAsync(CancellationToken cancellationToken = default);
    IAsyncEnumerable<string> GenerateStreamAsync(LlmRequest request, CancellationToken cancellationToken = default);

    // Default no-op; override in engines that maintain conversation context
    void ClearContext() { }
}
