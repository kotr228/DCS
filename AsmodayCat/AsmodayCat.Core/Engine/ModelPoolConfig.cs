using AsmodayCat.Shared.Enums;
using AsmodayCat.Shared.Models;

namespace AsmodayCat.Core.Engine;

// Вбудована матриця моделей згідно з вимогами
public static class ModelPoolConfig
{
    public static IReadOnlyList<ModelConfig> DefaultPool { get; } = new List<ModelConfig>
    {
        new()
        {
            ModelId       = "mistral:7b",
            TaskType      = ModelTaskType.General,
            RequiredVramMb = 5000,
            Description   = "Main orchestration agent with function calling support"
        },
        new()
        {
            ModelId       = "qwen2.5-coder:7b",
            TaskType      = ModelTaskType.Coding,
            RequiredVramMb = 5000,
            Description   = "Specialized coding model for C#/.NET refactoring and analysis"
        },
        new()
        {
            ModelId       = "phi3:3.8b",
            TaskType      = ModelTaskType.FastAnalytics,
            RequiredVramMb = 2500,
            Description   = "Lightweight model for fast file/log parsing in sandboxes"
        },
        new()
        {
            ModelId       = "llava:7b",
            TaskType      = ModelTaskType.Vision,
            RequiredVramMb = 5500,
            Description   = "Multimodal model for screenshot OCR and diagram analysis"
        },
        new()
        {
            ModelId       = "stable-diffusion",
            TaskType      = ModelTaskType.ImageGeneration,
            RequiredVramMb = 4000,
            Description   = "Image generation via ComfyUI/WebUI API bridge"
        }
    };

    public static ModelConfig? GetForTask(ModelTaskType task) =>
        DefaultPool.FirstOrDefault(m => m.TaskType == task);
}
