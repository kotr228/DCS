using AsmodayCat.Shared.Enums;

namespace AsmodayCat.Shared.Models;

public class AgentFolderConfig
{
    public string Path { get; set; } = string.Empty;
    public string OutputPath { get; set; } = string.Empty;
    public string SystemPrompt { get; set; } = string.Empty;
    public List<string> FileExtensionsAllowed { get; set; } = [];
    public AgentAction Action { get; set; } = AgentAction.CreateReport;
}
