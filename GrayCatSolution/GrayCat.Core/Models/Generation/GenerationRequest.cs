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
}