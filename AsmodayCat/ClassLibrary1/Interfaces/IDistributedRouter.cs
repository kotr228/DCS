using AsmodayCat.Shared.Models;

namespace AsmodayCat.Shared.Interfaces;

public interface IDistributedRouter
{
    Task<LlmResponse> RouteTaskAsync(LlmRequest request, CancellationToken cancellationToken = default);
}
