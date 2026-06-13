using System.Net.Http;
using AsmodayCat.Agent;
using AsmodayCat.Agent.Workspace;
using AsmodayCat.Core.Data;
using AsmodayCat.Core.Data.Repositories;
using AsmodayCat.Core.Engine;
using AsmodayCat.Core.Hardware;
using AsmodayCat.Service;
using AsmodayCat.Service.Ipc;
using AsmodayCat.Shared.Enums;
using AsmodayCat.Shared.Interfaces;
using AsmodayCat.Shared.Models;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "AsmodayCat";
});

// Core
builder.Services.AddSingleton<ResourceManager>();
builder.Services.AddSingleton<MetricsCollector>();
builder.Services.AddSingleton<IHardwareScanner, HardwareScanner>();
builder.Services.AddSingleton(_ =>
{
    var baseUrl = builder.Configuration["Ollama:BaseUrl"] ?? "http://localhost:11434";
    return new HttpClient { BaseAddress = new Uri(baseUrl) };
});
builder.Services.AddSingleton<OllamaClient>();
builder.Services.AddSingleton<ILLMEngine>(sp => sp.GetRequiredService<OllamaClient>());
builder.Services.AddSingleton<IModelRegistry, ModelRegistryClient>();
builder.Services.AddSingleton<ModelSwitcher>();
builder.Services.AddSingleton<SmartRouter>();

// Agent
builder.Services.AddSingleton<AccessManager>();
builder.Services.AddSingleton<IAgentController, AgentController>();

// Database layer
builder.Services.AddSingleton<AsmodayDatabase>();
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

// Hosted services
builder.Services.AddHostedService<Worker>();
builder.Services.AddHostedService<IpcServer>();

var host = builder.Build();

// Initialize database (creates tables + seeds defaults)
var migrator = host.Services.GetRequiredService<DatabaseMigrator>();
migrator.Initialize();

// Apply persisted hardware config to the LLM engine
var hwRepo    = host.Services.GetRequiredService<HardwareSettingsRepository>();
var llmEngine = host.Services.GetRequiredService<ILLMEngine>();
llmEngine.Configure(hwRepo.GetConfig());

// Step 4: restore active agent workspaces from DB
await RestoreWorkspacesAsync(host);

host.Run();

static async Task RestoreWorkspacesAsync(IHost host)
{
    var repo  = host.Services.GetRequiredService<AgentWorkspaceRepository>();
    var agent = host.Services.GetRequiredService<IAgentController>();

    foreach (var rule in repo.GetActive())
    {
        try
        {
            Enum.TryParse<AgentAction>(rule.ActionType, out var action);
            var config = new AgentFolderConfig
            {
                Path                  = rule.InputPath,
                OutputPath            = rule.OutputPath,
                SystemPrompt          = rule.SystemPrompt,
                Action                = action,
                FileExtensionsAllowed = rule.AllowedExtensions
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToList(),
                AllowInternetAccess   = rule.AllowInternetAccess
            };
            await agent.StartWatching(config);
        }
        catch { /* directory may not exist — skip silently */ }
    }
}
