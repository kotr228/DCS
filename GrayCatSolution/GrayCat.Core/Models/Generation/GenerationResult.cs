namespace GrayCat.Core.Models.Generation;

public class GenerationResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? ProjectPath { get; set; }
    public int FilesCreated { get; set; }
    public int DirectoriesCreated { get; set; }
    public long DurationMs { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}