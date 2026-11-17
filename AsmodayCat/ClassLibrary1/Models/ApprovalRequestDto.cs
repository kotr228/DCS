namespace AsmodayCat.Shared.Models;

// A risky agent action waiting for the user's decision in the UI.
public class ApprovalRequestDto
{
    public Guid     Id        { get; set; } = Guid.NewGuid();
    public string   Source    { get; set; } = string.Empty;   // "Chat" or the watched folder
    public string   Title     { get; set; } = string.Empty;   // short: "Run shell command"
    public string   Details   { get; set; } = string.Empty;   // the command / path / content preview
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime ExpiresAt { get; set; }
}
