namespace AsmodayCat.Shared.Models;

public class AgentActivityLogDto
{
    public DateTime Timestamp         { get; set; } = DateTime.Now;
    public string   ActionDescription { get; set; } = string.Empty;
    public string   Status            { get; set; } = string.Empty;  // "Processing", "Done", "Error"
}
