using System.Text.Json;
using AsmodayCat.Shared.Interfaces;
using AsmodayCat.Network.BlackCatBridge;

namespace AsmodayCat.Agent.Tools.System;

// FR7.3: генерація зображень через локальний Stable Diffusion WebUI / ComfyUI API
public class ImageGenerationTool : IAgentTool
{
    private readonly HttpClient _http;
    private readonly IBlackCatSecurityFilter _security;

    // Базовий URL локального SD WebUI (http://127.0.0.1:7860)
    private readonly string _sdBaseUrl;

    public ImageGenerationTool(HttpClient http, IBlackCatSecurityFilter security, string sdBaseUrl = "http://127.0.0.1:7860")
    {
        _http = http;
        _security = security;
        _sdBaseUrl = sdBaseUrl.TrimEnd('/');
    }

    public string GetName() => "image_generate";
    public string GetDescription() =>
        "Generate an image using Stable Diffusion. Args: {\"prompt\": \"...\", \"negative_prompt\": \"...\", \"output_path\": \"C:\\\\output\\\\image.png\", \"steps\": 20, \"width\": 512, \"height\": 512}";

    public async Task<string> ExecuteAsync(string jsonArguments, CancellationToken cancellationToken = default)
    {
        // Зовнішній SD API потребує дозволу BlackCat якщо він не localhost
        if (!_sdBaseUrl.Contains("127.0.0.1") && !_sdBaseUrl.Contains("localhost"))
        {
            var allowed = await _security.IsInternetAccessAllowedAsync("image_generation", cancellationToken);
            if (!allowed)
                return JsonSerializer.Serialize(new { error = "External SD API blocked by BlackCat." });
        }

        using var doc   = JsonDocument.Parse(jsonArguments);
        var prompt       = doc.RootElement.GetProperty("prompt").GetString() ?? string.Empty;
        var negPrompt    = doc.RootElement.TryGetProperty("negative_prompt", out var np) ? np.GetString() ?? "" : "";
        var outputPath   = doc.RootElement.TryGetProperty("output_path",     out var op) ? op.GetString() ?? "" : "";
        var steps        = doc.RootElement.TryGetProperty("steps",  out var st) ? st.GetInt32() : 20;
        var width        = doc.RootElement.TryGetProperty("width",  out var w)  ? w.GetInt32()  : 512;
        var height       = doc.RootElement.TryGetProperty("height", out var h)  ? h.GetInt32()  : 512;

        var payload = new
        {
            prompt,
            negative_prompt = negPrompt,
            steps,
            width,
            height,
            sampler_name    = "DPM++ 2M Karras"
        };

        var response = await _http.PostAsJsonAsync($"{_sdBaseUrl}/sdapi/v1/txt2img", payload, cancellationToken);
        response.EnsureSuccessStatusCode();

        using var resultDoc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var images = resultDoc.RootElement.GetProperty("images");

        if (images.GetArrayLength() == 0)
            return JsonSerializer.Serialize(new { error = "SD returned no images." });

        var base64 = images[0].GetString() ?? string.Empty;
        var imageBytes = Convert.FromBase64String(base64);

        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            var dir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            await File.WriteAllBytesAsync(outputPath, imageBytes, cancellationToken);
        }

        return JsonSerializer.Serialize(new
        {
            success     = true,
            output_path = outputPath,
            size_bytes  = imageBytes.Length
        });
    }
}
