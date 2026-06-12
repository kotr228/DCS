using System.Text.Json;
using System.Text.RegularExpressions;
using AsmodayCat.Shared.Interfaces;
using AsmodayCat.Network.BlackCatBridge;

namespace AsmodayCat.Agent.Tools.Web;

// FR7.1: завантажує URL, знімає HTML-теги, повертає чистий текст
public class WebScraperTool : IAgentTool
{
    private static readonly Regex HtmlTagRegex    = new(@"<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex WhitespaceRegex = new(@"\s{2,}", RegexOptions.Compiled);

    private readonly HttpClient _http;
    private readonly IBlackCatSecurityFilter _security;

    public WebScraperTool(HttpClient http, IBlackCatSecurityFilter security)
    {
        _http = http;
        _security = security;
    }

    public string GetName() => "web_scrape";
    public string GetDescription() =>
        "Fetch and read a webpage as plain text. Args: {\"url\": \"https://...\"}";

    public async Task<string> ExecuteAsync(string jsonArguments, CancellationToken cancellationToken = default)
    {
        var allowed = await _security.IsInternetAccessAllowedAsync("web_scrape", cancellationToken);
        if (!allowed)
            return JsonSerializer.Serialize(new { error = "Internet access blocked by BlackCat policy." });

        using var doc = JsonDocument.Parse(jsonArguments);
        var url = doc.RootElement.GetProperty("url").GetString() ?? string.Empty;

        var html = await _http.GetStringAsync(url, cancellationToken);

        // Видаляємо script/style блоки перед strip'ом тегів
        var noScript = Regex.Replace(html, @"<(script|style)[^>]*>.*?</(script|style)>",
            string.Empty, RegexOptions.Singleline | RegexOptions.IgnoreCase);
        var plain = HtmlTagRegex.Replace(noScript, " ");
        var clean = WhitespaceRegex.Replace(plain, " ").Trim();

        // Ліміт 8000 символів щоб не переповнити контекст LLM
        if (clean.Length > 8000) clean = clean[..8000] + "... [truncated]";

        return JsonSerializer.Serialize(new { content = clean });
    }
}
