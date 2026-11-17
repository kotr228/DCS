using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AsmodayCat.Agent.Tools.Web;

// FR7.1: web search via the DuckDuckGo HTML results page (no API key needed).
public class WebSearchTool : IAgentTool
{
    private const int MaxResults      = 8;
    private const int PagesToRead     = 3;      // top results whose text is included
    private const int PageExcerptChars = 2500;

    private static readonly Regex ResultLink = new(
        @"<a\b(?<attrs>[^>]*class=""result__a""[^>]*)>(?<title>.*?)</a>",
        RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex Snippet = new(
        @"class=""result__snippet""[^>]*>(?<text>.*?)</(a|div|td)>",
        RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex Href = new(@"href=""(?<url>[^""]*)""", RegexOptions.Compiled);

    private readonly HttpClient _http      = WebHttp.CreateClient();
    private readonly HttpClient _pageHttp  = WebHttp.CreateClient(allowRedirects: false);

    public string Name => "web_search";

    public string Description =>
        "Search the internet. Returns titles, URLs, snippets and the main text of the top pages.";

    public object Parameters => new
    {
        type = "object",
        properties = new
        {
            query = new { type = "string", description = "Search query" }
        },
        required = new[] { "query" }
    };

    public bool IsEnabled(ToolContext context) => context.AllowInternet;

    public async Task<string> ExecuteAsync(JsonElement args, ToolContext ctx, CancellationToken ct)
    {
        var query = args.Str("query")?.Trim() ?? string.Empty;
        if (query.Length == 0) return ToolRegistry.Error("'query' is required.");

        using var content  = new FormUrlEncodedContent(new Dictionary<string, string> { ["q"] = query });
        using var response = await _http.PostAsync("https://html.duckduckgo.com/html/", content, ct);
        var html = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode || html.Contains("anomaly-modal", StringComparison.Ordinal))
            return ToolRegistry.Error("Search engine refused the request (rate limit). Try again later.");

        var results = Parse(html);
        if (results.Count == 0)
            return ToolJson.Serialize(new { query, results, note = "No results." });

        // Small models rarely call web_fetch on their own, so read the top pages here
        var excerpts = await Task.WhenAll(results.Take(PagesToRead).Select(r => ReadExcerptAsync(r.Url, ct)));
        var output = results.Select((r, i) => new
        {
            title   = r.Title,
            url     = r.Url,
            snippet = r.Snippet,
            content = i < excerpts.Length ? excerpts[i] : null
        });

        return ToolJson.Serialize(new { query, results = output });
    }

    // Main text of a page: paragraph-like lines first, so menus and footers don't fill the excerpt.
    private async Task<string?> ReadExcerptAsync(string url, CancellationToken ct)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            var page = await WebScraperTool.FetchTextAsync(_pageHttp, uri, cts.Token);
            if (page.Text is null) return null;

            var paragraphs = page.Text.Split('\n').Select(l => l.Trim()).Where(l => l.Length >= 60);
            var text = string.Join("\n", paragraphs);
            if (text.Length < 300) text = page.Text;
            return text.Length > PageExcerptChars ? text[..PageExcerptChars] + "…" : text;
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is OperationCanceledException && !ct.IsCancellationRequested))
        {
            return null;
        }
    }

    internal static List<SearchResult> Parse(string html)
    {
        var links   = ResultLink.Matches(html);
        var results = new List<SearchResult>();

        for (var i = 0; i < links.Count && results.Count < MaxResults; i++)
        {
            var link = links[i];
            var href = Href.Match(link.Groups["attrs"].Value).Groups["url"].Value;
            var url  = DecodeUrl(href);
            if (url is null) continue;   // ads and internal links

            // The snippet sits between this link and the next one
            var end     = i + 1 < links.Count ? links[i + 1].Index : html.Length;
            var segment = html[(link.Index + link.Length)..end];
            var snippet = Snippet.Match(segment) is { Success: true } m ? WebHttp.CleanInline(m.Groups["text"].Value) : "";

            results.Add(new SearchResult(WebHttp.CleanInline(link.Groups["title"].Value), url, snippet));
        }
        return results;
    }

    private static string? DecodeUrl(string href)
    {
        href = WebUtility.HtmlDecode(href);
        if (href.StartsWith("//")) href = "https:" + href;

        if (!Uri.TryCreate(href, UriKind.Absolute, out var uri)) return null;

        if (uri.Host.EndsWith("duckduckgo.com", StringComparison.OrdinalIgnoreCase))
        {
            // Redirect links: //duckduckgo.com/l/?uddg=<encoded target>; y.js links are ads
            var match = Regex.Match(uri.Query, @"[?&]uddg=([^&]+)");
            return match.Success ? Uri.UnescapeDataString(match.Groups[1].Value) : null;
        }
        return uri.ToString();
    }

    internal sealed record SearchResult(string Title, string Url, string Snippet);
}
