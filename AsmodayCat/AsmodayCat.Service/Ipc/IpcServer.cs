using System.IO.Pipes;
using System.Text.Json;
using AsmodayCat.Core.Hardware;
using AsmodayCat.Shared.Interfaces;
using AsmodayCat.Shared.Models;

namespace AsmodayCat.Service.Ipc;

public class IpcServer : BackgroundService
{
    public const string PipeName = "AsmodayCat.Service";

    private readonly IAgentController _agent;
    private readonly IHardwareScanner _hardware;
    private readonly ResourceManager _resources;
    private readonly ILogger<IpcServer> _logger;

    public IpcServer(
        IAgentController agent,
        IHardwareScanner hardware,
        ResourceManager resources,
        ILogger<IpcServer> logger)
    {
        _agent = agent;
        _hardware = hardware;
        _resources = resources;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(
                    PipeName, PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await pipe.WaitForConnectionAsync(stoppingToken);
                _ = HandleClientAsync(pipe, stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "IPC server error");
            }
        }
    }

    private async Task HandleClientAsync(NamedPipeServerStream pipe, CancellationToken cancellationToken)
    {
        try
        {
            using var reader = new StreamReader(pipe, leaveOpen: true);
            await using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };

            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null) return;

            var command = JsonSerializer.Deserialize<IpcCommand>(line);
            if (command is null) return;

            var response = await DispatchAsync(command, cancellationToken);
            await writer.WriteLineAsync(JsonSerializer.Serialize(response));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "IPC client handler error");
        }
    }

    private async Task<IpcResponse> DispatchAsync(IpcCommand cmd, CancellationToken cancellationToken)
    {
        try
        {
            return cmd.Type switch
            {
                IpcCommandType.GetStatus => new IpcResponse
                {
                    Success = true,
                    Payload = new { Status = "Running" }
                },

                IpcCommandType.GetSystemLoad => new IpcResponse
                {
                    Success = true,
                    Payload = _resources.GetCurrentStatus()
                },

                IpcCommandType.StartAgent => await StartAgentAsync(cmd, cancellationToken),

                IpcCommandType.StopAgent => await StopAgentAsync(cmd, cancellationToken),

                _ => new IpcResponse { Success = false, Error = $"Unknown command: {cmd.Type}" }
            };
        }
        catch (Exception ex)
        {
            return new IpcResponse { Success = false, Error = ex.Message };
        }
    }

    private async Task<IpcResponse> StartAgentAsync(IpcCommand cmd, CancellationToken cancellationToken)
    {
        var path = cmd.Parameters.GetValueOrDefault("Path", string.Empty);
        var prompt = cmd.Parameters.GetValueOrDefault("SystemPrompt", string.Empty);

        var config = new AgentFolderConfig { Path = path, SystemPrompt = prompt };
        await _agent.StartWatching(config, cancellationToken);
        return new IpcResponse { Success = true };
    }

    private async Task<IpcResponse> StopAgentAsync(IpcCommand cmd, CancellationToken cancellationToken)
    {
        var path = cmd.Parameters.GetValueOrDefault("Path", string.Empty);
        await _agent.StopWatching(path, cancellationToken);
        return new IpcResponse { Success = true };
    }
}
