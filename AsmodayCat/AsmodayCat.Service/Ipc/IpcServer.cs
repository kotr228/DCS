using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using AsmodayCat.Core.Engine;
using AsmodayCat.Core.Hardware;
using AsmodayCat.Shared.Enums;
using AsmodayCat.Shared.Interfaces;
using AsmodayCat.Shared.Models;
using AsmodayCat.Service;

namespace AsmodayCat.Service.Ipc;

public class IpcServer : BackgroundService
{
    public const string PipeName = "AsmodayCat.Service";

    private readonly IAgentController    _agent;
    private readonly IHardwareScanner    _hardware;
    private readonly ResourceManager     _resources;
    private readonly MetricsCollector    _metrics;
    private readonly ILLMEngine          _llmEngine;
    private readonly ModelSwitcher       _modelSwitcher;
    private readonly IModelRegistry      _modelRegistry;
    private readonly PullStatusStore     _pullStatus;
    private readonly HardwareConfigStore _hwConfigStore;
    private readonly AgentRuleStore      _ruleStore;
    private readonly TerminalLogStore    _logStore;
    private readonly ILogger<IpcServer>  _logger;

    private static readonly JsonSerializerOptions _jsonOpts =
        new() { PropertyNameCaseInsensitive = true };

    public IpcServer(
        IAgentController    agent,
        IHardwareScanner    hardware,
        ResourceManager     resources,
        MetricsCollector    metrics,
        ILLMEngine          llmEngine,
        ModelSwitcher       modelSwitcher,
        IModelRegistry      modelRegistry,
        PullStatusStore     pullStatus,
        HardwareConfigStore hwConfigStore,
        AgentRuleStore      ruleStore,
        TerminalLogStore    logStore,
        ILogger<IpcServer>  logger)
    {
        _agent         = agent;
        _hardware      = hardware;
        _resources     = resources;
        _metrics       = metrics;
        _llmEngine     = llmEngine;
        _modelSwitcher = modelSwitcher;
        _modelRegistry = modelRegistry;
        _pullStatus    = pullStatus;
        _hwConfigStore = hwConfigStore;
        _ruleStore     = ruleStore;
        _logStore      = logStore;
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
                IpcCommandType.GetHardwareConfig  => GetHardwareConfig(),
                IpcCommandType.SaveHardwareConfig => SaveHardwareConfig(cmd),
                IpcCommandType.GetAgentRules      => GetAgentRules(),
                IpcCommandType.AddAgentRule       => await AddAgentRuleAsync(cmd, ct),
                IpcCommandType.RemoveAgentRule    => await RemoveAgentRuleAsync(cmd, ct),
                IpcCommandType.GetLogs            => GetLogs(cmd),
                IpcCommandType.ExecuteCliCommand  => await ExecuteCliCommandAsync(cmd, ct),
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

    // FR-A1..A4: agent rule management
    private IpcResponse GetAgentRules()
    {
        return new IpcResponse { Success = true, Payload = _ruleStore.GetAll() };
    }

    private async Task<IpcResponse> AddAgentRuleAsync(IpcCommand cmd, CancellationToken ct)
    {
        var json = cmd.Parameters.GetValueOrDefault("Rule", string.Empty);
        if (string.IsNullOrEmpty(json))
            return new IpcResponse { Success = false, Error = "Rule payload required" };

        var rule = JsonSerializer.Deserialize<AgentRuleDto>(json, _jsonOpts);
        if (rule is null)
            return new IpcResponse { Success = false, Error = "Invalid rule JSON" };

        Enum.TryParse<AgentAction>(rule.ActionType, out var action);

        var extList = rule.AllowedExtensions
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        var config = new AgentFolderConfig
        {
            Path                  = rule.InputPath,
            OutputPath            = rule.OutputPath,
            SystemPrompt          = rule.SystemPrompt,
            Action                = action,
            FileExtensionsAllowed = extList,
            AllowInternetAccess   = rule.AllowInternetAccess
        };

        await _agent.StartWatching(config, ct);
        _ruleStore.Add(rule);

        return new IpcResponse { Success = true, Payload = rule };
    }

    private async Task<IpcResponse> RemoveAgentRuleAsync(IpcCommand cmd, CancellationToken ct)
    {
        var idStr = cmd.Parameters.GetValueOrDefault("Id", string.Empty);
        if (!Guid.TryParse(idStr, out var id))
            return new IpcResponse { Success = false, Error = "Valid Id required" };

        var rule = _ruleStore.Get(id);
        if (rule is not null)
            await _agent.StopWatching(rule.InputPath, ct);

        _ruleStore.Remove(id);
        return new IpcResponse { Success = true };
    }

    // FR-T1..T3: terminal log access & CLI
    private IpcResponse GetLogs(IpcCommand cmd)
    {
        int.TryParse(cmd.Parameters.GetValueOrDefault("Offset", "0"), out var offset);
        return new IpcResponse { Success = true, Payload = _logStore.GetFrom(offset) };
    }

    private async Task<IpcResponse> ExecuteCliCommandAsync(IpcCommand cmd, CancellationToken ct)
    {
        var raw   = cmd.Parameters.GetValueOrDefault("Command", string.Empty).Trim();
        var input = raw.StartsWith('/') ? raw : "/" + raw;
        var verb  = input.Split(' ')[0].ToLowerInvariant();

        var response = verb switch
        {
            "/ping"     => "PONG — AsmodayCat.Service is alive",
            "/help"     => "Available: /ping /status /kill_all /clear /help",
            "/status"   => $"Running | model: {(_metrics.ActiveModelName is { Length: > 0 } m ? m : "none")} | logs: {_logStore.Count}",
            "/kill_all" => await KillAllCliAsync(ct),
            "/clear"    => ClearLogsCliAsync(),
            _           => $"Unknown command '{verb}'. Type /help for usage."
        };

        _logStore.Add(new Shared.Models.LogEntryDto
        {
            Level   = Shared.Models.LogEntryLevel.System,
            Message = response,
            Source  = "CLI"
        });

        return new IpcResponse { Success = true, Payload = new { response } };
    }

    private async Task<string> KillAllCliAsync(CancellationToken ct)
    {
        await _agent.CancelAllAsync();
        return "All active agents cancelled.";
    }

    private string ClearLogsCliAsync()
    {
        _logStore.Clear();
        return "Terminal buffer cleared.";
    }

    // FR-H5: hardware config persistence
    private IpcResponse GetHardwareConfig()
    {
        return new IpcResponse { Success = true, Payload = _hwConfigStore.Current };
    }

    private IpcResponse SaveHardwareConfig(IpcCommand cmd)
    {
        var json = cmd.Parameters.GetValueOrDefault("Config", string.Empty);
        if (string.IsNullOrEmpty(json))
            return new IpcResponse { Success = false, Error = "Config payload required" };

        var config = JsonSerializer.Deserialize<HardwareConfigDto>(json, _jsonOpts);
        if (config is null)
            return new IpcResponse { Success = false, Error = "Invalid config JSON" };

        _hwConfigStore.Save(config);
        _llmEngine.Configure(config);
        return new IpcResponse { Success = true };
    }
}
