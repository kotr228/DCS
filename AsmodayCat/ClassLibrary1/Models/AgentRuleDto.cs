namespace AsmodayCat.Shared.Models;

public class AgentRuleDto
{
    public Guid   Id                  { get; set; } = Guid.NewGuid();
    public string InputPath           { get; set; } = string.Empty;
    public string OutputPath          { get; set; } = string.Empty;
    public string ActionType          { get; set; } = "CreateReport";
    public string SystemPrompt        { get; set; } = string.Empty;
    public string AllowedExtensions   { get; set; } = "*.*";
    public bool   AllowInternetAccess { get; set; }
    public bool   IsActive            { get; set; }
}
