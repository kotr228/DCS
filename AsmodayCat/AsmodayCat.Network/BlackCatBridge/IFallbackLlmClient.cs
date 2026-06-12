using AsmodayCat.Shared.Models;

namespace AsmodayCat.Network.BlackCatBridge;

// Абстракція зовнішнього API (OpenAI / Claude як fallback)
public interface IFallbackLlmClient
{
    Task<LlmResponse> GenerateAsync(LlmRequest request, CancellationToken cancellationToken = default);
}
