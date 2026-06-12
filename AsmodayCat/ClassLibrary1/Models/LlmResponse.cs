using AsmodayCat.Shared.Enums;

namespace AsmodayCat.Shared.Models;

public class LlmResponse
{
    public string Content { get; set; } = string.Empty;
    public double TokensPerSecond { get; set; }
    public ExecutionDevice UsedDevice { get; set; }
}
