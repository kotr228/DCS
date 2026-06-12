using AsmodayCat.Shared.Enums;
using AsmodayCat.Shared.Interfaces;
using AsmodayCat.Shared.Models;

namespace AsmodayCat.Core.Engine;

// FR9.1: контекстне перемикання моделей — вивантажує поточну, завантажує потрібну
public class ModelSwitcher
{
    private readonly ILLMEngine _engine;
    private readonly IModelRegistry _registry;
    private ModelTaskType? _activeTaskType;

    public ModelSwitcher(ILLMEngine engine, IModelRegistry registry)
    {
        _engine = engine;
        _registry = registry;
    }

    public async Task EnsureModelForTaskAsync(
        ModelTaskType taskType,
        IProgress<ModelPullProgress>? pullProgress = null,
        CancellationToken cancellationToken = default)
    {
        if (_activeTaskType == taskType) return;

        var config = ModelPoolConfig.GetForTask(taskType);
        if (config is null) return;

        // Вивантажуємо попередню модель
        if (_activeTaskType.HasValue)
            await _engine.UnloadAsync(cancellationToken);

        // FR8.1: якщо моделі немає локально — тягнемо автоматично
        var available = await _registry.IsModelAvailableAsync(config.ModelId, cancellationToken);
        if (!available && pullProgress != null)
            await _registry.PullModelAsync(config.ModelId, pullProgress, cancellationToken);

        await _engine.LoadModelAsync(config.ModelId, cancellationToken);
        _activeTaskType = taskType;
    }

    // FR9.2: cloud fallback — передає промпт зовнішньому API
    public ModelTaskType? ActiveTaskType => _activeTaskType;
}
