namespace GrayCat.Core.Generation;

using System.Globalization;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using GrayCat.Core.Models;

/// <summary>Clean, render-ready description of a site. Written to site.json in generated projects.</summary>
public sealed class SiteModel
{
    public string Title { get; set; } = "My Site";
    public string Author { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public SiteTheme Theme { get; set; } = new();

    /// <summary>Language of the content, e.g. "uk" or "en" (html lang attribute).</summary>
    public string Language { get; set; } = "en";

    /// <summary>Icon shown in the browser tab ("assets/..." or a URL); empty = none.</summary>
    public string Favicon { get; set; } = string.Empty;
    public List<SitePage> Pages { get; set; } = new();

    /// <summary>Blocks of the home page (convenience for single-page callers).</summary>
    [JsonIgnore]
    public List<SiteBlock> Blocks => Pages.Count > 0 ? Pages[0].Blocks : new List<SiteBlock>();

    /// <summary>Title for the browser tab of a page.</summary>
    public string PageTitle(SitePage page) => page.Path == "/" ? Title : $"{page.Title} · {Title}";
}

public sealed class SitePage
{
    public string Slug { get; set; } = string.Empty;
    public string Title { get; set; } = "Home";

    /// <summary>"/" or "/about".</summary>
    public string Path { get; set; } = "/";

    /// <summary>"index.html" or "about.html".</summary>
    public string File { get; set; } = "index.html";

    public bool ShowInMenu { get; set; } = true;
    public List<SiteBlock> Blocks { get; set; } = new();
}

/// <summary>How internal links between pages are written for a given output.</summary>
public enum PageLinkStyle
{
    /// <summary>"/about" - React, Next.js, Node, React Native.</summary>
    Paths,

    /// <summary>"about.html" - plain files (static sites, previews).</summary>
    Files
}

public sealed class SiteBuildOptions
{
    public PageLinkStyle Links { get; set; } = PageLinkStyle.Paths;

    /// <summary>Prefix for "assets/..." paths (e.g. a file:/// URL of the project folder for previews).</summary>
    public string? AssetBase { get; set; }

    /// <summary>Keep hidden blocks (marked) - used by the editor.</summary>
    public bool IncludeHidden { get; set; }
}

public sealed class SiteBlock
{
    public string Id { get; set; } = string.Empty;

    /// <summary>Id of the editor block this came from. Used only by the in-editor preview, never written to site.json.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string EditorId { get; set; } = string.Empty;

    public string Type { get; set; } = "Text";
    public string Content { get; set; } = string.Empty;

    /// <summary>Element id so links like "#contact" scroll to the block (empty = none).</summary>
    public string Anchor { get; set; } = string.Empty;

    /// <summary>Hidden from the published site; only present in editor builds.</summary>
    [JsonIgnore]
    public bool Hidden { get; set; }

    /// <summary>Inline styles chosen in the editor, using React-style keys (color, background, fontSize...).</summary>
    public Dictionary<string, object?> Style { get; set; } = new();

    /// <summary>Block properties with defaults filled in and lists normalised to objects.</summary>
    public Dictionary<string, object?> Props { get; set; } = new();

    public string Str(string key) => PropertyValues.AsString(Props.GetValueOrDefault(key));
    public bool Bool(string key) => PropertyValues.AsBool(Props.GetValueOrDefault(key));
    public double Num(string key, double fallback = 0) => PropertyValues.AsDouble(Props.GetValueOrDefault(key), fallback);

    public List<Dictionary<string, object?>> Items(string key) =>
        PropertyValues.AsList(Props.GetValueOrDefault(key)).OfType<Dictionary<string, object?>>().ToList();

    public List<string> Strings(string key) =>
        PropertyValues.AsList(Props.GetValueOrDefault(key)).Select(v => PropertyValues.AsString(v)).Where(s => s.Length > 0).ToList();
}

/// <summary>Keys used in <see cref="ProjectModel.Settings"/>.</summary>
public static class SettingKeys
{
    public const string Description = "description";
    public const string Hidden = "hidden";
    public const string Anchor = "anchor";
    public const string Language = "site.language";
    public const string Favicon = "site.favicon";
}

/// <summary>Turns an editor project into a <see cref="SiteModel"/>.</summary>
public static class SiteBuilder
{
    private static readonly Regex HexColor = new("^#([0-9a-fA-F]{3}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})$", RegexOptions.Compiled);
    private static readonly Regex YouTubeId = new(@"(?:youtu\.be/|youtube(?:-nocookie)?\.com/(?:watch\?(?:.*&)?v=|embed/|shorts/|live/))([A-Za-z0-9_-]{11})", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex VimeoId = new(@"vimeo\.com/(?:video/)?(\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex PlaceholderContent = new(@"^New \w+ block$", RegexOptions.Compiled);

    private static readonly HashSet<string> UrlKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "url", "ctaUrl", "linkUrl", "logo", "image", "src", "backgroundImage", "avatar", "submitUrl", "videoUrl"
    };

    /// <summary>Keys whose values are navigation links (rewritten when they point at another page).</summary>
    private static readonly HashSet<string> LinkKeys = new(StringComparer.OrdinalIgnoreCase) { "url", "ctaUrl", "linkUrl", "homeUrl" };

    /// <summary>Block types that get a section id automatically, so "#contact"-style menu links work.</summary>
    private static readonly Dictionary<string, string> DefaultAnchors = new()
    {
        ["ContactForm"] = "contact",
        ["Pricing"] = "pricing",
        ["Accordion"] = "faq",
        ["Gallery"] = "gallery",
        ["ProductGrid"] = "products",
        ["Testimonial"] = "testimonials"
    };

    private static readonly Regex AnchorPattern = new("[^a-z0-9_-]+", RegexOptions.Compiled);
    private static readonly Regex LanguagePattern = new("^[A-Za-z]{2,3}(-[A-Za-z0-9]{2,8})?$", RegexOptions.Compiled);

    /// <summary>Languages offered in the editor (code → name).</summary>
    public static readonly IReadOnlyDictionary<string, string> Languages = new Dictionary<string, string>
    {
        ["uk"] = "Українська", ["en"] = "English", ["pl"] = "Polski", ["de"] = "Deutsch",
        ["fr"] = "Français", ["es"] = "Español", ["it"] = "Italiano", ["cs"] = "Čeština"
    };

    public static SiteModel Build(ProjectModel project) => Build(project, new SiteBuildOptions());

    public static SiteModel Build(ProjectModel project, SiteBuildOptions options)
    {
        ProjectPages.Normalize(project);

        var title = string.IsNullOrWhiteSpace(project.ProjectName) ? "My Site" : project.ProjectName.Trim();
        var site = new SiteModel
        {
            Title = title,
            Author = project.Author ?? string.Empty,
            Description = project.Settings != null && project.Settings.TryGetValue(SettingKeys.Description, out var d) && !string.IsNullOrWhiteSpace(d)
                ? d.Trim()
                : $"{title} - built with GrayCat",
            Theme = SiteThemes.From(project),
            Language = project.Settings != null && project.Settings.TryGetValue(SettingKeys.Language, out var lang) && LanguagePattern.IsMatch(lang ?? "")
                ? lang!.Trim().ToLowerInvariant()
                : "en",
            Favicon = project.Settings != null && project.Settings.TryGetValue(SettingKeys.Favicon, out var icon) ? SafeUrl(icon) : string.Empty
        };
        if (options.AssetBase != null && site.Favicon.StartsWith(ProjectAssets.FolderName + "/", StringComparison.OrdinalIgnoreCase))
            site.Favicon = options.AssetBase + site.Favicon;

        foreach (var info in project.Pages)
        {
            site.Pages.Add(new SitePage
            {
                Slug = info.Slug,
                Title = info.Title,
                Path = ProjectPages.PathOf(info.Slug),
                File = ProjectPages.FileOf(info.Slug),
                ShowInMenu = info.ShowInMenu
            });
        }

        var index = 0;
        foreach (var page in site.Pages)
        {
            var ordered = ProjectPages.BlocksOf(project, page.Slug)
                .Where(b => !string.IsNullOrWhiteSpace(b.Type))
                .OrderBy(b => b.PositionY)
                .ThenBy(b => b.PositionX);

            var usedAnchors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var block in ordered)
            {
                var hidden = block.Properties != null && block.Properties.TryGetValue(SettingKeys.Hidden, out var h) && PropertyValues.AsBool(h);
                if (hidden && !options.IncludeHidden)
                    continue;

                var built = BuildBlock(block, site, index++);
                built.Hidden = hidden;
                built.Anchor = ChooseAnchor(block, built, usedAnchors);
                if (built.Type is "Navbar" or "Header")
                    AddPageLinks(built, site, page);
                if (built.Type == "Navbar")
                    built.Props["homeUrl"] = "/";
                page.Blocks.Add(built);
            }

            foreach (var block in page.Blocks)
                RewriteLinks(block.Props, site, options);
        }

        return site;
    }

    private static string ChooseAnchor(BlockModel block, SiteBlock built, HashSet<string> used)
    {
        var requested = block.Properties != null && block.Properties.TryGetValue(SettingKeys.Anchor, out var a)
            ? AnchorPattern.Replace(PropertyValues.AsString(a).Trim().TrimStart('#').ToLowerInvariant(), "-").Trim('-')
            : string.Empty;
        if (requested.Length == 0 && DefaultAnchors.TryGetValue(built.Type, out var auto))
            requested = auto;
        return requested.Length > 0 && used.Add(requested) ? requested : string.Empty;
    }

    /// <summary>On multi-page sites, navigation lists the pages (marked active on the current one).</summary>
    private static void AddPageLinks(SiteBlock block, SiteModel site, SitePage current)
    {
        var key = block.Type == "Navbar" ? "menuItems" : "links";
        var custom = block.Items(key);

        if (site.Pages.Count > 1 && PropertyValues.AsBool(block.Props.GetValueOrDefault("pagesInMenu"), true))
        {
            var pageLinks = site.Pages.Where(p => p.ShowInMenu)
                .Select(p => new Dictionary<string, object?> { ["text"] = p.Title, ["url"] = p.Path })
                .ToList();
            var pagePaths = pageLinks.Select(l => (string)l["url"]!).ToHashSet(StringComparer.OrdinalIgnoreCase);
            // Keep custom links that are not just duplicates of page links or the default placeholders.
            custom = custom.Where(l => !pagePaths.Contains(PropertyValues.AsString(l.GetValueOrDefault("url"))) &&
                                       PropertyValues.AsString(l.GetValueOrDefault("url")) is not ("#" or "#home" or "#about"))
                           .ToList();
            custom = pageLinks.Concat(custom).ToList();
        }

        foreach (var link in custom)
        {
            var url = PropertyValues.AsString(link.GetValueOrDefault("url"));
            link["active"] = PageForLink(url, site)?.Path == current.Path && site.Pages.Count > 1;
        }
        block.Props[key] = custom.Cast<object?>().ToList();
    }

    /// <summary>Finds the page a link like "/about", "about.html" or "/" points to.</summary>
    public static SitePage? PageForLink(string url, SiteModel site)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        var u = url.Trim();
        if (u.StartsWith('#') || u.Contains("://") || u.StartsWith("mailto:") || u.StartsWith("tel:") || u.StartsWith("//"))
            return null;
        var hash = u.IndexOf('#');
        if (hash >= 0) u = u[..hash];
        var query = u.IndexOf('?');
        if (query >= 0) u = u[..query];
        u = u.Trim('/');
        if (u.EndsWith(".html", StringComparison.OrdinalIgnoreCase)) u = u[..^5];
        if (u.Equals("index", StringComparison.OrdinalIgnoreCase)) u = string.Empty;
        return site.Pages.FirstOrDefault(p => p.Slug.Equals(u, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Writes page links for the output (/about or about.html) and resolves local asset paths.</summary>
    private static void RewriteLinks(Dictionary<string, object?> props, SiteModel site, SiteBuildOptions options)
    {
        foreach (var key in props.Keys.ToList())
        {
            switch (props[key])
            {
                case string s when LinkKeys.Contains(key):
                    props[key] = RewriteLink(s, site, options);
                    break;
                case string s when options.AssetBase != null && s.StartsWith(ProjectAssets.FolderName + "/", StringComparison.OrdinalIgnoreCase):
                    props[key] = options.AssetBase + s;
                    break;
                case Dictionary<string, object?> nested:
                    RewriteLinks(nested, site, options);
                    break;
                case List<object?> list:
                    foreach (var item in list.OfType<Dictionary<string, object?>>())
                        RewriteLinks(item, site, options);
                    break;
            }
        }
    }

    private static string RewriteLink(string url, SiteModel site, SiteBuildOptions options)
    {
        var page = PageForLink(url, site);
        if (page == null) return url;
        var hash = url.IndexOf('#');
        var fragment = hash >= 0 ? url[hash..] : string.Empty;
        return (options.Links == PageLinkStyle.Files ? page.File : page.Path) + fragment;
    }

    public static SiteBlock BuildBlock(BlockModel block, SiteModel site, int index)
    {
        var type = block.Type.Trim();
        var props = BlockDefaults.For(type);

        foreach (var (key, raw) in block.Properties ?? new Dictionary<string, object>())
        {
            var value = PropertyValues.ToPlain(raw);
            if (props.TryGetValue(key, out var def) && def is List<object?>)
            {
                // A list property must stay a list, even if the user typed plain text.
                value = PropertyValues.AsList(value);
            }
            props[key] = value;
        }

        var content = block.Content ?? string.Empty;
        if (PlaceholderContent.IsMatch(content.Trim()))
            content = string.Empty;

        var sb = new SiteBlock
        {
            Id = $"b{index + 1}",
            EditorId = block.Id,
            Type = type,
            Content = content,
            Style = BuildStyle(block),
            Props = props
        };

        Normalize(sb, block, site);
        return sb;
    }

    private static Dictionary<string, object?> BuildStyle(BlockModel b)
    {
        var style = new Dictionary<string, object?>();
        if (IsColor(b.Color) && !b.Color.Equals("#000000", StringComparison.OrdinalIgnoreCase))
            style["color"] = b.Color;
        if (IsColor(b.Background) && !b.Background.Equals("#FFFFFF", StringComparison.OrdinalIgnoreCase))
            style["background"] = b.Background;
        if (b.FontSize is >= 8 and <= 72 && b.FontSize != 14)
            style["fontSize"] = (double)b.FontSize;
        var align = (b.TextAlignment ?? "").Trim().ToLowerInvariant();
        if (align is "center" or "right" or "justify")
            style["textAlign"] = align;
        var font = SafeFontFamily(b.FontFamily);
        if (font.Length > 0 && !font.Equals("Arial", StringComparison.OrdinalIgnoreCase))
            style["fontFamily"] = font;
        if (b.BorderRadius is > 0 and <= 200)
            style["borderRadius"] = (double)b.BorderRadius;
        return style;
    }

    private static void Normalize(SiteBlock sb, BlockModel block, SiteModel site)
    {
        var p = sb.Props;

        foreach (var key in p.Keys.ToList())
        {
            if (UrlKeys.Contains(key) && p[key] is not List<object?>)
                p[key] = SafeUrl(PropertyValues.AsString(p[key]));
        }

        switch (sb.Type)
        {
            case "Header":
                p["links"] = Links(p, "links");
                break;

            case "Navbar":
                p["menuItems"] = Links(p, "menuItems");
                p["position"] = OneOf(sb.Str("position"), "static", "static", "fixed", "sticky");
                break;

            case "Hero":
                p["overlayOpacity"] = Math.Clamp(sb.Num("overlayOpacity", 0.5), 0, 1);
                p["alignment"] = OneOf(sb.Str("alignment"), "center", "left", "center", "right");
                p["height"] = OneOf(sb.Str("height"), "medium", "small", "medium", "large", "full");
                break;

            case "Button":
                p["variant"] = OneOf(sb.Str("variant"), "primary", "primary", "secondary", "success", "danger", "warning", "info");
                p["size"] = OneOf(sb.Str("size"), "medium", "small", "medium", "large");
                p["target"] = sb.Str("target") == "_blank" ? "_blank" : "_self";
                break;

            case "Card":
                p["imagePosition"] = OneOf(sb.Str("imagePosition"), "top", "top", "left", "right", "bottom");
                break;

            case "Image":
                if (string.IsNullOrWhiteSpace(sb.Str("src")) && !string.IsNullOrWhiteSpace(block.ImagePath))
                    p["src"] = SafeUrl(block.ImagePath!);
                break;

            case "Gallery":
                p["columns"] = (double)Math.Clamp((int)sb.Num("columns", 3), 1, 4);
                p["images"] = MapItems(p, "images", "src", item => new()
                {
                    ["src"] = SafeUrl(Get(item, "src", "url", "image")),
                    ["alt"] = Get(item, "alt", "title"),
                    ["caption"] = Get(item, "caption")
                }).Where(i => PropertyValues.AsString(i["src"]).Length > 0).Cast<object?>().ToList();
                break;

            case "ProductGrid":
                p["columns"] = (double)Math.Clamp((int)sb.Num("columns", 3), 1, 4);
                p["products"] = MapItems(p, "products", "name", item => new()
                {
                    ["name"] = Get(item, "name", "title"),
                    ["price"] = Get(item, "price"),
                    ["description"] = Get(item, "description"),
                    ["image"] = SafeUrl(Get(item, "image", "src")),
                    ["url"] = SafeUrl(Get(item, "url", "link"))
                }).Cast<object?>().ToList();
                break;

            case "Form":
            case "ContactForm":
                var fields = MapItems(p, "fields", "name", NormalizeField).ToList();
                if (sb.Type == "ContactForm")
                {
                    if (sb.Bool("showPhoneField") && fields.All(f => !Equals(f["name"], "phone")))
                    {
                        var at = fields.FindIndex(f => Equals(f["name"], "email"));
                        fields.Insert(at >= 0 ? at + 1 : fields.Count, NormalizeField(new() { ["name"] = "phone", ["type"] = "tel", ["label"] = ContentText.T(site.Language, "Phone") }));
                    }
                    if (sb.Bool("showSubjectField") && fields.All(f => !Equals(f["name"], "subject")))
                    {
                        var at = fields.FindIndex(f => Equals(f["type"], "textarea"));
                        fields.Insert(at >= 0 ? at : fields.Count, NormalizeField(new() { ["name"] = "subject", ["type"] = "text", ["label"] = ContentText.T(site.Language, "Subject"), ["required"] = true }));
                    }
                }
                if (fields.Count == 0)
                    fields.Add(NormalizeField(new() { ["name"] = "email", ["type"] = "email", ["label"] = "Email", ["required"] = true }));
                p["fields"] = fields.Cast<object?>().ToList();
                p["sendingText"] = ContentText.T(site.Language, "Sending...");
                p["requiredText"] = ContentText.T(site.Language, "{0} is required");
                if (string.IsNullOrWhiteSpace(sb.Str("submitUrl")) || sb.Str("submitUrl") == "#")
                    p["submitUrl"] = sb.Type == "ContactForm" ? "/api/contact" : "/api/subscribe";
                break;

            case "Video":
                NormalizeVideo(sb);
                break;

            case "Testimonial":
                p["rating"] = (double)Math.Clamp((int)Math.Round(sb.Num("rating", 5)), 0, 5);
                p["layout"] = OneOf(sb.Str("layout"), "default", "default", "card", "minimal");
                break;

            case "Pricing":
                p["features"] = sb.Strings("features").Cast<object?>().ToList();
                break;

            case "Accordion":
                p["defaultOpen"] = (double)(int)sb.Num("defaultOpen", 0);
                p["items"] = MapItems(p, "items", "question", item => new()
                {
                    ["question"] = Get(item, "question", "title"),
                    ["answer"] = Get(item, "answer", "content", "text")
                }).Cast<object?>().ToList();
                break;

            case "Stats":
                p["layout"] = OneOf(sb.Str("layout"), "horizontal", "horizontal", "vertical", "grid");
                p["stats"] = MapItems(p, "stats", "number", item => new()
                {
                    ["number"] = Get(item, "number", "value"),
                    ["label"] = Get(item, "label", "title")
                }).Cast<object?>().ToList();
                break;

            case "SocialLinks":
                p["style"] = OneOf(sb.Str("style"), "icons", "icons", "buttons", "text");
                p["size"] = OneOf(sb.Str("size"), "medium", "small", "medium", "large");
                p["alignment"] = OneOf(sb.Str("alignment"), "center", "left", "center", "right");
                p["links"] = MapItems(p, "links", "url", item =>
                {
                    var platform = Get(item, "platform", "name").Trim();
                    var label = platform.Length == 0 ? "Link" : char.ToUpperInvariant(platform[0]) + platform[1..];
                    return new()
                    {
                        ["platform"] = platform.ToLowerInvariant(),
                        ["label"] = label,
                        ["initial"] = label[..1],
                        ["url"] = SafeUrl(Get(item, "url"))
                    };
                }).Cast<object?>().ToList();
                break;

            case "Divider":
                p["style"] = OneOf(sb.Str("style"), "solid", "solid", "dashed", "dotted", "double");
                p["thickness"] = (double)Math.Clamp((int)sb.Num("thickness", 1), 1, 10);
                p["color"] = IsColor(sb.Str("color")) ? sb.Str("color") : "#DDDDDD";
                p["spacing"] = OneOf(sb.Str("spacing"), "medium", "small", "medium", "large");
                break;

            case "Footer":
                p["links"] = Links(p, "links");
                if (string.IsNullOrWhiteSpace(sb.Str("copyright")))
                    p["copyright"] = ContentText.F(site.Language, "© {0} {1}. All rights reserved.", DateTime.Now.Year, site.Title);
                break;
        }
    }

    private static void NormalizeVideo(SiteBlock sb)
    {
        var p = sb.Props;
        var url = sb.Str("videoUrl").Trim();
        var autoplay = sb.Bool("autoplay");
        var loop = sb.Bool("loop");
        var muted = sb.Bool("muted") || autoplay; // browsers block autoplay with sound
        var controls = sb.Bool("controls");

        var ratio = sb.Str("aspectRatio").Split(':', '/');
        var w = ratio.Length == 2 && double.TryParse(ratio[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var rw) && rw > 0 ? rw : 16;
        var h = ratio.Length == 2 && double.TryParse(ratio[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var rh) && rh > 0 ? rh : 9;
        p["aspectRatio"] = $"{w.ToString(CultureInfo.InvariantCulture)} / {h.ToString(CultureInfo.InvariantCulture)}";
        p["autoplay"] = autoplay;
        p["loop"] = loop;
        p["muted"] = muted;
        p["controls"] = controls;

        string embed = string.Empty;
        var isFile = false;
        var yt = YouTubeId.Match(url);
        var vm = VimeoId.Match(url);
        if (yt.Success)
        {
            var id = yt.Groups[1].Value;
            var q = new List<string>();
            if (autoplay) q.Add("autoplay=1");
            if (muted) q.Add("mute=1");
            if (loop) { q.Add("loop=1"); q.Add($"playlist={id}"); }
            if (!controls) q.Add("controls=0");
            embed = $"https://www.youtube.com/embed/{id}" + (q.Count > 0 ? "?" + string.Join("&", q) : "");
            p["provider"] = "youtube";
        }
        else if (vm.Success)
        {
            var q = new List<string>();
            if (autoplay) q.Add("autoplay=1");
            if (muted) q.Add("muted=1");
            if (loop) q.Add("loop=1");
            if (!controls) q.Add("controls=0");
            embed = $"https://player.vimeo.com/video/{vm.Groups[1].Value}" + (q.Count > 0 ? "?" + string.Join("&", q) : "");
            p["provider"] = "vimeo";
        }
        else if (url.Length > 0)
        {
            embed = SafeUrl(url);
            isFile = true;
            p["provider"] = "custom";
        }

        p["videoUrl"] = SafeUrl(url);
        p["embedUrl"] = embed;
        p["isFile"] = isFile;
    }

    private static Dictionary<string, object?> NormalizeField(Dictionary<string, object?> item)
    {
        var name = Regex.Replace(Get(item, "name"), "[^A-Za-z0-9_-]", "");
        if (name.Length == 0) name = "field";
        var type = Get(item, "type").ToLowerInvariant();
        if (type is not ("text" or "email" or "tel" or "number" or "url" or "textarea" or "date" or "password"))
            type = "text";
        var label = Get(item, "label");
        return new()
        {
            ["name"] = name,
            ["type"] = type,
            ["label"] = label.Length > 0 ? label : char.ToUpperInvariant(name[0]) + name[1..],
            ["placeholder"] = Get(item, "placeholder"),
            ["required"] = PropertyValues.AsBool(item.GetValueOrDefault("required"))
        };
    }

    private static List<object?> Links(Dictionary<string, object?> p, string key) =>
        MapItems(p, key, "text", item => new()
        {
            ["text"] = Get(item, "text", "label", "title", "name"),
            ["url"] = SafeUrl(Get(item, "url", "href", "link") is { Length: > 0 } u ? u : "#")
        }).Where(i => PropertyValues.AsString(i["text"]).Length > 0).Cast<object?>().ToList();

    /// <summary>Maps a list property; plain strings become objects using <paramref name="stringKey"/>.</summary>
    private static IEnumerable<Dictionary<string, object?>> MapItems(
        Dictionary<string, object?> p, string key, string stringKey,
        Func<Dictionary<string, object?>, Dictionary<string, object?>> map)
    {
        foreach (var raw in PropertyValues.AsList(p.GetValueOrDefault(key)))
        {
            var item = raw as Dictionary<string, object?>
                       ?? new Dictionary<string, object?> { [stringKey] = PropertyValues.AsString(raw) };
            yield return map(item);
        }
    }

    private static string Get(Dictionary<string, object?> item, params string[] keys)
    {
        foreach (var k in keys)
        {
            var match = item.FirstOrDefault(kv => kv.Key.Equals(k, StringComparison.OrdinalIgnoreCase));
            if (match.Key != null)
            {
                var s = PropertyValues.AsString(match.Value);
                if (s.Length > 0) return s;
            }
        }
        return string.Empty;
    }

    private static string OneOf(string value, string fallback, params string[] allowed)
    {
        var v = value.Trim().ToLowerInvariant();
        return allowed.Contains(v) ? v : fallback;
    }

    public static bool IsColor(string? value) => value != null && HexColor.IsMatch(value.Trim());

    public static string SafeFontFamily(string? font) =>
        Regex.Replace(font ?? string.Empty, @"[^A-Za-z0-9 ,'\-]", "").Trim();

    /// <summary>Blocks script URLs; everything else (absolute, relative, anchors, mailto) passes through.</summary>
    public static string SafeUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return string.Empty;
        var trimmed = url.Trim();
        var compact = new string(trimmed.Where(c => !char.IsWhiteSpace(c) && !char.IsControl(c)).ToArray()).ToLowerInvariant();
        if (compact.StartsWith("javascript:") || compact.StartsWith("vbscript:") ||
            (compact.StartsWith("data:") && !compact.StartsWith("data:image/")))
            return "#";
        return trimmed;
    }
}
