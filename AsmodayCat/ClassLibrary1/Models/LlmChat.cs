using System.Text.Json;
using AsmodayCat.Shared.Enums;

namespace AsmodayCat.Shared.Models;

// One message of a multi-turn conversation (Ollama /api/chat format).
public class LlmChatMessage
{
    public string  Role    { get; set; } = "user";   // system | user | assistant | tool
    public string  Content { get; set; } = string.Empty;

    // Base64-encoded images (vision models only)
    public List<string>? Images { get; set; }

    // Set on assistant messages that request tool calls
    public List<LlmToolCall>? ToolCalls { get; set; }

    // Set on tool result messages
    public string? ToolName { get; set; }

    public static LlmChatMessage System(string text) => new() { Role = "system", Content = text };
    public static LlmChatMessage User(string text)   => new() { Role = "user",   Content = text };
}

public class LlmToolCall
{
    public string      Name      { get; set; } = string.Empty;
    public JsonElement Arguments { get; set; }
}

public class LlmToolDefinition
{
    public string Name        { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    // JSON schema object describing the arguments
    public object Parameters  { get; set; } = new { type = "object", properties = new { } };
}

public class LlmChatRequest
{
    public string                   Model           { get; set; } = string.Empty;
    public List<LlmChatMessage>     Messages        { get; set; } = [];
    public List<LlmToolDefinition>? Tools           { get; set; }
    public ExecutionDevice          ExecutionDevice { get; set; } = ExecutionDevice.CPU;

    // Estimated tokens this request needs; the engine grows its context window to fit
    public int?                     ContextTokens   { get; set; }
}

// One streamed piece of a chat response.
public class LlmChatChunk
{
    public string             Content   { get; set; } = string.Empty;
    public List<LlmToolCall>? ToolCalls { get; set; }
    public bool               Done      { get; set; }
    public double             TokensPerSecond { get; set; }
}

// Thrown when the selected model cannot use tools; callers retry without tools.
public class ToolsNotSupportedException : Exception
{
    public ToolsNotSupportedException(string model)
        : base($"Model '{model}' does not support tools.") { }
}
