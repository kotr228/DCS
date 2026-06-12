namespace AsmodayCat.Shared.Models;

// Protocol DTO for IPC streaming — one line per token
public class StreamChunkDto
{
    public string  Chunk    { get; set; } = string.Empty;
    public bool    Done     { get; set; }
    public string? Error    { get; set; }
    public string? ToolUsed { get; set; }
}
