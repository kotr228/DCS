using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AsmodayCat.Shared.Enums;
using AsmodayCat.Shared.Interfaces;
using AsmodayCat.Shared.Models;

namespace AsmodayCat.Core.Engine;

public class OllamaClient : ILLMEngine, IDisposable
{
    public static readonly TimeSpan DefaultIdleTimeout = TimeSpan.FromMinutes(10);

    private readonly HttpClient _http;
    private string  _currentModel  = string.Empty;
    private DateTime _lastUsed     = DateTime.MinValue;
    private Timer?  _idleTimer;
    private readonly TimeSpan _idleTimeout;
    private int[]?  _context;   // Ollama conversation context for multi-turn chat

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public OllamaClient(HttpClient http, TimeSpan? idleTimeout = null)
    {
        _http        = http;
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

        var payload = new
        {
            model   = _currentModel,
            prompt  = request.Prompt,
            system  = request.Context,
            stream  = false,
            options = BuildOptions(request.ExecutionDevice)
        };

        var started  = DateTime.UtcNow;
        var response = await _http.PostAsJsonAsync("/api/generate", payload, cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<OllamaGenerateResponse>(
            cancellationToken: cancellationToken);

        var elapsed = (DateTime.UtcNow - started).TotalSeconds;
        var tokens  = result?.EvalCount ?? 0;

        return new LlmResponse
        {
            Content        = result?.Response ?? string.Empty,
            TokensPerSecond = elapsed > 0 ? tokens / elapsed : 0,
            UsedDevice     = request.ExecutionDevice
        };
    }

    public async IAsyncEnumerable<string> GenerateStreamAsync(
        LlmRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _lastUsed = DateTime.UtcNow;
        ResetIdleTimer();

        var images = await EncodeImagesAsync(request.ImagePaths, cancellationToken);

        var payload = new OllamaGenerateRequest
        {
            Model   = _currentModel,
            Prompt  = request.Prompt,
            System  = request.Context.Length > 0 ? request.Context : null,
            Context = _context,
            Images  = images.Count > 0 ? images : null,
            Stream  = true,
            Options = BuildOptions(request.ExecutionDevice)
        };

        var json    = JsonSerializer.Serialize(payload, _jsonOpts);
        using var httpContent = new StringContent(json, Encoding.UTF8, "application/json");
        using var httpReq     = new HttpRequestMessage(HttpMethod.Post, "/api/generate") { Content = httpContent };
        using var resp        = await _http.SendAsync(httpReq, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        resp.EnsureSuccessStatusCode();

        await using var stream = await resp.Content.ReadAsStreamAsync(cancellationToken);
        using var reader       = new StreamReader(stream);

        while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrEmpty(line)) continue;

            var chunk = JsonSerializer.Deserialize<OllamaStreamChunk>(line);
            if (chunk is null) continue;

            if (!string.IsNullOrEmpty(chunk.Response))
                yield return chunk.Response;

            if (chunk.Done)
            {
                _context = chunk.Context;
                yield break;
            }
        }
    }

    public void ClearContext() => _context = null;

    public async Task UnloadAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_currentModel)) return;

        var payload = new { model = _currentModel, keep_alive = 0 };
        try
        {
            await _http.PostAsJsonAsync("/api/generate", payload, cancellationToken);
        }
        catch { /* ignore if server offline */ }

        _currentModel = string.Empty;
        _context      = null;
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

    private static async Task<List<string>> EncodeImagesAsync(
        List<string> paths, CancellationToken ct)
    {
        var result = new List<string>(paths.Count);
        foreach (var path in paths)
        {
            if (!File.Exists(path)) continue;
            var bytes = await File.ReadAllBytesAsync(path, ct);
            result.Add(Convert.ToBase64String(bytes));
        }
        return result;
    }

    public void Dispose()
    {
        _idleTimer?.Dispose();
        _http.Dispose();
    }

    // ── Private Ollama API types ─────────────────────────────────────────────

    private sealed class OllamaGenerateRequest
    {
        [JsonPropertyName("model")]   public string         Model   { get; set; } = string.Empty;
        [JsonPropertyName("prompt")]  public string         Prompt  { get; set; } = string.Empty;
        [JsonPropertyName("system")]  public string?        System  { get; set; }
        [JsonPropertyName("context")] public int[]?         Context { get; set; }
        [JsonPropertyName("images")]  public List<string>?  Images  { get; set; }
        [JsonPropertyName("stream")]  public bool           Stream  { get; set; }
        [JsonPropertyName("options")] public object?        Options { get; set; }
    }

    private sealed class OllamaGenerateResponse
    {
        public string Response  { get; set; } = string.Empty;
        public int    EvalCount { get; set; }
    }

    private sealed class OllamaStreamChunk
    {
        [JsonPropertyName("response")] public string  Response { get; set; } = string.Empty;
        [JsonPropertyName("done")]     public bool    Done     { get; set; }
        [JsonPropertyName("context")]  public int[]?  Context  { get; set; }
    }
}
