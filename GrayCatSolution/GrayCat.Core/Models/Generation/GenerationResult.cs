namespace GrayCat.Core.Models.Generation;

public class GenerationResult
{
    public bool Success { get; set; }
    public string? RequestId { get; set; }

    /// <summary>The generator was not allowed to write to the folder (e.g. a service account without access).</summary>
    public bool AccessDenied { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? ProjectPath { get; set; }
    public int FilesCreated { get; set; }

    /// <summary>How many of the written files actually changed on disk.</summary>
    public int FilesChanged { get; set; }
    public int DirectoriesCreated { get; set; }
    public long DurationMs { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}