using System.Net.Http;
using AsmodayCat.Agent;
using AsmodayCat.Agent.Workspace;
using AsmodayCat.Core.Engine;
using AsmodayCat.Core.Hardware;
using AsmodayCat.Service;
using AsmodayCat.Service.Ipc;
using AsmodayCat.Shared.Interfaces;

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

// IPC infrastructure
builder.Services.AddSingleton<PullStatusStore>();
builder.Services.AddSingleton<HardwareConfigStore>();
builder.Services.AddSingleton<AgentRuleStore>();

// Hosted services
builder.Services.AddHostedService<Worker>();
builder.Services.AddHostedService<IpcServer>();

var host = builder.Build();

// Apply persisted hardware config to the LLM engine before starting
var hwStore    = host.Services.GetRequiredService<HardwareConfigStore>();
var llmEngine  = host.Services.GetRequiredService<ILLMEngine>();
llmEngine.Configure(hwStore.Load());

host.Run();
