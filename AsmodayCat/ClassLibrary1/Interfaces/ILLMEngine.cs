using AsmodayCat.Shared.Models;

namespace AsmodayCat.Shared.Interfaces;

public interface ILLMEngine
{
    Task LoadModelAsync(string modelPath, CancellationToken cancellationToken = default);
    Task<LlmResponse> GenerateAsync(LlmRequest request, CancellationToken cancellationToken = default);
    Task UnloadAsync(CancellationToken cancellationToken = default);
}
