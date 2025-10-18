using GrayCat.Shared.Models;

namespace GrayCat.Core.Models;

public class ProjectModel
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ProjectName { get; set; } = "Untitled";
    public string Author { get; set; } = "Unknown";
    public ProjectType Type { get; set; } = ProjectType.React;
    public List<BlockModel> Blocks { get; set; } = new();
    public Dictionary<string, string> Settings { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;
    public string Version { get; set; } = "1.0.0";
}