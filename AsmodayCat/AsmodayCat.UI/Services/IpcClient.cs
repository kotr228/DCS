using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading.Channels;
using AsmodayCat.Shared.Models;

namespace AsmodayCat.UI.Services;

public enum IpcCommandType
{
    GetStatus,
    StartAgent,
    StopAgent,
    ChangeHardwareModel,
    GetSystemLoad,
    KillSwitch,
    Chat,
    PullModel,
    GetPullStatus,
    ListModels,
    GetDashboardStats,
    ClearContext,
    GetModelPool,
    UnloadModel,
    GetHardwareConfig,
    SaveHardwareConfig
}

public class IpcCommand
{
    public IpcCommandType Type { get; set; }
    public Dictionary<string, string> Parameters { get; set; } = new();
}

public class IpcResponse
{
    public bool         Success { get; set; }
    public string?      Error   { get; set; }
    public JsonElement? Payload { get; set; }
}

public class IpcClient
{
    private const string PipeName = "AsmodayCat.Service";

    private static readonly JsonSerializerOptions _opts =
        new() { PropertyNameCaseInsensitive = true };

    public async Task<IpcResponse?> SendCommandAsync(
        IpcCommand command, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var pipe = new NamedPipeClientStream(
                ".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);

            await pipe.ConnectAsync(2000, cancellationToken);

            await using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
            using var reader       = new StreamReader(pipe, leaveOpen: true);

            await writer.WriteLineAsync(JsonSerializer.Serialize(command));
            var responseJson = await reader.ReadLineAsync(cancellationToken);

            return responseJson is null
                ? null
                : JsonSerializer.Deserialize<IpcResponse>(responseJson, _opts);
        }
        catch
        {
            return null;
        }
    }

    // Streaming variant: service sends multiple JSON lines until Done=true
    public IAsyncEnumerable<StreamChunkDto> StreamCommandAsync(
        IpcCommand command,
        CancellationToken cancellationToken = default)
    {
        var channel = Channel.CreateUnbounded<StreamChunkDto>(
            new UnboundedChannelOptions { SingleWriter = true, SingleReader = true });

        _ = FillChannelAsync(channel, command, cancellationToken);

        return channel.Reader.ReadAllAsync(cancellationToken);
    }

    private async Task FillChannelAsync(
        Channel<StreamChunkDto> channel,
        IpcCommand command,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var pipe = new NamedPipeClientStream(
                ".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);

            await pipe.ConnectAsync(2000, cancellationToken);

            await using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
            using var reader       = new StreamReader(pipe, leaveOpen: true);

            await writer.WriteLineAsync(JsonSerializer.Serialize(command));

            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(cancellationToken);
                if (line is null) break;

                var chunk = JsonSerializer.Deserialize<StreamChunkDto>(line, _opts);
                if (chunk is null) continue;

                await channel.Writer.WriteAsync(chunk, cancellationToken);
                if (chunk.Done) break;
            }
        }
        catch (Exception ex)
        {
            await channel.Writer.WriteAsync(
                new StreamChunkDto { Error = ex.Message, Done = true }, CancellationToken.None);
        }
        finally
        {
            channel.Writer.TryComplete();
        }
    }
}
