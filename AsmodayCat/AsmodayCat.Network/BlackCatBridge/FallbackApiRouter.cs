using AsmodayCat.Shared.Interfaces;
using AsmodayCat.Shared.Models;

namespace AsmodayCat.Network.BlackCatBridge;

// Якщо локальна LLM падає — формуємо запит до зовнішнього fallback API через фільтр BlackCat
public class FallbackApiRouter
{
    private readonly ILLMEngine _localEngine;
    private readonly IBlackCatSecurityFilter _securityFilter;
    private readonly IFallbackLlmClient _fallbackClient;

    public FallbackApiRouter(
        ILLMEngine localEngine,
        IBlackCatSecurityFilter securityFilter,
        IFallbackLlmClient fallbackClient)
    {
        _localEngine = localEngine;
        _securityFilter = securityFilter;
        _fallbackClient = fallbackClient;
    }

    public async Task<LlmResponse> GenerateWithFallbackAsync(
        LlmRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _localEngine.GenerateAsync(request, cancellationToken);
        }
        catch (Exception)
        {
            var allowed = await _securityFilter.IsInternetAccessAllowedAsync(
                "llm-fallback", cancellationToken);

            if (!allowed)
                throw new InvalidOperationException(
                    "Local LLM failed and internet access is blocked by BlackCat policy.");

            return await _fallbackClient.GenerateAsync(request, cancellationToken);
        }
    }
}
