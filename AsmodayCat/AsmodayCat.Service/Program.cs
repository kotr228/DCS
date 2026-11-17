using System.Net.Http;
using AsmodayCat.Agent;
using AsmodayCat.Agent.Tools;
using AsmodayCat.Agent.Tools.System;
using AsmodayCat.Agent.Tools.Web;
using AsmodayCat.Agent.Workspace;
using AsmodayCat.Core.Data;
using AsmodayCat.Core.Data.Repositories;
using AsmodayCat.Core.Engine;
using AsmodayCat.Core.Hardware;
using AsmodayCat.Service;
using AsmodayCat.Service.Approvals;
using AsmodayCat.Service.Chat;
using AsmodayCat.Service.Ipc;
using AsmodayCat.Shared.Interfaces;
using AsmodayCat.Shared.Models;

// One instance per user: a second start (autostart + UI launch) just exits
using var singleInstance = new Mutex(true, @"Local\AsmodayCat.Service", out var isFirstInstance);
if (!isFirstInstance) return;

// A crash must leave a trace in the log (the app restarts the service, see TrayService)
AppDomain.CurrentDomain.UnhandledException += (_, e) => FileLoggerProvider.WriteCrash(e.ExceptionObject as Exception);
TaskScheduler.UnobservedTaskException += (_, e) =>
{
    FileLoggerProvider.WriteCrash(e.Exception);
    e.SetObserved();
};

// Content root = exe folder, so appsettings.json is found when started from autostart or the UI
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args            = args,
    ContentRootPath = AppContext.BaseDirectory
});

// Database first: connection settings changed in the app override appsettings.json
var database = new AsmodayDatabase();
new DatabaseMigrator(database).Initialize();
var savedSettings = new SettingsRepository(database);
AsmodayCat.Shared.Text.L.Lang = savedSettings.GetSetting(AsmodayCat.Shared.Text.L.SettingKey) ?? "uk";
builder.Configuration.AddInMemoryCollection(ConnectionSettingsDto.Keys
    .Select(k => new KeyValuePair<string, string?>(k, savedSettings.GetSetting(k)))
    .Where(kv => !string.IsNullOrWhiteSpace(kv.Value)));
builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "AsmodayCat";
});

// Core
builder.Services.AddSingleton<GpuMonitor>();
builder.Services.AddSingleton<ResourceManager>();
builder.Services.AddSingleton<MetricsCollector>();
builder.Services.AddSingleton<IActivityLog>(sp => sp.GetRequiredService<MetricsCollector>());
builder.Services.AddSingleton<IHardwareScanner, HardwareScanner>();
builder.Services.AddSingleton(_ =>
{
    var baseUrl = builder.Configuration["Ollama:BaseUrl"] ?? "http://localhost:11434";
    // Model loading and long generations can take minutes
    return new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromMinutes(15) };
});
builder.Services.AddSingleton<OllamaClient>();
builder.Services.AddSingleton<OllamaLauncher>();
builder.Services.AddSingleton<ILLMEngine>(sp => sp.GetRequiredService<OllamaClient>());
builder.Services.AddSingleton<IModelRegistry, ModelRegistryClient>();
builder.Services.AddSingleton<ModelSwitcher>();
builder.Services.AddSingleton<SmartRouter>();

// Agent tools
builder.Services.AddSingleton<IAgentTool, FileSystemTool>();
builder.Services.AddSingleton<IAgentTool, ShellExecutionTool>();
builder.Services.AddSingleton<IAgentTool, WebSearchTool>();
builder.Services.AddSingleton<IAgentTool, WebScraperTool>();
builder.Services.AddSingleton<IAgentTool>(_ =>
    new ImageGenerationTool(builder.Configuration["StableDiffusion:BaseUrl"] ?? "http://127.0.0.1:7860"));
builder.Services.AddSingleton<IAgentTool, MemoryTool>();
builder.Services.AddSingleton<ToolRegistry>();
builder.Services.AddSingleton<MemoryRepository>();
builder.Services.AddSingleton<AsmodayCat.Agent.Memory.AssistantMemory>();

// Agent
builder.Services.AddSingleton<NotificationStore>();
builder.Services.AddSingleton<INotificationSink>(sp => sp.GetRequiredService<NotificationStore>());
builder.Services.AddSingleton<ApprovalBroker>();
builder.Services.AddSingleton<IApprovalService>(sp => sp.GetRequiredService<ApprovalBroker>());
builder.Services.AddSingleton<AgentRunner>();
builder.Services.AddSingleton<AccessManager>();
builder.Services.AddSingleton<IAgentController, AgentController>();
builder.Services.AddSingleton<ChatSession>();

// Database layer
builder.Services.AddSingleton(database);
builder.Services.AddSingleton<DatabaseMigrator>();
builder.Services.AddSingleton<HardwareSettingsRepository>();
builder.Services.AddSingleton<SettingsRepository>();
builder.Services.AddSingleton<AgentWorkspaceRepository>();
builder.Services.AddSingleton<ChatRepository>();
builder.Services.AddSingleton<ModelPoolRepository>();

// IPC infrastructure (in-memory runtime stores)
builder.Services.AddSingleton<PullStatusStore>();
builder.Services.AddSingleton<TerminalLogStore>();
builder.Services.AddSingleton<ILoggerProvider, TerminalLoggerProvider>();
builder.Logging.AddProvider(new FileLoggerProvider());

// Hosted services
builder.Services.AddHostedService<Worker>();
builder.Services.AddHostedService<IpcServer>();

var host = builder.Build();

// Apply persisted hardware config to the LLM engine
var hwRepo    = host.Services.GetRequiredService<HardwareSettingsRepository>();
var llmEngine = host.Services.GetRequiredService<ILLMEngine>();
llmEngine.Configure(hwRepo.GetConfig());

// Restore active agent workspaces from DB
await RestoreWorkspacesAsync(host);

host.Run();

static async Task RestoreWorkspacesAsync(IHost host)
{
    var repo   = host.Services.GetRequiredService<AgentWorkspaceRepository>();
    var agent  = host.Services.GetRequiredService<IAgentController>();
    var logger = host.Services.GetRequiredService<ILogger<AgentController>>();

    foreach (var rule in repo.GetActive())
    {
        try
        {
            await agent.StartWatching(IpcServer.WithCatchUp(rule));
        }
        catch (Exception ex)
        {
            // The rule stays active in the DB and is retried on the next start
            logger.LogWarning("Could not start watching {Path}: {Error}", rule.InputPath, ex.Message);
        }
    }
}
