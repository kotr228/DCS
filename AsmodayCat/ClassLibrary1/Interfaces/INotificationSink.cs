namespace AsmodayCat.Shared.Interfaces;

// Short messages for the user (shown as tray notifications), e.g. "agent processed a file".
public interface INotificationSink
{
    void Notify(string title, string text, string? openPath = null, bool isError = false);
}
