using System.Net.Http.Json;
using System.Text.Json;

namespace AsmodayCat.Agent.Tools.System;

// FR7.3: image generation through a local Stable Diffusion WebUI (AUTOMATIC1111 / Forge API).
// Offered to the model only while the WebUI answers.
public class ImageGenerationTool : IAgentTool
{
    private static readonly TimeSpan AvailabilityCacheTime = TimeSpan.FromSeconds(60);

    private readonly HttpClient _http;
    private readonly string     _baseUrl;

    private bool     _available;
    private DateTime _checkedAt = DateTime.MinValue;

    public ImageGenerationTool(string baseUrl)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _http    = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
    }

    public string Name => "image_generate";

    public string Description =>
        "Generate an image with Stable Diffusion from an English text prompt and save it as PNG. Returns the file path.";

    public object Parameters => new
    {
        type = "object",
        properties = new
        {
            prompt          = new { type = "string",  description = "What to draw, in English" },
            negative_prompt = new { type = "string",  description = "What to avoid (optional)" },
            file_name       = new { type = "string",  description = "Output file name, e.g. cat.png (optional)" },
            width           = new { type = "integer", description = "Default 512" },
            height          = new { type = "integer", description = "Default 512" },
            steps           = new { type = "integer", description = "Default 20" }
        },
        required = new[] { "prompt" }
    };

    public bool IsEnabled(ToolContext context) => context.AllowImages;

    public async Task<bool> IsAvailableAsync(CancellationToken ct)
    {
        if (DateTime.UtcNow - _checkedAt < AvailabilityCacheTime) return _available;

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(2));
            using var resp = await _http.GetAsync($"{_baseUrl}/sdapi/v1/sd-models", cts.Token);
            _available = resp.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            ct.ThrowIfCancellationRequested();
            _available = false;
        }

        _checkedAt = DateTime.UtcNow;
        return _available;
    }

    public async Task<string> ExecuteAsync(JsonElement args, ToolContext ctx, CancellationToken ct)
    {
        var prompt = args.Str("prompt") ?? string.Empty;
        if (prompt.Length == 0) return ToolRegistry.Error("'prompt' is required.");

        var payload = new
        {
            prompt,
            negative_prompt = args.Str("negative_prompt") ?? string.Empty,
            steps           = Math.Clamp(args.Int("steps", 20), 1, 80),
            width           = Math.Clamp(args.Int("width", 512), 64, 2048),
            height          = Math.Clamp(args.Int("height", 512), 64, 2048),
            sampler_name    = "DPM++ 2M Karras"
        };

        using var response = await _http.PostAsJsonAsync($"{_baseUrl}/sdapi/v1/txt2img", payload, ct);
        if (!response.IsSuccessStatusCode)
            return ToolRegistry.Error($"Stable Diffusion error {(int)response.StatusCode}");

        using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        if (!result.RootElement.TryGetProperty("images", out var images) || images.GetArrayLength() == 0)
            return ToolRegistry.Error("Stable Diffusion returned no images.");

        var bytes = Convert.FromBase64String(images[0].GetString() ?? string.Empty);

        var name = SafeFileName(args.Str("file_name"));
        Directory.CreateDirectory(ctx.ImageOutputDirectory);
        var path = Path.Combine(ctx.ImageOutputDirectory, name);
        await File.WriteAllBytesAsync(path, bytes, ct);
        ctx.RecordAction($"Generated image {path} (prompt: {(prompt.Length > 100 ? prompt[..100] + "…" : prompt)})");

        return ToolJson.Serialize(new { success = true, path, size_bytes = bytes.Length });
    }

    private static string SafeFileName(string? requested)
    {
        var name = string.IsNullOrWhiteSpace(requested)
            ? $"image_{DateTime.Now:yyyyMMdd_HHmmss}.png"
            : Path.GetFileName(requested.Trim());

        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        if (!name.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) name += ".png";
        return name;
    }
}
