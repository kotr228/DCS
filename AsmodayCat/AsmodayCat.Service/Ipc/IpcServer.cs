using AsmodayCat.Shared.Text;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using AsmodayCat.Core.Data.Repositories;
using AsmodayCat.Core.Engine;
using AsmodayCat.Core.Hardware;
using AsmodayCat.Service.Approvals;
using AsmodayCat.Service.Chat;
using AsmodayCat.Shared.Enums;
using AsmodayCat.Shared.Interfaces;
using AsmodayCat.Shared.Models;

namespace AsmodayCat.Service.Ipc;

public class IpcServer : BackgroundService
{
    public static string PipeName => IpcPipe.Name;

    private readonly IAgentController           _agent;
    private readonly ResourceManager            _resources;
    private readonly MetricsCollector           _metrics;
    private readonly OllamaClient               _ollama;
    private readonly IModelRegistry             _modelRegistry;
    private readonly PullStatusStore            _pullStatus;
    private readonly HardwareSettingsRepository _hwRepo;
    private readonly AgentWorkspaceRepository   _workspaceRepo;
    private readonly TerminalLogStore           _logStore;
    private readonly ChatSession                _chat;
    private readonly ApprovalBroker             _approvals;
    private readonly OllamaLauncher             _launcher;
    private readonly MemoryRepository           _memory;
    private readonly NotificationStore          _notifications;
    private readonly SettingsRepository         _settings;
    private readonly IConfiguration             _config;
    private readonly IHostApplicationLifetime   _lifetime;
    private readonly ILogger<IpcServer>         _logger;

    private static readonly JsonSerializerOptions _jsonOpts =
        new() { PropertyNameCaseInsensitive = true };

    public IpcServer(
        IAgentController           agent,
        ResourceManager            resources,
        MetricsCollector           metrics,
        OllamaClient               ollama,
        IModelRegistry             modelRegistry,
        PullStatusStore            pullStatus,
        HardwareSettingsRepository hwRepo,
        AgentWorkspaceRepository   workspaceRepo,
        TerminalLogStore           logStore,
        ChatSession                chat,
        ApprovalBroker             approvals,
        OllamaLauncher             launcher,
        MemoryRepository           memory,
        NotificationStore          notifications,
        SettingsRepository         settings,
        IConfiguration             config,
        IHostApplicationLifetime   lifetime,
        ILogger<IpcServer>         logger)
    {
        _agent         = agent;
        _resources     = resources;
        _metrics       = metrics;
        _ollama        = ollama;
        _modelRegistry = modelRegistry;
        _pullStatus    = pullStatus;
        _hwRepo        = hwRepo;
        _workspaceRepo = workspaceRepo;
        _logStore      = logStore;
        _chat          = chat;
        _approvals     = approvals;
        _launcher      = launcher;
        _memory        = memory;
        _notifications = notifications;
        _settings      = settings;
        _config        = config;
        _lifetime      = lifetime;
        _logger        = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var security = CreatePipeSecurity();

        while (!stoppingToken.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe = null;
            try
            {
                pipe = NamedPipeServerStreamAcl.Create(
                    PipeName, PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous,
                    inBufferSize: 0, outBufferSize: 0,
                    security);

                await pipe.WaitForConnectionAsync(stoppingToken);

                // The handler owns the pipe from here
                var connected = pipe;
                pipe = null;
                _ = HandleClientAsync(connected, stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "IPC server error");
                await Task.Delay(500, stoppingToken).ContinueWith(_ => { });
            }
            finally
            {
                pipe?.Dispose();
            }
        }
    }

    // Only the account that runs the service may talk to it (no other users, no network clients):
    // the service can write files and run commands on that user's behalf.
    private static PipeSecurity CreatePipeSecurity()
    {
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.NetworkSid, null),
            PipeAccessRights.FullControl, AccessControlType.Deny));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            WindowsIdentity.GetCurrent().User!,
            PipeAccessRights.FullControl, AccessControlType.Allow));
        return security;
    }

    private async Task HandleClientAsync(NamedPipeServerStream pipe, CancellationToken ct)
    {
        await using var _ = pipe;
        try
        {
            using var reader       = new StreamReader(pipe, leaveOpen: true);
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
        catch (IOException) { /* client went away */ }
        catch (OperationCanceledException) { }
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
                IpcCommandType.GetStatus           => new IpcResponse { Success = true, Payload = new { Status = "Running" } },
                IpcCommandType.GetSystemLoad       => new IpcResponse { Success = true, Payload = _resources.GetCurrentStatus() },
                IpcCommandType.StartAgent          => await StartAgentAsync(cmd, ct),
                IpcCommandType.StopAgent           => await StopAgentAsync(cmd, ct),
                IpcCommandType.KillSwitch          => await KillSwitchAsync(),
                IpcCommandType.ListModels          => await ListModelsAsync(ct),
                IpcCommandType.PullModel           => PullModelBackground(cmd),
                IpcCommandType.GetPullStatus       => GetPullStatus(cmd),
                IpcCommandType.GetDashboardStats   => GetDashboardStats(),
                IpcCommandType.ClearContext        => ClearContextCmd(),
                IpcCommandType.GetModelPool        => await GetModelPoolAsync(ct),
                IpcCommandType.UnloadModel         => await UnloadModelAsync(ct),
                IpcCommandType.ChangeHardwareModel => await UnloadModelAsync(ct),   // legacy dashboard "unload"
                IpcCommandType.GetHardwareConfig   => GetHardwareConfig(),
                IpcCommandType.SaveHardwareConfig  => SaveHardwareConfig(cmd),
                IpcCommandType.GetAgentRules       => GetAgentRules(),
                IpcCommandType.AddAgentRule        => await AddAgentRuleAsync(cmd, ct),
                IpcCommandType.RemoveAgentRule     => await RemoveAgentRuleAsync(cmd, ct),
                IpcCommandType.SetAgentRuleActive  => await SetAgentRuleActiveAsync(cmd, ct),
                IpcCommandType.GetLogs             => GetLogs(cmd),
                IpcCommandType.ExecuteCliCommand   => await ExecuteCliCommandAsync(cmd, ct),
                IpcCommandType.GetPendingApprovals => new IpcResponse { Success = true, Payload = _approvals.GetPending() },
                IpcCommandType.ResolveApproval     => ResolveApproval(cmd),
                IpcCommandType.CancelChat          => CancelChat(),
                IpcCommandType.GetEngineStatus     => await GetEngineStatusAsync(ct),
                IpcCommandType.DeleteModel         => await DeleteModelAsync(cmd, ct),
                IpcCommandType.GetChatSessions     => new IpcResponse { Success = true, Payload = _chat.GetSessions(cmd.Parameters.GetValueOrDefault("Query")) },
                IpcCommandType.GetNotifications    => new IpcResponse
                {
                    Success = true,
                    Payload = _notifications.GetSince(long.TryParse(cmd.Parameters.GetValueOrDefault("After"), out var after) ? after : -1)
                },
                IpcCommandType.GetConnectionSettings  => new IpcResponse { Success = true, Payload = GetConnectionSettings() },
                IpcCommandType.SaveConnectionSettings => SaveConnectionSettings(cmd),
                IpcCommandType.RestartService         => RestartService(),
                IpcCommandType.CancelPull             => CancelPull(cmd),
                IpcCommandType.UpdateAgentRule        => await UpdateAgentRuleAsync(cmd, ct),
                IpcCommandType.SetLanguage            => SetLanguage(cmd),
                IpcCommandType.TruncateChat        => int.TryParse(cmd.Parameters.GetValueOrDefault("FromIndex"), out var from)
                                                          ? new IpcResponse { Success = true, Payload = _chat.Truncate(from) }
                                                          : new IpcResponse { Success = false, Error = "FromIndex required" },
                IpcCommandType.GetChatHistory      => new IpcResponse { Success = true, Payload = _chat.GetHistory(ParseId(cmd)) },
                IpcCommandType.OpenChatSession     => ParseId(cmd) is { } openId
                                                          ? new IpcResponse { Success = true, Payload = _chat.Open(openId) }
                                                          : new IpcResponse { Success = false, Error = "Valid Id required" },
                IpcCommandType.DeleteChatSession   => DeleteChatSession(cmd),
                IpcCommandType.GetMemory           => GetMemory(cmd),
                IpcCommandType.DeleteMemory        => long.TryParse(cmd.Parameters.GetValueOrDefault("Id"), out var memId)
                                                          ? new IpcResponse { Success = _memory.Delete(memId) }
                                                          : new IpcResponse { Success = false, Error = "Valid Id required" },
                IpcCommandType.ClearMemory         => new IpcResponse { Success = true, Payload = _memory.Clear() },
                IpcCommandType.AddMemoryFact       => AddMemoryFact(cmd),
                IpcCommandType.ReprocessAgentRule  => ParseId(cmd) is { } ruleId
                                                          ? new IpcResponse { Success = true, Payload = _agent.Reprocess(ruleId) }
                                                          : new IpcResponse { Success = false, Error = "Valid Id required" },
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
        var allowWeb    = cmd.Parameters.GetValueOrDefault("AllowWeb")   == "true";
        var allowTools  = cmd.Parameters.GetValueOrDefault("AllowTools") == "true";
        var attachments = cmd.Parameters.TryGetValue("Attachments", out var ap)
            ? ap.Split(';', StringSplitOptions.RemoveEmptyEntries).ToList()
            : new List<string>();

        var quick = cmd.Parameters.GetValueOrDefault("Quick") == "true";
        await _chat.RunAsync(message, attachments, allowWeb, allowTools,
            chunk => writer.WriteLineAsync(JsonSerializer.Serialize(chunk)), ct, quick);
    }

    private IpcResponse GetMemory(IpcCommand cmd)
    {
        var query   = cmd.Parameters.GetValueOrDefault("Query")?.Trim();
        var entries = string.IsNullOrEmpty(query) ? _memory.Recent(300) : _memory.Search(query, 100);
        return new IpcResponse
        {
            Success = true,
            Payload = entries.Select(e => new MemoryEntryDto
            {
                Id = e.Id, Kind = e.Kind, Text = e.Text, Source = e.Source, CreatedAt = e.CreatedAt
            }).ToList()
        };
    }

    private IpcResponse AddMemoryFact(IpcCommand cmd)
    {
        var text = cmd.Parameters.GetValueOrDefault("Text")?.Trim();
        if (string.IsNullOrEmpty(text)) return new IpcResponse { Success = false, Error = L.T("Порожній текст", "Empty text", "Пустой текст") };
        return new IpcResponse { Success = true, Payload = _memory.Add(MemoryKind.Fact, text, L.T("Користувач", "User", "Пользователь")) };
    }

    // ── Connection settings (applied after a restart) ─────────────────────────

    private ConnectionSettingsDto GetConnectionSettings() => new()
    {
        OllamaBaseUrl          = _config["Ollama:BaseUrl"] ?? "http://localhost:11434",
        StableDiffusionBaseUrl = _config["StableDiffusion:BaseUrl"] ?? "http://127.0.0.1:7860",
        ChatWorkspacePath      = _chat.WorkspacePath
    };

    private IpcResponse SaveConnectionSettings(IpcCommand cmd)
    {
        var json = cmd.Parameters.GetValueOrDefault("Settings", string.Empty);
        var s = JsonSerializer.Deserialize<ConnectionSettingsDto>(json, _jsonOpts);
        if (s is null) return new IpcResponse { Success = false, Error = L.T("Порожні налаштування", "Empty settings", "Пустые настройки") };

        foreach (var (url, name) in new[] { (s.OllamaBaseUrl, "Ollama"), (s.StableDiffusionBaseUrl, "Stable Diffusion") })
            if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var u) || (u.Scheme != "http" && u.Scheme != "https"))
                return new IpcResponse { Success = false, Error = L.T($"Адреса {name} має виглядати як http://сервер:порт", $"The {name} address must look like http://server:port", $"Адрес {name} должен выглядеть как http://сервер:порт") };

        var folder = s.ChatWorkspacePath.Trim().Trim('"');
        if (folder.Length > 0)
        {
            try { Directory.CreateDirectory(folder); }
            catch (Exception ex) { return new IpcResponse { Success = false, Error = L.T($"Тека помічника недоступна: {ex.Message}", $"The assistant folder is not available: {ex.Message}", $"Папка помощника недоступна: {ex.Message}") }; }
        }

        _settings.SetSetting("Ollama:BaseUrl", s.OllamaBaseUrl.Trim().TrimEnd('/'));
        _settings.SetSetting("StableDiffusion:BaseUrl", s.StableDiffusionBaseUrl.Trim().TrimEnd('/'));
        _settings.SetSetting("Chat:WorkspacePath", folder);
        return new IpcResponse { Success = true };
    }

    // Exits after replying; the UI starts the service again
    private IpcResponse RestartService()
    {
        _logger.LogInformation("Restart requested from the app");
        _ = Task.Delay(500).ContinueWith(_ => _lifetime.StopApplication());
        return new IpcResponse { Success = true };
    }

    private static Guid? ParseId(IpcCommand cmd) =>
        Guid.TryParse(cmd.Parameters.GetValueOrDefault("Id"), out var id) ? id : null;

    private IpcResponse DeleteChatSession(IpcCommand cmd)
    {
        if (ParseId(cmd) is not { } id) return new IpcResponse { Success = false, Error = "Valid Id required" };
        _chat.Delete(id);
        return new IpcResponse { Success = true };
    }

    private IpcResponse CancelChat()
    {
        _chat.CancelCurrent();
        return new IpcResponse { Success = true };
    }

    private IpcResponse ClearContextCmd()
    {
        _chat.Clear();
        _ollama.ClearContext();
        return new IpcResponse { Success = true };
    }

    // ── Approvals ─────────────────────────────────────────────────────────────

    private IpcResponse ResolveApproval(IpcCommand cmd)
    {
        if (!Guid.TryParse(cmd.Parameters.GetValueOrDefault("Id"), out var id))
            return new IpcResponse { Success = false, Error = "Valid Id required" };

        var approved = cmd.Parameters.GetValueOrDefault("Approved") == "true";
        var found    = _approvals.Resolve(id, approved);
        return new IpcResponse { Success = found, Error = found ? null : L.T("Запит уже не актуальний", "The request is no longer pending", "Запрос уже не актуален") };
    }

    // ── Agent ─────────────────────────────────────────────────────────────────

    private async Task<IpcResponse> StartAgentAsync(IpcCommand cmd, CancellationToken ct)
    {
        Enum.TryParse<AgentAction>(cmd.Parameters.GetValueOrDefault("Action", nameof(AgentAction.CreateReport)), out var action);

        var config = new AgentFolderConfig
        {
            Path         = cmd.Parameters.GetValueOrDefault("Path", string.Empty),
            OutputPath   = cmd.Parameters.GetValueOrDefault("OutputPath", string.Empty),
            SystemPrompt = cmd.Parameters.GetValueOrDefault("SystemPrompt", string.Empty),
            Action       = action
        };
        await _agent.StartWatching(config, ct);
        return new IpcResponse { Success = true };
    }

    private async Task<IpcResponse> StopAgentAsync(IpcCommand cmd, CancellationToken ct)
    {
        await _agent.StopWatchingPath(cmd.Parameters.GetValueOrDefault("Path", string.Empty), ct);
        return new IpcResponse { Success = true };
    }

    // FR6.3: stop watchers, the chat answer and any pending approvals.
    // Rules are paused in the DB so they don't silently restart with the service.
    private async Task<IpcResponse> KillSwitchAsync()
    {
        _logger.LogWarning("Kill Switch activated — stopping all agents and the chat");
        _chat.CancelCurrent();
        _approvals.DenyAll();
        await _agent.CancelAllAsync();

        foreach (var rule in _workspaceRepo.GetActive())
        {
            rule.IsActive = false;
            _workspaceRepo.Update(rule);
        }
        return new IpcResponse { Success = true };
    }

    // Files added or changed since the rule was created but never processed (e.g. while the service was off)
    public static AgentFolderConfig WithCatchUp(AgentRuleDto rule)
    {
        var config = ToConfig(rule);
        config.CatchUpSince = rule.CreatedAt;
        return config;
    }

    public static AgentFolderConfig ToConfig(AgentRuleDto rule)
    {
        Enum.TryParse<AgentAction>(rule.ActionType, out var action);
        return new AgentFolderConfig
        {
            Id                    = rule.Id,
            Path                  = rule.InputPath,
            OutputPath            = rule.OutputPath,
            SystemPrompt          = rule.SystemPrompt,
            Action                = action,
            FileExtensionsAllowed = rule.AllowedExtensions
                .Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList(),
            AllowInternetAccess   = rule.AllowInternetAccess
        };
    }

    // FR-A1..A4: agent rule management (SQLite-backed)
    private IpcResponse GetAgentRules()
    {
        // IsActive = wanted and actually running (a missing folder leaves it stopped)
        var rules = _workspaceRepo.GetAll();
        foreach (var r in rules)
        {
            r.IsActive   = r.IsActive && _agent.IsWatching(r.Id);
            r.OutputPath = AsmodayCat.Agent.AgentController.ResolveOutputPath(ToConfig(r));   // show the real folder
        }
        return new IpcResponse { Success = true, Payload = rules };
    }

    private async Task<IpcResponse> AddAgentRuleAsync(IpcCommand cmd, CancellationToken ct)
    {
        var json = cmd.Parameters.GetValueOrDefault("Rule", string.Empty);
        if (string.IsNullOrEmpty(json))
            return new IpcResponse { Success = false, Error = "Rule payload required" };

        var rule = JsonSerializer.Deserialize<AgentRuleDto>(json, _jsonOpts);
        if (rule is null || string.IsNullOrWhiteSpace(rule.InputPath))
            return new IpcResponse { Success = false, Error = L.T("Вкажіть теку, за якою стежити", "Choose a folder to watch", "Укажите папку для наблюдения") };

        rule.InputPath = Path.GetFullPath(rule.InputPath.Trim().Trim('"'));
        rule.OutputPath = rule.OutputPath.Trim().Trim('"');

        var config = ToConfig(rule);
        if (cmd.Parameters.GetValueOrDefault("ProcessExisting") == "true")
            config.CatchUpSince = DateTime.MinValue;          // everything already in the folder

        await _agent.StartWatching(config, ct);
        rule.IsActive = true;
        _workspaceRepo.Add(rule);

        return new IpcResponse { Success = true, Payload = rule };
    }

    private async Task<IpcResponse> UpdateAgentRuleAsync(IpcCommand cmd, CancellationToken ct)
    {
        var rule = JsonSerializer.Deserialize<AgentRuleDto>(cmd.Parameters.GetValueOrDefault("Rule", "{}"), _jsonOpts);
        if (rule is null || string.IsNullOrWhiteSpace(rule.InputPath))
            return new IpcResponse { Success = false, Error = L.T("Вкажіть теку, за якою стежити", "Choose a folder to watch", "Укажите папку для наблюдения") };

        var existing = _workspaceRepo.Get(rule.Id);
        if (existing is null) return new IpcResponse { Success = false, Error = L.T("Правило не знайдено", "Rule not found", "Правило не найдено") };

        rule.InputPath  = Path.GetFullPath(rule.InputPath.Trim().Trim('"'));
        rule.OutputPath = rule.OutputPath.Trim().Trim('"');
        rule.IsActive   = existing.IsActive;
        rule.CreatedAt  = existing.CreatedAt;

        var wasWatching = _agent.IsWatching(rule.Id);
        await _agent.StopWatching(rule.Id, ct);
        try
        {
            if (rule.IsActive) await _agent.StartWatching(ToConfig(rule), ct);
        }
        catch
        {
            // New settings don't work (e.g. folder missing): keep the old rule running
            if (wasWatching) await _agent.StartWatching(ToConfig(existing), ct);
            throw;
        }
        _workspaceRepo.Update(rule);
        return new IpcResponse { Success = true, Payload = rule };
    }

    private async Task<IpcResponse> RemoveAgentRuleAsync(IpcCommand cmd, CancellationToken ct)
    {
        if (!Guid.TryParse(cmd.Parameters.GetValueOrDefault("Id"), out var id))
            return new IpcResponse { Success = false, Error = "Valid Id required" };

        await _agent.StopWatching(id, ct);
        _workspaceRepo.Delete(id);
        return new IpcResponse { Success = true };
    }

    private async Task<IpcResponse> SetAgentRuleActiveAsync(IpcCommand cmd, CancellationToken ct)
    {
        if (!Guid.TryParse(cmd.Parameters.GetValueOrDefault("Id"), out var id))
            return new IpcResponse { Success = false, Error = "Valid Id required" };

        var rule = _workspaceRepo.Get(id);
        if (rule is null)
            return new IpcResponse { Success = false, Error = L.T("Правило не знайдено", "Rule not found", "Правило не найдено") };

        var active = cmd.Parameters.GetValueOrDefault("Active") == "true";
        // Resuming: also catch files that arrived while the rule was paused
        if (active) await _agent.StartWatching(WithCatchUp(rule), ct);
        else        await _agent.StopWatching(id, ct);

        rule.IsActive = active;
        _workspaceRepo.Update(rule);
        return new IpcResponse { Success = true, Payload = rule };
    }

    // ── Models ────────────────────────────────────────────────────────────────

    // Starts Ollama if needed, then reports whether it runs and which models it has
    private async Task<IpcResponse> GetEngineStatusAsync(CancellationToken ct)
    {
        var running = await _launcher.EnsureRunningAsync(ct);
        var models  = running ? await _modelRegistry.GetLocalModelsAsync(ct) : [];
        return new IpcResponse
        {
            Success = true,
            Payload = new { Running = running, Models = models, Error = running ? null : _launcher.LastError, Missing = !running && _launcher.ExeMissing }
        };
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

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, CancellationTokenSource> _pulls =
        new(StringComparer.OrdinalIgnoreCase);

    // Language of messages the service sends back (errors, notifications, approvals)
    private IpcResponse SetLanguage(IpcCommand cmd)
    {
        L.Lang = cmd.Parameters.GetValueOrDefault("Lang", "uk");
        _settings.SetSetting(L.SettingKey, L.Lang);
        return new IpcResponse { Success = true };
    }

    private IpcResponse CancelPull(IpcCommand cmd)
    {
        var modelId = cmd.Parameters.GetValueOrDefault("ModelId", string.Empty);
        if (!_pulls.TryGetValue(modelId, out var cts))
            return new IpcResponse { Success = false, Error = L.T("Ця модель зараз не завантажується", "This model is not downloading now", "Эта модель сейчас не загружается") };
        cts.Cancel();
        _logger.LogInformation("Download of {Model} cancelled", modelId);
        return new IpcResponse { Success = true };
    }

    private IpcResponse PullModelBackground(IpcCommand cmd)
    {
        var modelId = cmd.Parameters.GetValueOrDefault("ModelId", string.Empty);
        if (string.IsNullOrEmpty(modelId))
            return new IpcResponse { Success = false, Error = "ModelId required" };

        var progress = new Progress<ModelPullProgress>(_pullStatus.Update);
        var cts = new CancellationTokenSource();
        if (!_pulls.TryAdd(modelId, cts))
            return new IpcResponse { Success = true, Payload = new { modelId, status = "already pulling" } };

        _ = Task.Run(async () =>
        {
            try
            {
                if (!await _launcher.EnsureRunningAsync(cts.Token))
                {
                    _pullStatus.Update(new ModelPullProgress { ModelId = modelId, Status = $"error: {_launcher.LastError}" });
                    return;
                }
                await _modelRegistry.PullModelAsync(modelId, progress, cts.Token);
            }
            catch (OperationCanceledException)
            {
                // Ollama keeps the downloaded part: pulling again resumes from there
                _pullStatus.Update(new ModelPullProgress { ModelId = modelId, Status = "cancelled" });
            }
            finally
            {
                _pulls.TryRemove(modelId, out _);
                cts.Dispose();
            }
        });

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
        // Standalone mode has no network nodes: the card shows how many folder agents are watching
        var watching = _workspaceRepo.GetAll().Count(r => _agent.IsWatching(r.Id));
        var dto = _metrics.GetSnapshot(availableNodes: watching);
        return new IpcResponse { Success = true, Payload = dto };
    }

    // FR-M1/M2: merged local + pool matrix
    private async Task<IpcResponse> GetModelPoolAsync(CancellationToken ct)
    {
        await _launcher.EnsureRunningAsync(ct);

        var localSet  = (await _modelRegistry.GetLocalModelsAsync(ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var vram      = await _modelRegistry.GetLoadedModelVramAsync(ct);
        var loadedSet = vram.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);

        long VramOf(string id) =>
            vram.Where(kv => kv.Key.StartsWith(id, StringComparison.OrdinalIgnoreCase)).Sum(kv => kv.Value);

        bool Matches(IEnumerable<string> set, string id) =>
            set.Any(l => l.StartsWith(id, StringComparison.OrdinalIgnoreCase));

        // Pool matrix — always present regardless of install state
        var pool = ModelPoolConfig.DefaultPool.Select(m =>
        {
            var isActive   = Matches(loadedSet, m.ModelId);
            var isExternal = m.TaskType == ModelTaskType.ImageGeneration;
            return new LlmModelDto
            {
                Name            = m.ModelId,
                RecommendedTask = isExternal ? "Images (SD WebUI)" : m.TaskType.ToString(),
                IsExternal      = isExternal,
                VramUsageBytes  = isActive ? VramOf(m.ModelId) : 0L,
                Status          = isActive                  ? ModelPoolStatus.Loaded
                                : Matches(localSet, m.ModelId) ? ModelPoolStatus.Ready
                                :                             ModelPoolStatus.NotInstalled
            };
        }).ToList();

        // Extra custom models installed locally but not in the pool
        var poolIds = ModelPoolConfig.DefaultPool.Select(m => m.ModelId).ToList();
        foreach (var local in localSet)
        {
            if (poolIds.Any(p => local.StartsWith(p, StringComparison.OrdinalIgnoreCase))) continue;
            pool.Add(new LlmModelDto
            {
                Name            = local,
                RecommendedTask = "Custom",
                Status          = loadedSet.Contains(local) ? ModelPoolStatus.Loaded : ModelPoolStatus.Ready,
                VramUsageBytes  = VramOf(local)
            });
        }

        return new IpcResponse { Success = true, Payload = pool };
    }

    private async Task<IpcResponse> DeleteModelAsync(IpcCommand cmd, CancellationToken ct)
    {
        var modelId = cmd.Parameters.GetValueOrDefault("ModelId", string.Empty);
        if (string.IsNullOrWhiteSpace(modelId))
            return new IpcResponse { Success = false, Error = "ModelId required" };

        // A model in memory can't be deleted cleanly: unload it first
        if ((await _modelRegistry.GetLoadedModelsAsync(ct)).Any(m => m.Equals(modelId, StringComparison.OrdinalIgnoreCase)))
            await _ollama.UnloadModelAsync(modelId, ct);

        await _modelRegistry.DeleteModelAsync(modelId, ct);
        _logger.LogInformation("Deleted model {Model}", modelId);
        return new IpcResponse { Success = true };
    }

    // FR-M4: unload every model Ollama has in memory, free VRAM
    private async Task<IpcResponse> UnloadModelAsync(CancellationToken ct)
    {
        foreach (var model in await _modelRegistry.GetLoadedModelsAsync(ct))
            await _ollama.UnloadModelAsync(model, ct);

        _metrics.ActiveModelName   = string.Empty;
        _metrics.ActiveModelStatus = "Idle";
        return new IpcResponse { Success = true };
    }

    // ── Terminal (FR-T1..T3) ──────────────────────────────────────────────────

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
            "/help"     => "Available: /ping /status /models /rules /memory [query] /forget <id> /forget_all /kill_all /clear /help",
            "/memory"   => MemoryCli(input),
            "/forget"   => long.TryParse(input.Split(' ', 2).ElementAtOrDefault(1), out var mid)
                               ? (_memory.Delete(mid) ? $"Forgot #{mid}." : $"No memory #{mid}.")
                               : "Usage: /forget <id>",
            "/forget_all" => $"Memory cleared ({_memory.Clear()} entries).",
            "/status"   => $"Running | model: {(_metrics.ActiveModelName is { Length: > 0 } m ? m : "none")} | logs: {_logStore.Count}",
            "/models"   => await ModelsCliAsync(ct),
            "/rules"    => RulesCli(),
            "/kill_all" => await KillAllCliAsync(),
            "/clear"    => ClearLogsCli(),
            _           => $"Unknown command '{verb}'. Type /help for usage."
        };

        _logStore.Add(new LogEntryDto
        {
            Level   = LogEntryLevel.System,
            Message = response,
            Source  = "CLI"
        });

        return new IpcResponse { Success = true, Payload = new { response } };
    }

    private async Task<string> ModelsCliAsync(CancellationToken ct)
    {
        var local  = await _modelRegistry.GetLocalModelsAsync(ct);
        var loaded = await _modelRegistry.GetLoadedModelsAsync(ct);
        return local.Count == 0
            ? "No models installed (or Ollama is not running)."
            : $"Installed: {string.Join(", ", local)} | In memory: {(loaded.Count > 0 ? string.Join(", ", loaded) : "none")}";
    }

    private string MemoryCli(string input)
    {
        var query   = input.Split(' ', 2).ElementAtOrDefault(1)?.Trim();
        var entries = string.IsNullOrEmpty(query) ? _memory.Recent(15) : _memory.Search(query, 15);
        if (entries.Count == 0) return string.IsNullOrEmpty(query) ? "Memory is empty." : "Nothing found.";
        return $"{_memory.Count()} entries.\n" + string.Join("\n",
            entries.Select(e => $"#{e.Id} [{e.CreatedAt:yyyy-MM-dd HH:mm}] ({e.Kind}) {(e.Text.Length > 160 ? e.Text[..160] + "…" : e.Text)}"));
    }

    private string RulesCli()
    {
        var rules = _workspaceRepo.GetAll();
        return rules.Count == 0
            ? "No agent rules."
            : string.Join(" | ", rules.Select(r => $"{r.InputPath} [{(_agent.IsWatching(r.Id) ? "watching" : "stopped")}]"));
    }

    private async Task<string> KillAllCliAsync()
    {
        await KillSwitchAsync();
        return "All agents and the chat were stopped. Rules are paused.";
    }

    private string ClearLogsCli()
    {
        _logStore.Clear();
        return "Terminal buffer cleared.";
    }

    // ── Hardware config (FR-H5) ───────────────────────────────────────────────

    private IpcResponse GetHardwareConfig() =>
        new() { Success = true, Payload = _hwRepo.GetConfig() };

    private IpcResponse SaveHardwareConfig(IpcCommand cmd)
    {
        var json = cmd.Parameters.GetValueOrDefault("Config", string.Empty);
        if (string.IsNullOrEmpty(json))
            return new IpcResponse { Success = false, Error = "Config payload required" };

        var config = JsonSerializer.Deserialize<HardwareConfigDto>(json, _jsonOpts);
        if (config is null)
            return new IpcResponse { Success = false, Error = "Invalid config JSON" };

        _hwRepo.SaveConfig(config);
        _ollama.Configure(config);
        return new IpcResponse { Success = true };
    }
}
