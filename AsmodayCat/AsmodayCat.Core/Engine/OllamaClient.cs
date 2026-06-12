using System.Net.Http.Json;
using AsmodayCat.Shared.Enums;
using AsmodayCat.Shared.Interfaces;
using AsmodayCat.Shared.Models;

namespace AsmodayCat.Core.Engine;

// Клієнт для Ollama REST API (http://localhost:11434)
public class OllamaClient : ILLMEngine
{
    private readonly HttpClient _http;
    private string _currentModel = string.Empty;

    public OllamaClient(HttpClient http)
    {
        _http = http;
    }

    public Task LoadModelAsync(string modelPath, CancellationToken cancellationToken = default)
    {
        _currentModel = modelPath;
        return Task.CompletedTask;
    }

    public async Task<LlmResponse> GenerateAsync(LlmRequest request, CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            model = _currentModel,
            prompt = request.Prompt,
            system = request.Context,
            stream = false,
            options = BuildOptions(request.ExecutionDevice)
        };

        var started = DateTime.UtcNow;
        var response = await _http.PostAsJsonAsync("/api/generate", payload, cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<OllamaGenerateResponse>(
            cancellationToken: cancellationToken);

        var elapsed = (DateTime.UtcNow - started).TotalSeconds;
        var tokens = result?.EvalCount ?? 0;

        return new LlmResponse
        {
            Content = result?.Response ?? string.Empty,
            TokensPerSecond = elapsed > 0 ? tokens / elapsed : 0,
            UsedDevice = request.ExecutionDevice
        };
    }

    public Task UnloadAsync(CancellationToken cancellationToken = default)
    {
        _currentModel = string.Empty;
        return Task.CompletedTask;
    }

    private static object BuildOptions(ExecutionDevice device) => device switch
    {
        ExecutionDevice.GPU_Nvidia => new { num_gpu = 99 },
        ExecutionDevice.GPU_AMD => new { num_gpu = 99 },
        _ => new { num_gpu = 0 }
    };

    private sealed class OllamaGenerateResponse
    {
        public string Response { get; set; } = string.Empty;
        public int EvalCount { get; set; }
    }
}
