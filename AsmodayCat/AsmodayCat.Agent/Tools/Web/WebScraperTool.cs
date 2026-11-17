using System.Text;
using System.Text.Json;

namespace AsmodayCat.Agent.Tools.Web;

// FR7.1: download a public web page and return its readable text.
public class WebScraperTool : IAgentTool
{
    private const int MaxDownloadBytes = 3 * 1024 * 1024;
    private const int MaxTextChars     = 8000;
    private const int MaxRedirects     = 5;

    private readonly HttpClient _http = WebHttp.CreateClient(allowRedirects: false);

    public string Name => "web_fetch";

    public string Description => "Download a web page (http/https) and return its text content.";

    public object Parameters => new
    {
        type = "object",
        properties = new
        {
            url = new { type = "string", description = "Full URL starting with http:// or https://" }
        },
        required = new[] { "url" }
    };

    public bool IsEnabled(ToolContext context) => context.AllowInternet;

    public async Task<string> ExecuteAsync(JsonElement args, ToolContext ctx, CancellationToken ct)
    {
        if (!Uri.TryCreate(args.Str("url")?.Trim(), UriKind.Absolute, out var uri))
            return ToolRegistry.Error("'url' must be an absolute http(s) URL.");

        var page = await FetchTextAsync(_http, uri, ct);
        if (page.Error is not null) return ToolRegistry.Error(page.Error);

        var text      = page.Text!;
        var truncated = text.Length > MaxTextChars;
        if (truncated) text = text[..MaxTextChars];

        return ToolJson.Serialize(new { url = page.Url.ToString(), content = text, truncated });
    }

    internal sealed record PageText(Uri Url, string? Text, string? Error);

    // Downloads a public page and returns its readable text. `http` must not follow redirects
    // by itself: every hop is checked against private addresses here.
    internal static async Task<PageText> FetchTextAsync(HttpClient http, Uri uri, CancellationToken ct)
    {
        HttpResponseMessage? response = null;
        try
        {
            for (var hop = 0; ; hop++)
            {
                if (await WebHttp.CheckPublicUrlAsync(uri, ct) is { } problem)
                    return new PageText(uri, null, problem);

                response?.Dispose();
                response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);

                var code = (int)response.StatusCode;
                if (code is >= 300 and < 400 && response.Headers.Location is { } location)
                {
                    if (hop >= MaxRedirects) return new PageText(uri, null, "Too many redirects.");
                    uri = location.IsAbsoluteUri ? location : new Uri(uri, location);
                    continue;
                }
                break;
            }

            if (!response.IsSuccessStatusCode)
                return new PageText(uri, null, $"HTTP {(int)response.StatusCode} for {uri}");

            var mediaType = response.Content.Headers.ContentType?.MediaType ?? "text/html";
            var isHtml    = mediaType.Contains("html", StringComparison.OrdinalIgnoreCase);
            var isText    = isHtml || mediaType.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
                            || mediaType.Contains("json") || mediaType.Contains("xml");
            if (!isText)
                return new PageText(uri, null, $"Unsupported content type '{mediaType}'. Only web pages and text can be read.");

            var raw = await ReadLimitedAsync(response, ct);
            return new PageText(uri, isHtml ? WebHttp.HtmlToText(raw) : raw, null);
        }
        finally
        {
            response?.Dispose();
        }
    }

    private static async Task<string> ReadLimitedAsync(HttpResponseMessage response, CancellationToken ct)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        var chunk = new byte[64 * 1024];
        int n;
        while ((n = await stream.ReadAsync(chunk, ct)) > 0 && buffer.Length < MaxDownloadBytes)
            buffer.Write(chunk, 0, n);

        var charset = response.Content.Headers.ContentType?.CharSet?.Trim('"');
        Encoding encoding;
        try { encoding = string.IsNullOrEmpty(charset) ? Encoding.UTF8 : Encoding.GetEncoding(charset); }
        catch (ArgumentException) { encoding = Encoding.UTF8; }

        return encoding.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }
}
