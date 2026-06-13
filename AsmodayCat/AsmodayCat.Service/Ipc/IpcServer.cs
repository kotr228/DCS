using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using AsmodayCat.Core.Engine;
using AsmodayCat.Core.Hardware;
using AsmodayCat.Shared.Enums;
using AsmodayCat.Shared.Interfaces;
using AsmodayCat.Shared.Models;

namespace AsmodayCat.Service.Ipc;

public class IpcServer : BackgroundService
{
    public const string PipeName = "AsmodayCat.Service";

    private readonly IAgentController   _agent;
    private readonly IHardwareScanner   _hardware;
    private readonly ResourceManager    _resources;
    private readonly MetricsCollector   _metrics;
    private readonly ILLMEngine         _llmEngine;
    private readonly ModelSwitcher      _modelSwitcher;
    private readonly IModelRegistry     _modelRegistry;
    private readonly PullStatusStore    _pullStatus;
    private readonly ILogger<IpcServer> _logger;

    public IpcServer(
        IAgentController   agent,
        IHardwareScanner   hardware,
        ResourceManager    resources,
        MetricsCollector   metrics,
        ILLMEngine         llmEngine,
        ModelSwitcher      modelSwitcher,
        IModelRegistry     modelRegistry,
        PullStatusStore    pullStatus,
        ILogger<IpcServer> logger)
    {
        _agent         = agent;
        _hardware      = hardware;
        _resources     = resources;
        _metrics       = metrics;
        _llmEngine     = llmEngine;
        _modelSwitcher = modelSwitcher;
        _modelRegistry = modelRegistry;
        _pullStatus    = pullStatus;
        _logger        = logger;
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

    private async Task HandleClientAsync(NamedPipeServerStream pipe, CancellationToken ct)
    {
        try
        {
            using var reader     = new StreamReader(pipe, leaveOpen: true);
            await using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };

            var line = await reader.ReadLineAsync(ct);
            if (line is null) return;

            var command = JsonSerializer.Deserialize<IpcCommand>(line);
            if (command is null) return;

            // Chat uses a multi-line streaming response
            if (command.Type == IpcCommandType.Chat)
            {
                await HandleChatStreamAsync(writer, command, ct);
                return;
            }

            var response = await DispatchAsync(command, ct);
            await writer.WriteLineAsync(JsonSerializer.Serialize(response));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "IPC client handler error");
        }
    }

    private async Task<IpcResponse> DispatchAsync(IpcCommand cmd, CancellationToken ct)
    {
        try
        {
            return cmd.Type switch
            {
                IpcCommandType.GetStatus         => new IpcResponse { Success = true, Payload = new { Status = "Running" } },
                IpcCommandType.GetSystemLoad     => new IpcResponse { Success = true, Payload = _resources.GetCurrentStatus() },
                IpcCommandType.StartAgent        => await StartAgentAsync(cmd, ct),
                IpcCommandType.StopAgent         => await StopAgentAsync(cmd, ct),
                IpcCommandType.KillSwitch        => await KillSwitchAsync(),
                IpcCommandType.ListModels        => await ListModelsAsync(ct),
                IpcCommandType.PullModel         => PullModelBackground(cmd),
                IpcCommandType.GetPullStatus     => GetPullStatus(cmd),
                IpcCommandType.GetDashboardStats => GetDashboardStats(),
                IpcCommandType.ClearContext      => ClearContextCmd(),
                IpcCommandType.GetModelPool      => await GetModelPoolAsync(ct),
                IpcCommandType.UnloadModel       => await UnloadModelAsync(ct),
                _ => new IpcResponse { Success = false, Error = $"Unknown command: {cmd.Type}" }
            };
        }
        catch (Exception ex)
        {
            return new IpcResponse { Success = false, Error = ex.Message };
        }
    }

    // ── Chat streaming (FR-CH1) ───────────────────────────────────────────────

    private async Task HandleChatStreamAsync(StreamWriter writer, IpcCommand cmd, CancellationToken ct)
    {
        var message     = cmd.Parameters.GetValueOrDefault("Message", string.Empty);
        var useVision   = cmd.Parameters.TryGetValue("UseVision", out var uv) && uv == "true";
        var attachments = cmd.Parameters.TryGetValue("Attachments", out var ap)
            ? ap.Split(';', StringSplitOptions.RemoveEmptyEntries).ToList()
            : new List<string>();

        try
        {
            // FR-CH7: route to vision model when images are present
            var taskType = (useVision && attachments.Count > 0)
                ? ModelTaskType.Vision
                : ModelTaskType.General;

            await _modelSwitcher.EnsureModelForTaskAsync(taskType, cancellationToken: ct);

            var request = new LlmRequest
            {
                Prompt     = message,
                ImagePaths = attachments
            };

            await foreach (var token in _llmEngine.GenerateStreamAsync(request, ct))
            {
                var chunk = new StreamChunkDto { Chunk = token, Done = false };
                await writer.WriteLineAsync(JsonSerializer.Serialize(chunk));
            }
        }
        catch (Exception ex)
        {
            var errChunk = new StreamChunkDto { Error = ex.Message, Done = true };
            await writer.WriteLineAsync(JsonSerializer.Serialize(errChunk));
            return;
        }

        await writer.WriteLineAsync(JsonSerializer.Serialize(new StreamChunkDto { Done = true }));
    }

    // ── Standard command handlers ─────────────────────────────────────────────

    private async Task<IpcResponse> StartAgentAsync(IpcCommand cmd, CancellationToken ct)
    {
        var path       = cmd.Parameters.GetValueOrDefault("Path", string.Empty);
        var outputPath = cmd.Parameters.GetValueOrDefault("OutputPath", string.Empty);
        var prompt     = cmd.Parameters.GetValueOrDefault("SystemPrompt", string.Empty);
        var actionStr  = cmd.Parameters.GetValueOrDefault("Action", nameof(AgentAction.CreateReport));

        Enum.TryParse<AgentAction>(actionStr, out var action);

        var config = new AgentFolderConfig
        {
            Path         = path,
            OutputPath   = outputPath,
            SystemPrompt = prompt,
            Action       = action
        };
        await _agent.StartWatching(config, ct);
        return new IpcResponse { Success = true };
    }

    private async Task<IpcResponse> StopAgentAsync(IpcCommand cmd, CancellationToken ct)
    {
        var path = cmd.Parameters.GetValueOrDefault("Path", string.Empty);
        await _agent.StopWatching(path, ct);
        return new IpcResponse { Success = true };
    }

    private async Task<IpcResponse> KillSwitchAsync()
    {
        _logger.LogWarning("Kill Switch activated — cancelling all tasks");
        await _agent.CancelAllAsync();
        return new IpcResponse { Success = true };
    }

    private async Task<IpcResponse> ListModelsAsync(CancellationToken ct)
    {
        var local    = await _modelRegistry.GetLocalModelsAsync(ct);
        var poolInfo = ModelPoolConfig.DefaultPool.Select(m => new
        {
            m.ModelId,
            m.TaskType,
            m.RequiredVramMb,
            m.Description,
            IsLocal = local.Any(l => l.StartsWith(m.ModelId, StringComparison.OrdinalIgnoreCase))
        });
        return new IpcResponse { Success = true, Payload = poolInfo };
    }

    private IpcResponse PullModelBackground(IpcCommand cmd)
    {
        var modelId = cmd.Parameters.GetValueOrDefault("ModelId", string.Empty);
        if (string.IsNullOrEmpty(modelId))
            return new IpcResponse { Success = false, Error = "ModelId required" };

        var progress = new Progress<ModelPullProgress>(_pullStatus.Update);
        _ = _modelRegistry.PullModelAsync(modelId, progress);

        return new IpcResponse { Success = true, Payload = new { modelId, status = "pulling" } };
    }

    private IpcResponse GetPullStatus(IpcCommand cmd)
    {
        var modelId = cmd.Parameters.GetValueOrDefault("ModelId", string.Empty);

        if (!string.IsNullOrEmpty(modelId))
            return new IpcResponse { Success = true, Payload = _pullStatus.Get(modelId) };

        return new IpcResponse { Success = true, Payload = _pullStatus.GetAll() };
    }

    private IpcResponse GetDashboardStats()
    {
        var dto = _metrics.GetSnapshot(availableNodes: 0);
        return new IpcResponse { Success = true, Payload = dto };
    }

    private IpcResponse ClearContextCmd()
    {
        _llmEngine.ClearContext();
        return new IpcResponse { Success = true };
    }

    // FR-M1/M2: merged local + pool matrix
    private async Task<IpcResponse> GetModelPoolAsync(CancellationToken ct)
    {
        var localModels = await _modelRegistry.GetLocalModelsAsync(ct);
        var localSet    = localModels.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var activeModel = _metrics.ActiveModelName;

        // Pool matrix — always present regardless of install state
        var pool = ModelPoolConfig.DefaultPool.Select(m =>
        {
            var isLocal  = localSet.Any(l => l.StartsWith(m.ModelId, StringComparison.OrdinalIgnoreCase));
            var isActive = !string.IsNullOrEmpty(activeModel) &&
                           activeModel.Equals(m.ModelId, StringComparison.OrdinalIgnoreCase);
            return new LlmModelDto
            {
                Name            = m.ModelId,
                RecommendedTask = m.TaskType.ToString(),
                VramUsageBytes  = isActive ? _resources.GetTotalVramUsedMb() * 1024L * 1024L : 0L,
                Status          = isActive ? ModelPoolStatus.Loaded
                                : isLocal  ? ModelPoolStatus.Ready
                                :            ModelPoolStatus.NotInstalled
            };
        }).ToList();

        // Extra custom models installed locally but not in the pool
        var poolIds = ModelPoolConfig.DefaultPool
            .Select(m => m.ModelId.ToLowerInvariant()).ToHashSet();
        foreach (var local in localSet)
        {
            if (!poolIds.Any(p => local.ToLowerInvariant().StartsWith(p)))
            {
                pool.Add(new LlmModelDto
                {
                    Name            = local,
                    RecommendedTask = "Custom",
                    Status          = ModelPoolStatus.Ready
                });
            }
        }

        return new IpcResponse { Success = true, Payload = pool };
    }

    // FR-M4: unload current model, free VRAM
    private async Task<IpcResponse> UnloadModelAsync(CancellationToken ct)
    {
        await _llmEngine.UnloadAsync(ct);
        _metrics.ActiveModelName   = string.Empty;
        _metrics.ActiveModelStatus = "Idle";
        return new IpcResponse { Success = true };
    }
}
