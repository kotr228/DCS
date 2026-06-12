using System.Text.Json;
using AsmodayCat.Shared.Interfaces;
using AsmodayCat.Shared.Models;

namespace AsmodayCat.Network.CoffeeCatBridge;

public class NetworkTaskDistributor : IDistributedRouter
{
    private readonly IDocControlNetworkCore _network;
    private readonly ILLMEngine _localEngine;

    // Поріг VRAM (MB) після якого задача вважається "великою" і відправляється в мережу
    private const int DistributeThresholdMb = 4096;

    public NetworkTaskDistributor(IDocControlNetworkCore network, ILLMEngine localEngine)
    {
        _network = network;
        _localEngine = localEngine;
    }

    public async Task<LlmResponse> RouteTaskAsync(LlmRequest request, CancellationToken cancellationToken = default)
    {
        if (request.RequiredVram < DistributeThresholdMb)
            return await _localEngine.GenerateAsync(request, cancellationToken);

        var freeNode = await FindFreeNodeAsync(cancellationToken);
        if (freeNode is null)
            return await _localEngine.GenerateAsync(request, cancellationToken);

        return await DispatchToNodeAsync(freeNode, request, cancellationToken);
    }

    private async Task<string?> FindFreeNodeAsync(CancellationToken cancellationToken)
    {
        var nodes = await _network.GetNetworkNodesStatusAsync(cancellationToken);
        // Вибираємо вузол з найбільшим вільним VRAM
        return nodes
            .OrderByDescending(n => n.VramFree)
            .FirstOrDefault()
            ?.ToString();
    }

    private async Task<LlmResponse> DispatchToNodeAsync(
        string nodeId, LlmRequest request, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(request);
        var responseBytes = await _network.SendTaskAsync(nodeId, payload, cancellationToken);
        return JsonSerializer.Deserialize<LlmResponse>(responseBytes)
               ?? new LlmResponse { Content = string.Empty };
    }
}
