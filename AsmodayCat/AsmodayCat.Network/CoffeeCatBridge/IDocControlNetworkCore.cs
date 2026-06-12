using AsmodayCat.Shared.Models;

namespace AsmodayCat.Network.CoffeeCatBridge;

// Mock-інтерфейс для P2P-протоколу CoffeeCat (DocControlNetworkCore)
public interface IDocControlNetworkCore
{
    Task<IReadOnlyList<NodeResourceStatus>> GetNetworkNodesStatusAsync(CancellationToken cancellationToken = default);
    Task<byte[]> SendTaskAsync(string nodeId, byte[] payload, CancellationToken cancellationToken = default);
}
