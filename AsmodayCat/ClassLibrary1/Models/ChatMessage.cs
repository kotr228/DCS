namespace AsmodayCat.Shared.Models;

public class ChatMessage
{
    public string Role { get; set; } = string.Empty;     // "user" | "assistant" | "tool"
    public string Content { get; set; } = string.Empty;
    public string? ToolName { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.Now;
}
