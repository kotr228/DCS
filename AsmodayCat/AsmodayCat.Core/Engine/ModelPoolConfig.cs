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
            ModelId       = "qwen2.5:7b",
            TaskType      = ModelTaskType.General,
            RequiredVramMb = 5500,
            Description   = "Main assistant: multilingual (incl. Ukrainian), reliable function calling"
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
            // Reads text in images far better than llava:7b and fits 8 GB of VRAM
            ModelId       = "qwen2.5vl:7b",
            TaskType      = ModelTaskType.Vision,
            RequiredVramMb = 6000,
            Description   = "Vision model: reads text in images, screenshots, documents and diagrams"
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
