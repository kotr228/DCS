namespace AsmodayCat.Service.Ipc;

// IpcCommandType and IpcCommand live in AsmodayCat.Shared.Ipc (shared with the UI).
public class IpcResponse
{
    public bool    Success { get; set; }
    public string? Error   { get; set; }
    public object? Payload { get; set; }
}
