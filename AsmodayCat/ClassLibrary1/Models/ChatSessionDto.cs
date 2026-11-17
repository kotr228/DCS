namespace AsmodayCat.Shared.Models;

// One saved conversation, for the history list in the chat
public class ChatSessionDto
{
    public Guid     Id        { get; set; }
    public string   Title     { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; }
    public bool     IsCurrent { get; set; }

    public string Display => $"{UpdatedAt:dd.MM HH:mm}  ·  {Title}";

    // Used by the combo box text and screen readers
    public override string ToString() => Display;
}

public class ChatHistoryDto
{
    public Guid?                SessionId { get; set; }
    public List<ChatMessageDto> Messages  { get; set; } = [];
}
