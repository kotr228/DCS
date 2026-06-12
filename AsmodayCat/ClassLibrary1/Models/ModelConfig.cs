using AsmodayCat.Shared.Enums;

namespace AsmodayCat.Shared.Models;

public class ModelConfig
{
    public string ModelId { get; set; } = string.Empty;
    public ModelTaskType TaskType { get; set; }
    public int RequiredVramMb { get; set; }
    public string Description { get; set; } = string.Empty;
}
