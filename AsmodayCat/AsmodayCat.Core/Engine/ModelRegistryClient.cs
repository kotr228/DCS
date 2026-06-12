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

    public async Task<bool> IsModelAvailableAsync(string modelId, CancellationToken cancellationToken = default)
    {
        var models = await GetLocalModelsAsync(cancellationToken);
        return models.Any(m => m.StartsWith(modelId, StringComparison.OrdinalIgnoreCase));
    }

    // FR8.2: стрімінг прогресу pull через IProgress<T>
    public async Task PullModelAsync(string modelId, IProgress<ModelPullProgress> progress,
        CancellationToken cancellationToken = default)
    {
        var payload = new { name = modelId, stream = true };

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/pull")
        {
            Content = JsonContent.Create(payload)
        };

        using var response = await _http.SendAsync(request,
            HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

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
            }
            catch { /* пропускаємо рядки що не парсяться */ }
        }
    }

    private sealed class OllamaTagsResponse
    {
        public List<OllamaModel>? Models { get; set; }
    }

    private sealed class OllamaModel
    {
        public string Name { get; set; } = string.Empty;
    }
}
