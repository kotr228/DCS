using System.Net.Http;
using AsmodayCat.Agent;
using AsmodayCat.Agent.Workspace;
using AsmodayCat.Core.Engine;
using AsmodayCat.Core.Hardware;
using AsmodayCat.Service;
using AsmodayCat.Service.Ipc;
using AsmodayCat.Shared.Interfaces;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.UseWindowsService(options =>
{
    options.ServiceName = "AsmodayCat";
});

// Core
builder.Services.AddSingleton<ResourceManager>();
builder.Services.AddSingleton<IHardwareScanner, HardwareScanner>();
builder.Services.AddHttpClient<OllamaClient>(client =>
{
    client.BaseAddress = new Uri(
        builder.Configuration["Ollama:BaseUrl"] ?? "http://localhost:11434");
});
builder.Services.AddSingleton<ILLMEngine, OllamaClient>();

// Agent
builder.Services.AddSingleton<AccessManager>();
builder.Services.AddSingleton<IAgentController, AgentController>();

// Hosted services
builder.Services.AddHostedService<Worker>();
builder.Services.AddHostedService<IpcServer>();

var host = builder.Build();
host.Run();
