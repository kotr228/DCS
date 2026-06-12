using System.Net.Http.Json;
using AsmodayCat.Shared.Enums;
using AsmodayCat.Shared.Interfaces;
using AsmodayCat.Shared.Models;

namespace AsmodayCat.Core.Engine;

// Клієнт для Ollama REST API (http://localhost:11434) з підтримкою Idle Timeout (FR1.3)
public class OllamaClient : ILLMEngine, IDisposable
{
    public static readonly TimeSpan DefaultIdleTimeout = TimeSpan.FromMinutes(10);

    private readonly HttpClient _http;
    private string _currentModel = string.Empty;
    private DateTime _lastUsed = DateTime.MinValue;
    private Timer? _idleTimer;
    private readonly TimeSpan _idleTimeout;

    public OllamaClient(HttpClient http, TimeSpan? idleTimeout = null)
    {
        _http = http;
        _idleTimeout = idleTimeout ?? DefaultIdleTimeout;
    }

    public Task LoadModelAsync(string modelPath, CancellationToken cancellationToken = default)
    {
        _currentModel = modelPath;
        ResetIdleTimer();
        return Task.CompletedTask;
    }

    public async Task<LlmResponse> GenerateAsync(LlmRequest request, CancellationToken cancellationToken = default)
    {
        _lastUsed = DateTime.UtcNow;
        ResetIdleTimer();

        var device = request.ExecutionDevice;

        var payload = new
        {
            model = _currentModel,
            prompt = request.Prompt,
            system = request.Context,
            stream = false,
            options = BuildOptions(device)
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
            UsedDevice = device
        };
    }

    public async Task UnloadAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_currentModel)) return;

        // Відправляємо Ollama команду вивантаження: keep_alive=0
        var payload = new { model = _currentModel, keep_alive = 0 };
        try
        {
            await _http.PostAsJsonAsync("/api/generate", payload, cancellationToken);
        }
        catch { /* Ігноруємо якщо сервер недоступний */ }

        _currentModel = string.Empty;
        _idleTimer?.Dispose();
        _idleTimer = null;
    }

    private void ResetIdleTimer()
    {
        _idleTimer?.Dispose();
        _idleTimer = new Timer(
            _ => _ = UnloadAsync(),
            null,
            (long)_idleTimeout.TotalMilliseconds,
            Timeout.Infinite);
    }

    private static object BuildOptions(ExecutionDevice device) => device switch
    {
        ExecutionDevice.GPU_Nvidia => new { num_gpu = 99 },
        ExecutionDevice.GPU_AMD   => new { num_gpu = 99 },
        _                         => new { num_gpu = 0 }
    };

    public void Dispose()
    {
        _idleTimer?.Dispose();
        _http.Dispose();
    }

    private sealed class OllamaGenerateResponse
    {
        public string Response { get; set; } = string.Empty;
        public int EvalCount { get; set; }
    }
}
