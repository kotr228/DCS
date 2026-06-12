using AsmodayCat.Shared.Enums;

namespace AsmodayCat.Shared.Models;

public class LlmRequest
{
    public string Prompt { get; set; } = string.Empty;
    public string Context { get; set; } = string.Empty;
    public int RequiredVram { get; set; }
    public ExecutionDevice ExecutionDevice { get; set; } = ExecutionDevice.CPU;
}
