using AsmodayCat.Shared.Models;

namespace AsmodayCat.Service.Ipc;

// FR8.2: зберігає поточний прогрес скачування моделей для polling'у з UI
public class PullStatusStore
{
    private readonly Dictionary<string, ModelPullProgress> _progress = new();
    private readonly object _lock = new();

    public void Update(ModelPullProgress progress)
    {
        lock (_lock)
            _progress[progress.ModelId] = progress;
    }

    public ModelPullProgress? Get(string modelId)
    {
        lock (_lock)
            return _progress.TryGetValue(modelId, out var p) ? p : null;
    }

    public IReadOnlyDictionary<string, ModelPullProgress> GetAll()
    {
        lock (_lock)
            return new Dictionary<string, ModelPullProgress>(_progress);
    }
}
