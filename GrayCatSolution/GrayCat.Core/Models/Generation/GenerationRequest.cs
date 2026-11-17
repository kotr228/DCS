using GrayCat.Shared.Models;

namespace GrayCat.Core.Models.Generation;

public class GenerationRequest
{
    public string ProjectName { get; set; } = "NewProject";
    public string OutputPath { get; set; } = string.Empty;
    public ProjectType Type { get; set; }
    public string Author { get; set; } = "Default User";
    public ProjectModel? ProjectData { get; set; }
    public bool IncludeBackend { get; set; } = false;
    public bool UseSQLite { get; set; } = false;

    /// <summary>Regenerate code inside an existing GrayCat project folder instead of creating a new one.</summary>
    public bool Overwrite { get; set; } = false;

    /// <summary>Folder whose "assets" subfolder holds the project's images (the unpacked .graycat). Null = the output folder.</summary>
    public string? AssetsDirectory { get; set; }
}