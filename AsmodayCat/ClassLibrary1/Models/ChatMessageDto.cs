using System.ComponentModel;

namespace AsmodayCat.Shared.Models;

public enum ChatRole { User, Assistant, System, Tool }

public class ChatMessageDto : INotifyPropertyChanged
{
    public string   Id              { get; set; } = Guid.NewGuid().ToString();
    public ChatRole Role            { get; set; }
    public DateTime Timestamp       { get; set; } = DateTime.Now;
    public List<string> AttachmentPaths { get; set; } = [];

    private string _content = string.Empty;
    public string Content
    {
        get => _content;
        set
        {
            if (_content == value) return;
            _content = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Content)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
