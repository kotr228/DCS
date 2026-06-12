using System.Text.Json;
using AsmodayCat.Shared.Interfaces;
using AsmodayCat.Network.BlackCatBridge;

namespace AsmodayCat.Agent.Tools.Web;

// FR7.1: пошуковий інструмент через DuckDuckGo Instant Answer API
// Весь трафік через BlackCat (перевірка дозволу перед запитом)
public class WebSearchTool : IAgentTool
{
    private readonly HttpClient _http;
    private readonly IBlackCatSecurityFilter _security;

    public WebSearchTool(HttpClient http, IBlackCatSecurityFilter security)
    {
        _http = http;
        _security = security;
    }

    public string GetName() => "web_search";
    public string GetDescription() =>
        "Search the web for information. Args: {\"query\": \"search query\"}";

    public async Task<string> ExecuteAsync(string jsonArguments, CancellationToken cancellationToken = default)
    {
        var allowed = await _security.IsInternetAccessAllowedAsync("web_search", cancellationToken);
        if (!allowed)
            return JsonSerializer.Serialize(new { error = "Internet access blocked by BlackCat policy." });

        using var doc = JsonDocument.Parse(jsonArguments);
        var query = doc.RootElement.GetProperty("query").GetString() ?? string.Empty;

        var url = $"https://api.duckduckgo.com/?q={Uri.EscapeDataString(query)}&format=json&no_html=1";
        var json = await _http.GetStringAsync(url, cancellationToken);

        using var result = JsonDocument.Parse(json);
        var abstract_ = result.RootElement.TryGetProperty("Abstract", out var ab) ? ab.GetString() : string.Empty;
        var relatedTopics = result.RootElement.TryGetProperty("RelatedTopics", out var rt)
            ? rt.EnumerateArray()
                .Take(5)
                .Select(t => t.TryGetProperty("Text", out var txt) ? txt.GetString() : null)
                .Where(s => s != null)
                .ToList()
            : [];

        return JsonSerializer.Serialize(new
        {
            abstract_ = abstract_,
            related   = relatedTopics
        });
    }
}
