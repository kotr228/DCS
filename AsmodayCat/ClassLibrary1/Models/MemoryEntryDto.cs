namespace AsmodayCat.Shared.Models;

// One remembered item, for the Memory tab
public class MemoryEntryDto
{
    public long     Id        { get; set; }
    public string   Kind      { get; set; } = string.Empty;   // fact | action | conversation
    public string   Text      { get; set; } = string.Empty;
    public string   Source    { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
