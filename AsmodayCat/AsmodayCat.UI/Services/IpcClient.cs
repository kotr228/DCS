using System.IO;
using System.IO.Pipes;
using System.Text.Json;

namespace AsmodayCat.UI.Services;

public enum IpcCommandType
{
    GetStatus,
    StartAgent,
    StopAgent,
    ChangeHardwareModel,
    GetSystemLoad,
    KillSwitch,
    Chat
}

public class IpcCommand
{
    public IpcCommandType Type { get; set; }
    public Dictionary<string, string> Parameters { get; set; } = new();
}

public class IpcResponse
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public JsonElement? Payload { get; set; }
}

public class IpcClient
{
    private const string PipeName = "AsmodayCat.Service";

    public async Task<IpcResponse?> SendCommandAsync(
        IpcCommand command, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var pipe = new NamedPipeClientStream(
                ".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);

            await pipe.ConnectAsync(2000, cancellationToken);

            await using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
            using var reader = new StreamReader(pipe, leaveOpen: true);

            await writer.WriteLineAsync(JsonSerializer.Serialize(command));
            var responseJson = await reader.ReadLineAsync(cancellationToken);

            return responseJson is null
                ? null
                : JsonSerializer.Deserialize<IpcResponse>(responseJson);
        }
        catch
        {
            return null;
        }
    }
}
