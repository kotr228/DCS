using AsmodayCat.Shared.Enums;

namespace AsmodayCat.Shared.Models;

public class AgentFolderConfig
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Path { get; set; } = string.Empty;
    public string OutputPath { get; set; } = string.Empty;
    public string SystemPrompt { get; set; } = string.Empty;
    public List<string> FileExtensionsAllowed { get; set; } = [];
    public AgentAction Action { get; set; } = AgentAction.CreateReport;
    public bool AllowInternetAccess { get; set; }

    // On start, also process files already in the folder that have no up-to-date result:
    // all of them (DateTime.MinValue) or only those changed after this moment. null = only new files.
    public DateTime? CatchUpSince { get; set; }
}
