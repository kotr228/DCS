using AsmodayCat.Shared.Text;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using AsmodayCat.Shared.Interfaces;
using AsmodayCat.Shared.Models;

namespace AsmodayCat.Core.Engine;

// FR8.1/FR8.2: перевірка локального реєстру Ollama та auto-pull відсутніх моделей
public class ModelRegistryClient : IModelRegistry
{
    private readonly HttpClient _http;

    public ModelRegistryClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<IReadOnlyList<string>> GetLocalModelsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var resp = await _http.GetFromJsonAsync<OllamaTagsResponse>("/api/tags", cancellationToken);
            return resp?.Models?.Select(m => m.Name).ToList().AsReadOnly()
                   ?? (IReadOnlyList<string>)Array.Empty<string>();
        }
        catch { return Array.Empty<string>(); }
    }

    public async Task<IReadOnlyList<string>> GetLoadedModelsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var resp = await _http.GetFromJsonAsync<OllamaTagsResponse>("/api/ps", cancellationToken);
            return resp?.Models?.Select(m => m.Name).ToList().AsReadOnly()
                   ?? (IReadOnlyList<string>)Array.Empty<string>();
        }
        catch { return Array.Empty<string>(); }
    }

    public async Task<IReadOnlyDictionary<string, long>> GetLoadedModelVramAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var resp = await _http.GetFromJsonAsync<OllamaTagsResponse>("/api/ps", cancellationToken);
            return resp?.Models?.ToDictionary(m => m.Name, m => m.SizeVram, StringComparer.OrdinalIgnoreCase)
                   ?? new Dictionary<string, long>();
        }
        catch { return new Dictionary<string, long>(); }
    }

    public async Task DeleteModelAsync(string modelId, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, "/api/delete")
        {
            Content = JsonContent.Create(new { model = modelId })
        };
        using var resp = await _http.SendAsync(request, cancellationToken);
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(L.T($"Не вдалося видалити {modelId}: {body}", $"Could not delete {modelId}: {body}", $"Не удалось удалить {modelId}: {body}"));
        }
    }

    public async Task<bool> IsModelAvailableAsync(string modelId, CancellationToken cancellationToken = default)
    {
        var models = await GetLocalModelsAsync(cancellationToken);
        return models.Any(m => m.StartsWith(modelId, StringComparison.OrdinalIgnoreCase));
    }

    // FR8.2: стрімінг прогресу pull через IProgress<T>
    public async Task PullModelAsync(string modelId, IProgress<ModelPullProgress> progress,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await PullCoreAsync(modelId, progress, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Report instead of throwing: pulls run fire-and-forget from the IPC server
            progress.Report(new ModelPullProgress { ModelId = modelId, Status = $"error: {ex.Message}" });
        }
    }

    private async Task PullCoreAsync(string modelId, IProgress<ModelPullProgress> progress,
        CancellationToken cancellationToken)
    {
        var payload = new { name = modelId, stream = true };

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/pull")
        {
            Content = JsonContent.Create(payload)
        };

        using var response = await _http.SendAsync(request,
            HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(await response.Content.ReadAsStringAsync(cancellationToken));

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(line)) continue;

            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;

                var pull = new ModelPullProgress
                {
                    ModelId        = modelId,
                    Status         = root.TryGetProperty("status", out var s) ? s.GetString() ?? "" : "",
                    TotalBytes     = root.TryGetProperty("total",     out var t) ? t.GetInt64() : 0,
                    CompletedBytes = root.TryGetProperty("completed", out var c) ? c.GetInt64() : 0
                };

                progress.Report(pull);

                if (pull.Status == "success") break;
                if (root.TryGetProperty("error", out var err))
                    throw new InvalidOperationException(err.GetString());
            }
            catch (JsonException) { /* пропускаємо рядки що не парсяться */ }
        }

        // The loop also ends when cancelled: report that instead of finishing silently
        cancellationToken.ThrowIfCancellationRequested();
    }

    private sealed class OllamaTagsResponse
    {
        public List<OllamaModel>? Models { get; set; }
    }

    private sealed class OllamaModel
    {
        public string Name { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("size_vram")]
        public long SizeVram { get; set; }
    }
}
