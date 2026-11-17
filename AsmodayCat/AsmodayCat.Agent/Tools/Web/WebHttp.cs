using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace AsmodayCat.Agent.Tools.Web;

internal static class WebHttp
{
    private const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36";

    private static readonly Regex DropBlocks = new(
        @"<(script|style|noscript|svg|head|template)\b[^>]*>.*?</\1\s*>",
        RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex Comments   = new(@"<!--.*?-->", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex BlockBreak = new(
        @"<(br|/p|/div|/li|/tr|/h[1-6]|/section|/article|/table|/ul|/ol)\b[^>]*>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Tags        = new(@"<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex Spaces      = new(@"[ \t ]+", RegexOptions.Compiled);
    private static readonly Regex BlankLines  = new(@"\n\s*\n+", RegexOptions.Compiled);

    public static HttpClient CreateClient(bool allowRedirects = true)
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect      = allowRedirects,
            AutomaticDecompression = DecompressionMethods.All
        };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(25) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("uk,en;q=0.8");
        return client;
    }

    public static string HtmlToText(string html)
    {
        var s = Comments.Replace(html, " ");
        s = DropBlocks.Replace(s, " ");
        s = BlockBreak.Replace(s, "\n");
        s = Tags.Replace(s, " ");
        s = WebUtility.HtmlDecode(s);
        s = Spaces.Replace(s, " ");
        s = string.Join('\n', s.Split('\n').Select(l => l.Trim()));
        return BlankLines.Replace(s, "\n\n").Trim();
    }

    public static string CleanInline(string html) =>
        Spaces.Replace(WebUtility.HtmlDecode(Tags.Replace(html, "")), " ").Trim();

    // Blocks requests to localhost and private networks so the model can't probe the LAN
    // or local services (Ollama, routers, admin panels).
    public static async Task<string?> CheckPublicUrlAsync(Uri uri, CancellationToken ct)
    {
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            return "Only http and https URLs are allowed.";

        IPAddress[] addresses;
        try
        {
            addresses = IPAddress.TryParse(uri.Host, out var literal)
                ? [literal]
                : await Dns.GetHostAddressesAsync(uri.DnsSafeHost, ct);
        }
        catch (SocketException)
        {
            return $"Cannot resolve host '{uri.Host}'.";
        }

        return addresses.Any(IsPrivate) ? "Access to local or private network addresses is not allowed." : null;
    }

    private static bool IsPrivate(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (IPAddress.IsLoopback(ip)) return true;

        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = ip.GetAddressBytes();
            return b[0] == 10
                || b[0] == 0
                || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                || (b[0] == 192 && b[1] == 168)
                || (b[0] == 169 && b[1] == 254)
                || (b[0] == 100 && b[1] >= 64 && b[1] <= 127);
        }

        return ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6UniqueLocal || ip.Equals(IPAddress.IPv6Any);
    }

    static WebHttp() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
}
