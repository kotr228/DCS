using AsmodayCat.Shared.Text;
using AsmodayCat.Shared.Enums;
using AsmodayCat.Shared.Interfaces;
using AsmodayCat.Shared.Models;

namespace AsmodayCat.Core.Engine;

// FR9.1: picks the model for a task. The model name travels with each request,
// so there is no shared "active model" state that can go stale after an unload.
public class ModelSwitcher
{
    private readonly ILLMEngine     _engine;
    private readonly IModelRegistry _registry;
    private readonly OllamaLauncher _launcher;

    // Text-model fallbacks, in order, when the preferred one isn't installed
    private static readonly ModelTaskType[] TextFallbacks =
        [ModelTaskType.General, ModelTaskType.Coding, ModelTaskType.FastAnalytics];

    public ModelSwitcher(ILLMEngine engine, IModelRegistry registry, OllamaLauncher launcher)
    {
        _engine   = engine;
        _registry = registry;
        _launcher = launcher;
    }

    public ModelTaskType? ActiveTaskType { get; private set; }

    // Known Ollama model families that accept images
    private static readonly string[] VisionFamilies =
        ["qwen2.5vl", "qwen2-vl", "llava", "bakllava", "llama3.2-vision", "minicpm-v", "moondream", "gemma3", "granite3.2-vision"];

    public static bool IsVisionModel(string name) =>
        VisionFamilies.Any(f => name.StartsWith(f, StringComparison.OrdinalIgnoreCase));

    // Returns the installed model to use for the task, or throws with a clear message.
    public async Task<string> ResolveModelAsync(ModelTaskType taskType, CancellationToken cancellationToken = default)
    {
        if (!await _launcher.EnsureRunningAsync(cancellationToken))
            throw new InvalidOperationException(_launcher.LastError ?? L.T("Ollama не запущена.", "Ollama is not running.", "Ollama не запущена."));

        var local = await _registry.GetLocalModelsAsync(cancellationToken);
        if (local.Count == 0)
            throw new InvalidOperationException(
                L.T("Ollama працює, але жодної моделі не встановлено. Відкрийте вкладку «Моделі» й завантажте, наприклад, qwen2.5:7b.",
                    "Ollama is running, but no model is installed. Open the Models tab and download one, e.g. qwen2.5:7b.",
                    "Ollama работает, но ни одна модель не установлена. Откройте вкладку «Модели» и скачайте, например, qwen2.5:7b."));

        string? Find(ModelTaskType t)
        {
            var cfg = ModelPoolConfig.GetForTask(t);
            if (cfg is null) return null;
            return local.FirstOrDefault(m => m.StartsWith(cfg.ModelId, StringComparison.OrdinalIgnoreCase));
        }

        var model = Find(taskType);

        // Images: any other installed model that can see
        if (model is null && taskType == ModelTaskType.Vision)
            model = local.FirstOrDefault(IsVisionModel);

        if (model is null && taskType != ModelTaskType.Vision && taskType != ModelTaskType.ImageGeneration)
        {
            model = TextFallbacks.Select(Find).FirstOrDefault(m => m is not null);

            // Last resort: any installed model that isn't a vision model
            model ??= local.FirstOrDefault(m => !IsVisionModel(m));
        }

        if (model is null)
        {
            var wanted = ModelPoolConfig.GetForTask(taskType)?.ModelId ?? taskType.ToString();
            throw new InvalidOperationException(L.T($"Модель «{wanted}» для цієї задачі не встановлена. Встановіть її на вкладці «Моделі».",
                $"Model “{wanted}” for this task is not installed. Install it on the Models tab.",
                $"Модель «{wanted}» для этой задачи не установлена. Установите её на вкладке «Модели»."));
        }

        ActiveTaskType = taskType;
        return model;
    }

    // Kept for existing callers: resolves the model and makes it the engine default.
    public async Task EnsureModelForTaskAsync(
        ModelTaskType taskType,
        IProgress<ModelPullProgress>? pullProgress = null,
        CancellationToken cancellationToken = default)
    {
        var model = await ResolveModelAsync(taskType, cancellationToken);
        await _engine.LoadModelAsync(model, cancellationToken);
    }
}
