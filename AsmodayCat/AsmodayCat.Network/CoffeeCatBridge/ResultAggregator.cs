using AsmodayCat.Shared.Models;

namespace AsmodayCat.Network.CoffeeCatBridge;

// FR4.3: агрегує відповіді від розподілених нод у єдиний артефакт
public class ResultAggregator
{
    public LlmResponse Aggregate(IReadOnlyList<LlmResponse> nodeResults)
    {
        if (nodeResults.Count == 0)
            return new LlmResponse { Content = string.Empty };

        if (nodeResults.Count == 1)
            return nodeResults[0];

        var combined = string.Join(
            "\n\n---\n\n",
            nodeResults
                .Where(r => !string.IsNullOrWhiteSpace(r.Content))
                .Select((r, i) => $"[Node {i + 1}]\n{r.Content}"));

        var avgTps = nodeResults.Average(r => r.TokensPerSecond);

        return new LlmResponse
        {
            Content = combined,
            TokensPerSecond = avgTps,
            UsedDevice = nodeResults[0].UsedDevice
        };
    }

    // Розбиває список файлів на батчі для розподілу між нодами (FR4.2)
    public static IReadOnlyList<List<T>> SplitIntoBatches<T>(IList<T> items, int batchSize)
    {
        var batches = new List<List<T>>();
        for (var i = 0; i < items.Count; i += batchSize)
            batches.Add(items.Skip(i).Take(batchSize).ToList());
        return batches;
    }
}
