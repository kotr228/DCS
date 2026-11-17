using AsmodayCat.Shared.Text;
using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AsmodayCat.Core.Hardware;
using AsmodayCat.Shared.Enums;
using AsmodayCat.Shared.Interfaces;
using AsmodayCat.Shared.Models;

namespace AsmodayCat.Core.Engine;

// Ollama keeps the model in memory for KeepAlive after the last request and then
// unloads it by itself (FR1.3), so no local idle timer is needed.
public class OllamaClient : ILLMEngine
{
    public static readonly TimeSpan DefaultIdleTimeout = TimeSpan.FromMinutes(10);

    private readonly HttpClient  _http;
    private readonly GpuMonitor? _gpu;
    private readonly Dictionary<string, (int Layers, long SizeBytes)> _modelInfo = new(StringComparer.OrdinalIgnoreCase);
    private readonly string     _keepAlive;
    private volatile string     _defaultModel = string.Empty;
    private volatile string     _lastUsedModel = string.Empty;
    private int[]?  _context;   // legacy /api/generate conversation context
    private HardwareConfigDto _hwConfig = new();

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public OllamaClient(HttpClient http, GpuMonitor? gpu = null, TimeSpan? idleTimeout = null)
    {
        _http      = http;
        _gpu       = gpu;
        _keepAlive = $"{(int)(idleTimeout ?? DefaultIdleTimeout).TotalSeconds}s";
    }

    public Task LoadModelAsync(string modelPath, CancellationToken cancellationToken = default)
    {
        _defaultModel = modelPath;
        return Task.CompletedTask;
    }

    private string ResolveModel(string? requested)
    {
        var model = !string.IsNullOrWhiteSpace(requested) ? requested : _defaultModel;
        if (string.IsNullOrWhiteSpace(model))
            throw new InvalidOperationException(L.T("Модель не вибрано. Встановіть модель на вкладці «Моделі».", "No model selected. Install a model on the Models tab.", "Модель не выбрана. Установите модель на вкладке «Модели»."));
        _lastUsedModel = model;
        return model;
    }

    public async Task<LlmResponse> GenerateAsync(LlmRequest request, CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            model      = ResolveModel(request.Model),
            prompt     = request.Prompt,
            system     = request.Context,
            stream     = false,
            keep_alive = _keepAlive,
            options    = await BuildOptionsAsync(request.Model ?? _defaultModel, request.ExecutionDevice, null, cancellationToken)
        };

        var started  = DateTime.UtcNow;
        var response = await _http.PostAsJsonAsync("/api/generate", payload, cancellationToken);
        await EnsureSuccessAsync(response, payload.model, cancellationToken);

        var result = await response.Content.ReadFromJsonAsync<OllamaGenerateResponse>(
            cancellationToken: cancellationToken);

        var elapsed = (DateTime.UtcNow - started).TotalSeconds;
        var tokens  = result?.EvalCount ?? 0;

        return new LlmResponse
        {
            Content         = result?.Response ?? string.Empty,
            TokensPerSecond = elapsed > 0 ? tokens / elapsed : 0,
            UsedDevice      = request.ExecutionDevice
        };
    }

    public async IAsyncEnumerable<string> GenerateStreamAsync(
        LlmRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var images = await EncodeImagesAsync(request.ImagePaths, cancellationToken);

        var payload = new OllamaGenerateRequest
        {
            Model     = ResolveModel(request.Model),
            Prompt    = request.Prompt,
            System    = request.Context.Length > 0 ? request.Context : null,
            Context   = _context,
            Images    = images.Count > 0 ? images : null,
            Stream    = true,
            KeepAlive = _keepAlive,
            Options   = await BuildOptionsAsync(request.Model ?? _defaultModel, request.ExecutionDevice, null, cancellationToken)
        };

        using var resp = await PostStreamAsync("/api/generate", payload, payload.Model, cancellationToken);
        await using var stream = await resp.Content.ReadAsStreamAsync(cancellationToken);
        using var reader       = new StreamReader(stream);

        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (string.IsNullOrEmpty(line)) continue;

            var chunk = JsonSerializer.Deserialize<OllamaStreamChunk>(line);
            if (chunk is null) continue;
            if (!string.IsNullOrEmpty(chunk.Error)) throw new InvalidOperationException(chunk.Error);

            if (!string.IsNullOrEmpty(chunk.Response))
                yield return chunk.Response;

            if (chunk.Done)
            {
                _context = chunk.Context;
                yield break;
            }
        }
    }

    // /api/chat with optional tools. Tool calls arrive inside the streamed message.
    public async IAsyncEnumerable<LlmChatChunk> ChatStreamAsync(
        LlmChatRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var model = ResolveModel(request.Model);

        var payload = new OllamaChatRequest
        {
            Model     = model,
            Stream    = true,
            KeepAlive = _keepAlive,
            Options   = await BuildOptionsAsync(model, request.ExecutionDevice, request.ContextTokens, cancellationToken),
            Messages  = request.Messages.Select(ToOllama).ToList(),
            Tools     = request.Tools is { Count: > 0 }
                ? request.Tools.Select(t => new OllamaTool
                  {
                      Function = new OllamaToolFunction
                      {
                          Name        = t.Name,
                          Description = t.Description,
                          Parameters  = t.Parameters
                      }
                  }).ToList()
                : null
        };

        using var resp = await PostStreamAsync("/api/chat", payload, model, cancellationToken);
        await using var stream = await resp.Content.ReadAsStreamAsync(cancellationToken);
        using var reader       = new StreamReader(stream);

        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (string.IsNullOrEmpty(line)) continue;

            var chunk = JsonSerializer.Deserialize<OllamaChatChunk>(line);
            if (chunk is null) continue;
            if (!string.IsNullOrEmpty(chunk.Error)) throw new InvalidOperationException(chunk.Error);

            var calls = chunk.Message?.ToolCalls?
                .Where(c => c.Function is not null)
                .Select(c => new LlmToolCall
                {
                    Name      = c.Function!.Name,
                    Arguments = NormalizeArguments(c.Function.Arguments)
                })
                .ToList();

            var tps = chunk.Done && chunk.EvalDuration > 0
                ? chunk.EvalCount / (chunk.EvalDuration / 1_000_000_000.0)
                : 0;

            yield return new LlmChatChunk
            {
                Content         = chunk.Message?.Content ?? string.Empty,
                ToolCalls       = calls is { Count: > 0 } ? calls : null,
                Done            = chunk.Done,
                TokensPerSecond = tps
            };

            if (chunk.Done) yield break;
        }
    }

    public void ClearContext() => _context = null;

    public void Configure(HardwareConfigDto config) => _hwConfig = config;

    public async Task UnloadAsync(CancellationToken cancellationToken = default)
    {
        var model = _lastUsedModel;
        if (string.IsNullOrEmpty(model)) return;
        await UnloadModelAsync(model, cancellationToken);
        _context = null;
    }

    public async Task UnloadModelAsync(string model, CancellationToken cancellationToken = default)
    {
        try
        {
            await _http.PostAsJsonAsync("/api/generate", new { model, keep_alive = 0 }, cancellationToken);
        }
        catch (HttpRequestException) { /* server offline — nothing to unload */ }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task<HttpResponseMessage> PostStreamAsync(
        string path, object payload, string model, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(payload, payload.GetType(), _jsonOpts);
        using var httpReq = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        HttpResponseMessage resp;
        try
        {
            resp = await _http.SendAsync(httpReq, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException(
                L.T($"Не вдається з’єднатися з Ollama ({_http.BaseAddress}). Чи вона запущена? ({ex.Message})",
                    $"Cannot connect to Ollama ({_http.BaseAddress}). Is it running? ({ex.Message})",
                    $"Не удаётся подключиться к Ollama ({_http.BaseAddress}). Она запущена? ({ex.Message})"), ex);
        }

        try
        {
            await EnsureSuccessAsync(resp, model, ct);
        }
        catch
        {
            resp.Dispose();
            throw;
        }
        return resp;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage resp, string model, CancellationToken ct)
    {
        if (resp.IsSuccessStatusCode) return;

        var body = await resp.Content.ReadAsStringAsync(ct);
        var error = body;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var e)) error = e.GetString() ?? body;
        }
        catch (JsonException) { }

        if (error.Contains("does not support tools", StringComparison.OrdinalIgnoreCase))
            throw new ToolsNotSupportedException(model);

        if (resp.StatusCode == HttpStatusCode.NotFound)
            throw new InvalidOperationException(L.T($"Модель «{model}» не встановлена. Встановіть її на вкладці «Моделі». ({error})",
                $"Model “{model}” is not installed. Install it on the Models tab. ({error})",
                $"Модель «{model}» не установлена. Установите её на вкладке «Модели». ({error})"));

        throw new InvalidOperationException(L.T($"Помилка Ollama {(int)resp.StatusCode}: {error}", $"Ollama error {(int)resp.StatusCode}: {error}", $"Ошибка Ollama {(int)resp.StatusCode}: {error}"));
    }

    // Some models return arguments as a JSON string instead of an object.
    private static JsonElement NormalizeArguments(JsonElement args)
    {
        if (args.ValueKind == JsonValueKind.String)
        {
            try
            {
                using var doc = JsonDocument.Parse(args.GetString() ?? "{}");
                return doc.RootElement.Clone();
            }
            catch (JsonException) { }
        }
        if (args.ValueKind == JsonValueKind.Object) return args.Clone();

        using var empty = JsonDocument.Parse("{}");
        return empty.RootElement.Clone();
    }

    private static OllamaChatMessage ToOllama(LlmChatMessage m) => new()
    {
        Role      = m.Role,
        Content   = m.Content,
        Images    = m.Images is { Count: > 0 } ? m.Images : null,
        ToolName  = m.ToolName,
        ToolCalls = m.ToolCalls?.Select(c => new OllamaToolCall
        {
            Function = new OllamaToolCallFunction { Name = c.Name, Arguments = c.Arguments }
        }).ToList()
    };

    // Upper limit for automatic growth of the context window
    public const int MaxAutoContextTokens = 16384;

    private async Task<object> BuildOptionsAsync(string model, ExecutionDevice device, int? neededTokens, CancellationToken ct)
    {
        var numGpu = device switch
        {
            ExecutionDevice.GPU_Nvidia => 99,
            ExecutionDevice.GPU_AMD    => 99,
            _                          => (int?)null   // let Ollama decide
        };

        // Override with HardwareConfig preference
        if (_hwConfig.PreferredDevice == "GPU") numGpu = 99;
        if (_hwConfig.PreferredDevice == "CPU") numGpu = 0;

        // The Hardware setting is the baseline; long conversations, web pages or files get
        // a bigger window (up to MaxAutoContextTokens) instead of being cut off silently.
        var numCtx = _hwConfig.ContextWindowSize > 0 ? _hwConfig.ContextWindowSize : 4096;
        if (neededTokens is { } need && need > numCtx)
            numCtx = Math.Max(numCtx, Math.Min((need + 4095) / 4096 * 4096, MaxAutoContextTokens));

        if (numGpu != 0 && !string.IsNullOrEmpty(model))
            numGpu = await FitToVramAsync(model, numCtx, numGpu, ct);

        return new OllamaOptions
        {
            NumGpu    = numGpu,
            NumCtx    = numCtx,
            NumThread = _hwConfig.CpuThreads        > 0 ? _hwConfig.CpuThreads        : null
        };
    }

    // Applies "Max VRAM" and "Allow CPU fallback": when the model doesn't fit the VRAM budget,
    // put only the layers that fit on the GPU (the rest run on the CPU) — or refuse if CPU fallback is off.
    private async Task<int?> FitToVramAsync(string model, int numCtx, int? numGpu, CancellationToken ct)
    {
        var limitMb  = _hwConfig.MaxVramAllocationMb;
        var gpuKnown = _gpu?.IsAvailable == true;
        if (!gpuKnown && limitMb <= 0) return numGpu;

        var info = await GetModelInfoAsync(model, ct);
        if (info is null || info.Value.Layers <= 0 || info.Value.SizeBytes <= 0) return numGpu;

        // Weights + KV cache (roughly 60 MB per 1K context for a 7B model)
        var needMb = (long)(info.Value.SizeBytes / 1024 / 1024 * 1.1) + numCtx / 1024 * 60;

        // Free VRAM plus what Ollama's loaded models hold: Ollama unloads them to make room
        var budgetMb = long.MaxValue;
        if (gpuKnown)
            budgetMb = _gpu!.VramFreeMb + await GetOllamaVramMbAsync(ct);
        if (limitMb > 0)
            budgetMb = Math.Min(budgetMb, limitMb);

        if (needMb <= budgetMb) return numGpu;

        if (!_hwConfig.AllowCpuFallback)
            throw new InvalidOperationException(
                L.T($"Модель «{model}» потребує ~{needMb} МБ відеопам’яті, а доступно {budgetMb} МБ. Роботу на процесорі вимкнено в Налаштуваннях — увімкніть її або виберіть меншу модель.",
                    $"Model “{model}” needs ~{needMb} MB of video memory, but {budgetMb} MB is available. Running on the processor is turned off in Settings — turn it on or choose a smaller model.",
                    $"Модели «{model}» нужно ~{needMb} МБ видеопамяти, а доступно {budgetMb} МБ. Работа на процессоре выключена в Настройках — включите её или выберите модель поменьше."));

        var layers = (int)Math.Floor(info.Value.Layers * (double)Math.Max(0, budgetMb) / needMb);
        return Math.Clamp(layers, 0, info.Value.Layers);
    }

    private async Task<(int Layers, long SizeBytes)?> GetModelInfoAsync(string model, CancellationToken ct)
    {
        lock (_modelInfo)
            if (_modelInfo.TryGetValue(model, out var cached)) return cached;
        try
        {
            using var show = await _http.PostAsJsonAsync("/api/show", new { model }, ct);
            if (!show.IsSuccessStatusCode) return null;
            using var showDoc = JsonDocument.Parse(await show.Content.ReadAsStringAsync(ct));
            var layers = 0;
            if (showDoc.RootElement.TryGetProperty("model_info", out var mi))
                foreach (var p in mi.EnumerateObject())
                    if (p.Name.EndsWith(".block_count") && p.Value.TryGetInt32(out var n)) { layers = n; break; }

            using var tagsDoc = JsonDocument.Parse(await _http.GetStringAsync("/api/tags", ct));
            long size = 0;
            foreach (var m in tagsDoc.RootElement.GetProperty("models").EnumerateArray())
                if (string.Equals(m.GetProperty("name").GetString(), model, StringComparison.OrdinalIgnoreCase))
                    size = m.GetProperty("size").GetInt64();

            var info = (layers, size);
            lock (_modelInfo) _modelInfo[model] = info;
            return info;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or KeyNotFoundException)
        {
            return null;
        }
    }

    private async Task<long> GetOllamaVramMbAsync(CancellationToken ct)
    {
        try
        {
            using var doc = JsonDocument.Parse(await _http.GetStringAsync("/api/ps", ct));
            return doc.RootElement.GetProperty("models").EnumerateArray()
                .Sum(m => m.TryGetProperty("size_vram", out var v) ? v.GetInt64() : 0) / 1024 / 1024;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or KeyNotFoundException)
        {
            return 0;
        }
    }

    public static async Task<List<string>> EncodeImagesAsync(
        IEnumerable<string> paths, CancellationToken ct)
    {
        var result = new List<string>();
        foreach (var path in paths)
        {
            if (!File.Exists(path)) continue;
            var bytes = await File.ReadAllBytesAsync(path, ct);
            result.Add(Convert.ToBase64String(bytes));
        }
        return result;
    }

    // ── Private Ollama API types ─────────────────────────────────────────────

    private sealed class OllamaOptions
    {
        [JsonPropertyName("num_gpu")]    public int? NumGpu    { get; set; }
        [JsonPropertyName("num_ctx")]    public int  NumCtx    { get; set; }
        [JsonPropertyName("num_thread")] public int? NumThread { get; set; }
    }

    private sealed class OllamaGenerateRequest
    {
        [JsonPropertyName("model")]      public string         Model     { get; set; } = string.Empty;
        [JsonPropertyName("prompt")]     public string         Prompt    { get; set; } = string.Empty;
        [JsonPropertyName("system")]     public string?        System    { get; set; }
        [JsonPropertyName("context")]    public int[]?         Context   { get; set; }
        [JsonPropertyName("images")]     public List<string>?  Images    { get; set; }
        [JsonPropertyName("stream")]     public bool           Stream    { get; set; }
        [JsonPropertyName("keep_alive")] public string?        KeepAlive { get; set; }
        [JsonPropertyName("options")]    public object?        Options   { get; set; }
    }

    private sealed class OllamaGenerateResponse
    {
        [JsonPropertyName("response")]   public string Response  { get; set; } = string.Empty;
        [JsonPropertyName("eval_count")] public int    EvalCount { get; set; }
    }

    private sealed class OllamaStreamChunk
    {
        [JsonPropertyName("response")] public string  Response { get; set; } = string.Empty;
        [JsonPropertyName("done")]     public bool    Done     { get; set; }
        [JsonPropertyName("context")]  public int[]?  Context  { get; set; }
        [JsonPropertyName("error")]    public string? Error    { get; set; }
    }

    private sealed class OllamaChatRequest
    {
        [JsonPropertyName("model")]      public string                  Model     { get; set; } = string.Empty;
        [JsonPropertyName("messages")]   public List<OllamaChatMessage> Messages  { get; set; } = [];
        [JsonPropertyName("tools")]      public List<OllamaTool>?       Tools     { get; set; }
        [JsonPropertyName("stream")]     public bool                    Stream    { get; set; }
        [JsonPropertyName("keep_alive")] public string?                 KeepAlive { get; set; }
        [JsonPropertyName("options")]    public object?                 Options   { get; set; }
    }

    private sealed class OllamaChatMessage
    {
        [JsonPropertyName("role")]       public string                 Role      { get; set; } = string.Empty;
        [JsonPropertyName("content")]    public string                 Content   { get; set; } = string.Empty;
        [JsonPropertyName("images")]     public List<string>?          Images    { get; set; }
        [JsonPropertyName("tool_calls")] public List<OllamaToolCall>?  ToolCalls { get; set; }
        [JsonPropertyName("tool_name")]  public string?                ToolName  { get; set; }
    }

    private sealed class OllamaTool
    {
        [JsonPropertyName("type")]     public string             Type     { get; set; } = "function";
        [JsonPropertyName("function")] public OllamaToolFunction Function { get; set; } = new();
    }

    private sealed class OllamaToolFunction
    {
        [JsonPropertyName("name")]        public string Name        { get; set; } = string.Empty;
        [JsonPropertyName("description")] public string Description { get; set; } = string.Empty;
        [JsonPropertyName("parameters")]  public object Parameters  { get; set; } = new();
    }

    private sealed class OllamaToolCall
    {
        [JsonPropertyName("function")] public OllamaToolCallFunction? Function { get; set; }
    }

    private sealed class OllamaToolCallFunction
    {
        [JsonPropertyName("name")]      public string      Name      { get; set; } = string.Empty;
        [JsonPropertyName("arguments")] public JsonElement Arguments { get; set; }
    }

    private sealed class OllamaChatChunk
    {
        [JsonPropertyName("message")]       public OllamaChatMessage? Message      { get; set; }
        [JsonPropertyName("done")]          public bool               Done         { get; set; }
        [JsonPropertyName("eval_count")]    public long               EvalCount    { get; set; }
        [JsonPropertyName("eval_duration")] public long               EvalDuration { get; set; }
        [JsonPropertyName("error")]         public string?            Error        { get; set; }
    }
}
