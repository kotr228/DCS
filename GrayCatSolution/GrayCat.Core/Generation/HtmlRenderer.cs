namespace GrayCat.Core.Generation;

using System.Globalization;
using System.Net;
using System.Text;

/// <summary>Renders a <see cref="SiteModel"/> to plain HTML (no build step, no framework).</summary>
public static class HtmlRenderer
{
    /// <summary>Renders the home page.</summary>
    public static string RenderPage(SiteModel site, bool inlineAssets, string? baseHref = null, bool editor = false) =>
        RenderPage(site, site.Pages.Count > 0 ? site.Pages[0] : new SitePage(), inlineAssets, baseHref, editor);

    /// <param name="inlineAssets">true: CSS and JS embedded in the page (preview); false: links to styles.css / script.js.</param>
    /// <param name="editor">Adds block markers and the click-to-select script used inside GrayCat Studio.</param>
    public static string RenderPage(SiteModel site, SitePage page, bool inlineAssets, string? baseHref = null, bool editor = false)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine($"<html lang=\"{Attr(site.Language)}\">");
        sb.AppendLine("<head>");
        sb.AppendLine("  <meta charset=\"utf-8\" />");
        if (baseHref != null)
            sb.AppendLine($"  <base href=\"{Attr(baseHref)}\" />");
        sb.AppendLine("  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />");
        sb.AppendLine($"  <meta name=\"description\" content=\"{Attr(site.Description)}\" />");
        if (!string.IsNullOrWhiteSpace(site.Author))
            sb.AppendLine($"  <meta name=\"author\" content=\"{Attr(site.Author)}\" />");
        sb.AppendLine($"  <meta property=\"og:title\" content=\"{Attr(site.PageTitle(page))}\" />");
        sb.AppendLine($"  <meta property=\"og:description\" content=\"{Attr(site.Description)}\" />");
        sb.AppendLine($"  <title>{Enc(site.PageTitle(page))}</title>");
        if (site.Favicon.Length > 0)
            sb.AppendLine($"  <link rel=\"icon\" href=\"{Attr(site.Favicon)}\" />");
        if (site.Theme.FontUrl.Length > 0)
        {
            sb.AppendLine("  <link rel=\"preconnect\" href=\"https://fonts.gstatic.com\" crossorigin />");
            sb.AppendLine($"  <link rel=\"stylesheet\" href=\"{Attr(site.Theme.FontUrl)}\" />");
        }
        if (inlineAssets)
            sb.AppendLine($"  <style>\n{SiteAssets.Css}\n  </style>");
        else
            sb.AppendLine("  <link rel=\"stylesheet\" href=\"styles.css\" />");
        if (editor)
            sb.AppendLine($"  <style>\n{SiteAssets.EditorCss}\n  </style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");
        sb.AppendLine($"<div class=\"gc-site\" style=\"{Attr(SiteThemes.CssVariables(site.Theme))}\">");
        sb.Append(RenderBlocks(page, editor));
        sb.AppendLine("</div>");
        if (editor)
            sb.AppendLine($"<script>\n{SiteAssets.EditorScript}\n</script>");
        else if (inlineAssets)
            sb.AppendLine($"<script>\n{SiteAssets.FormScript}\n</script>");
        else
            sb.AppendLine("<script src=\"script.js\"></script>");
        sb.AppendLine("</body>");
        sb.AppendLine("</html>");
        return sb.ToString();
    }

    /// <summary>Home page body.</summary>
    public static string RenderBlocks(SiteModel site, bool editor = false) =>
        RenderBlocks(site.Pages.Count > 0 ? site.Pages[0] : new SitePage(), editor);

    /// <summary>The page body (all blocks); in editor mode each block root carries data-gc-id.</summary>
    public static string RenderBlocks(SitePage page, bool editor = false)
    {
        var sb = new StringBuilder();
        if (page.Blocks.Count == 0)
        {
            sb.AppendLine(editor
                ? "<div class=\"gc-empty\"><h1>This page is empty</h1><p>Click a block in the library on the left to add it.</p></div>"
                : "<div class=\"gc-empty\"><h1>This page is empty</h1><p>Add blocks in GrayCat Studio to build it.</p></div>");
        }
        var inCardRow = false;
        for (var i = 0; i < page.Blocks.Count; i++)
        {
            var block = page.Blocks[i];
            // Cards that follow each other sit side by side.
            var isCard = block.Type == "Card";
            var nextIsCard = i + 1 < page.Blocks.Count && page.Blocks[i + 1].Type == "Card";
            if (isCard && nextIsCard && !inCardRow)
            {
                sb.AppendLine("<div class=\"gc-card-row\">");
                inCardRow = true;
            }

            var html = RenderBlock(block);
            if (block.Anchor.Length > 0)
                html = AddRootAttributes(html, $" id=\"{Attr(block.Anchor)}\"");
            if (editor)
                html = AddRootAttributes(html, $" data-gc-id=\"{Attr(block.EditorId)}\" data-gc-type=\"{Attr(block.Type)}\"" +
                                               (block.Hidden ? " data-gc-hidden=\"true\"" : ""));
            sb.AppendLine(html);

            if (inCardRow && !nextIsCard)
            {
                sb.AppendLine("</div>");
                inCardRow = false;
            }
        }
        return sb.ToString();
    }

    /// <summary>Inserts attributes into the block's root element.</summary>
    private static string AddRootAttributes(string html, string attributes)
    {
        var end = html.IndexOfAny(new[] { ' ', '>' });
        return end < 0 ? html : html.Insert(end, attributes);
    }

    private static string NavLink(Dictionary<string, object?> link, bool listItem)
    {
        var active = PropertyValues.AsBool(link.GetValueOrDefault("active"));
        var a = $"<a href=\"{Attr(S(link, "url"))}\"{(active ? " class=\"is-active\" aria-current=\"page\"" : "")}>{Enc(S(link, "text"))}</a>";
        return listItem ? $"<li>{a}</li>" : a;
    }

    public static string RenderBlock(SiteBlock b)
    {
        var style = StyleAttr(b.Style);
        var cls = "gc-block gc-" + b.Type.ToLowerInvariant();

        switch (b.Type)
        {
            case "Header":
            {
                var nav = b.Bool("showNavigation") && b.Items("links").Count > 0
                    ? $"<nav class=\"gc-header-nav\">{string.Concat(b.Items("links").Select(l => NavLink(l, false)))}</nav>"
                    : "";
                return $"<header class=\"{cls}\"{style}><div class=\"gc-container\"><h1>{Enc(b.Str("title"))}</h1>{P(b.Str("subtitle"), "gc-lead")}{nav}</div></header>";
            }

            case "Navbar":
            {
                var extra = b.Str("position") != "static" ? $" gc-navbar-{b.Str("position")}" : "";
                if (b.Bool("transparent")) extra += " gc-navbar-transparent";
                var logo = b.Str("logo").Length > 0 ? $"<img src=\"{Attr(b.Str("logo"))}\" alt=\"\" />" : "";
                var links = string.Concat(b.Items("menuItems").Select(l => NavLink(l, true)));
                var search = b.Bool("showSearch") ? "<input class=\"gc-search\" type=\"search\" placeholder=\"Search\" aria-label=\"Search\" />" : "";
                var cta = b.Str("ctaText").Length > 0 ? $"<a class=\"gc-btn gc-btn-primary gc-btn-small\" href=\"{Attr(b.Str("ctaUrl"))}\">{Enc(b.Str("ctaText"))}</a>" : "";
                return $"<nav class=\"{cls}{extra}\"{style}><div class=\"gc-container gc-navbar-inner\"><a class=\"gc-brand\" href=\"{Attr(b.Str("homeUrl").Length > 0 ? b.Str("homeUrl") : "#")}\">{logo}{Enc(b.Str("brandName"))}</a><ul class=\"gc-nav-links\">{links}</ul>{search}{cta}</div></nav>";
            }

            case "Hero":
            {
                var bg = b.Str("backgroundImage");
                var heroStyle = new Dictionary<string, object?>(b.Style);
                var styleText = StyleText(heroStyle);
                if (bg.Length > 0) styleText += $"background-image:url('{CssUrl(bg)}');";
                var overlay = b.Bool("overlay") && bg.Length > 0
                    ? $"<div class=\"gc-hero-overlay\" style=\"opacity:{Num(b.Num("overlayOpacity", 0.5))}\"></div>"
                    : "";
                var cta = b.Str("ctaText").Length > 0 ? $"<a class=\"gc-btn gc-btn-large\" href=\"{Attr(b.Str("ctaUrl"))}\">{Enc(b.Str("ctaText"))}</a>" : "";
                return $"<section class=\"{cls} gc-hero-{b.Str("height")} gc-align-{b.Str("alignment")}\"{(styleText.Length > 0 ? $" style=\"{Attr(styleText)}\"" : "")}>{overlay}<div class=\"gc-container gc-hero-content\"><h1>{Enc(b.Str("headline"))}</h1>{P(b.Str("subheadline"), "gc-lead")}{cta}</div></section>";
            }

            case "Button":
            {
                var btn = $"gc-btn gc-btn-{b.Str("variant")} gc-btn-{b.Str("size")}";
                if (b.Bool("fullWidth")) btn += " gc-btn-block";
                if (b.Bool("disabled")) btn += " is-disabled";
                var target = b.Str("target") == "_blank" ? " target=\"_blank\" rel=\"noopener noreferrer\"" : "";
                var disabled = b.Bool("disabled") ? " aria-disabled=\"true\" tabindex=\"-1\"" : "";
                return $"<div class=\"gc-block gc-button-block\"{style}><div class=\"gc-container\"><a class=\"{btn}\" href=\"{Attr(b.Str("url"))}\"{target}{disabled}>{Enc(b.Str("text"))}</a></div></div>";
            }

            case "Card":
            {
                var card = $"gc-card gc-card-img-{b.Str("imagePosition")}";
                if (b.Bool("shadow")) card += " gc-card-shadow";
                if (b.Bool("bordered")) card += " gc-card-bordered";
                if (b.Bool("hoverable")) card += " gc-card-hover";
                var img = b.Str("image").Length > 0 ? $"<img class=\"gc-card-img\" src=\"{Attr(b.Str("image"))}\" alt=\"{Attr(b.Str("title"))}\" loading=\"lazy\" />" : "";
                var link = b.Str("linkText").Length > 0 ? $"<a class=\"gc-link\" href=\"{Attr(b.Str("linkUrl"))}\">{Enc(b.Str("linkText"))} →</a>" : "";
                return $"<section class=\"gc-block gc-card-block\"{style}><div class=\"gc-container\"><article class=\"{card}\">{img}<div class=\"gc-card-body\"><h3>{Enc(b.Str("title"))}</h3>{P(b.Str("description"))}{link}</div></article></div></section>";
            }

            case "Image":
            {
                var src = b.Str("src");
                var inner = src.Length > 0
                    ? $"<img src=\"{Attr(src)}\" alt=\"{Attr(b.Str("alt"))}\" loading=\"lazy\" />"
                    : "<div class=\"gc-placeholder\">No image selected</div>";
                var caption = b.Str("caption").Length > 0 ? $"<figcaption>{Enc(b.Str("caption"))}</figcaption>" : "";
                return $"<figure class=\"{cls}\"{style}>{inner}{caption}</figure>";
            }

            case "Gallery":
            {
                var items = string.Concat(b.Items("images").Select(i =>
                    $"<figure><img src=\"{Attr(S(i, "src"))}\" alt=\"{Attr(S(i, "alt"))}\" loading=\"lazy\" />{(S(i, "caption").Length > 0 ? $"<figcaption>{Enc(S(i, "caption"))}</figcaption>" : "")}</figure>"));
                return $"<section class=\"{cls}\"{style}><div class=\"gc-container\">{H2(b.Str("title"))}<div class=\"gc-grid\" style=\"--gc-cols:{(int)b.Num("columns", 3)}\">{items}</div></div></section>";
            }

            case "ProductGrid":
            {
                var currency = b.Str("currency");
                var buttonText = b.Str("buttonText");
                var items = string.Concat(b.Items("products").Select(p =>
                    "<article class=\"gc-product\">" +
                    (S(p, "image").Length > 0 ? $"<img src=\"{Attr(S(p, "image"))}\" alt=\"{Attr(S(p, "name"))}\" loading=\"lazy\" />" : "") +
                    $"<div class=\"gc-product-body\"><h3>{Enc(S(p, "name"))}</h3>{P(S(p, "description"))}" +
                    (S(p, "price").Length > 0 ? $"<div class=\"gc-product-price\">{Enc(currency + S(p, "price"))}</div>" : "") +
                    (buttonText.Length > 0 ? $"<a class=\"gc-btn gc-btn-primary gc-btn-small\" href=\"{Attr(S(p, "url").Length > 0 ? S(p, "url") : "#")}\">{Enc(buttonText)}</a>" : "") +
                    "</div></article>"));
                return $"<section class=\"{cls}\"{style}><div class=\"gc-container\">{H2(b.Str("title"))}<div class=\"gc-grid\" style=\"--gc-cols:{(int)b.Num("columns", 3)}\">{items}</div></div></section>";
            }

            case "Form":
            case "ContactForm":
            {
                var fields = string.Concat(b.Items("fields").Select(f => Field(b.Id, f)));
                return $"<section class=\"gc-block gc-form-block gc-{b.Type.ToLowerInvariant()}\"{style}><div class=\"gc-container gc-narrow\">{H2(b.Str("title"), "")}{P(b.Str("subtitle"))}" +
                       $"<form class=\"gc-form\" data-gc-form action=\"{Attr(b.Str("submitUrl"))}\" method=\"post\" data-success=\"{Attr(b.Str("successMessage"))}\" data-sending=\"{Attr(b.Str("sendingText"))}\" data-error=\"{Attr(b.Str("errorMessage"))}\">{fields}" +
                       $"<button type=\"submit\" class=\"gc-btn gc-btn-primary\">{Enc(b.Str("submitText"))}</button><p class=\"gc-form-status\" role=\"status\" aria-live=\"polite\"></p></form></div></section>";
            }

            case "Video":
            {
                var embed = b.Str("embedUrl");
                string inner;
                if (embed.Length == 0)
                    inner = "<div class=\"gc-placeholder\">Add a video URL in GrayCat Studio</div>";
                else if (b.Bool("isFile"))
                    inner = $"<div class=\"gc-video-frame\" style=\"aspect-ratio:{b.Str("aspectRatio")}\"><video src=\"{Attr(embed)}\"{(b.Bool("controls") ? " controls" : "")}{(b.Bool("autoplay") ? " autoplay" : "")}{(b.Bool("loop") ? " loop" : "")}{(b.Bool("muted") ? " muted" : "")} playsinline></video></div>";
                else
                    inner = $"<div class=\"gc-video-frame\" style=\"aspect-ratio:{b.Str("aspectRatio")}\"><iframe src=\"{Attr(embed)}\" title=\"Video\" referrerpolicy=\"strict-origin-when-cross-origin\" allow=\"accelerometer; autoplay; clipboard-write; encrypted-media; gyroscope; picture-in-picture\" allowfullscreen></iframe></div>";
                return $"<section class=\"{cls}\"{style}><div class=\"gc-container\">{inner}</div></section>";
            }

            case "Testimonial":
            {
                var rating = (int)b.Num("rating", 5);
                var stars = b.Bool("showRating") ? $"<div class=\"gc-stars\" aria-label=\"{rating} out of 5\">{new string('★', rating)}{new string('☆', 5 - rating)}</div>" : "";
                var avatar = b.Str("avatar").Length > 0 ? $"<img src=\"{Attr(b.Str("avatar"))}\" alt=\"{Attr(b.Str("author"))}\" />" : "";
                var role = b.Str("role").Length > 0 ? $"<span>{Enc(b.Str("role"))}</span>" : "";
                return $"<section class=\"{cls} gc-testimonial-{b.Str("layout")}\"{style}><div class=\"gc-container gc-narrow\"><blockquote><p class=\"gc-quote\">“{Enc(b.Str("quote"))}”</p>{stars}<footer class=\"gc-author\">{avatar}<div><strong>{Enc(b.Str("author"))}</strong>{role}</div></footer></blockquote></div></section>";
            }

            case "Pricing":
            {
                var badge = b.Str("badge").Length > 0 ? $"<span class=\"gc-badge\">{Enc(b.Str("badge"))}</span>" : "";
                var period = b.Str("period").Length > 0 ? $"<span class=\"gc-period\">/{Enc(b.Str("period"))}</span>" : "";
                var features = string.Concat(b.Strings("features").Select(f => $"<li>{Enc(f)}</li>"));
                var cta = b.Str("ctaText").Length > 0 ? $"<a class=\"gc-btn gc-btn-primary gc-btn-block\" href=\"{Attr(b.Str("ctaUrl"))}\">{Enc(b.Str("ctaText"))}</a>" : "";
                return $"<section class=\"{cls}\"{style}><div class=\"gc-container\"><div class=\"gc-price-card{(b.Bool("highlighted") ? " gc-price-highlight" : "")}\">{badge}<h3>{Enc(b.Str("planName"))}</h3><div class=\"gc-price\"><span class=\"gc-currency\">{Enc(b.Str("currency"))}</span><span class=\"gc-amount\">{Enc(b.Str("price"))}</span>{period}</div><ul class=\"gc-features\">{features}</ul>{cta}</div></div></section>";
            }

            case "Accordion":
            {
                var open = (int)b.Num("defaultOpen", 0);
                var name = b.Bool("allowMultiple") ? "" : $" name=\"acc-{b.Id}\"";
                var items = string.Concat(b.Items("items").Select((it, i) =>
                    $"<details class=\"gc-accordion-item\"{name}{(i == open ? " open" : "")}><summary>{Enc(S(it, "question"))}</summary><div class=\"gc-accordion-body\">{Enc(S(it, "answer"))}</div></details>"));
                return $"<section class=\"{cls}\"{style}><div class=\"gc-container gc-narrow\">{H2(b.Str("title"), "")}<div class=\"gc-accordion-list\">{items}</div></div></section>";
            }

            case "Stats":
            {
                var items = string.Concat(b.Items("stats").Select(s =>
                    $"<div class=\"gc-stat\"><div class=\"gc-stat-number\">{Enc(S(s, "number"))}</div><div class=\"gc-stat-label\">{Enc(S(s, "label"))}</div></div>"));
                return $"<section class=\"{cls} gc-stats-{b.Str("layout")}\"{style}><div class=\"gc-container\"><div class=\"gc-stats-row\">{items}</div></div></section>";
            }

            case "SocialLinks":
            {
                var items = string.Concat(b.Items("links").Select(l =>
                    $"<li><a class=\"gc-social-link\" href=\"{Attr(S(l, "url"))}\" target=\"_blank\" rel=\"noopener noreferrer\" aria-label=\"{Attr(S(l, "label"))}\"><span class=\"gc-social-icon\" aria-hidden=\"true\">{Enc(S(l, "initial"))}</span><span class=\"gc-social-name\">{Enc(S(l, "label"))}</span></a></li>"));
                return $"<section class=\"{cls} gc-social gc-social-{b.Str("style")} gc-social-{b.Str("size")} gc-align-{b.Str("alignment")}\"{style}><div class=\"gc-container\"><ul class=\"gc-social-list\">{items}</ul></div></section>";
            }

            case "Divider":
            {
                var line = $"<hr style=\"border-top:{(int)b.Num("thickness", 1)}px {b.Str("style")} {b.Str("color")}\" />";
                var inner = b.Bool("withIcon") && b.Str("icon").Length > 0
                    ? $"<div class=\"gc-divider-with-icon\">{line}<span class=\"gc-divider-icon\">{Enc(b.Str("icon"))}</span>{line}</div>"
                    : line;
                return $"<div class=\"{cls} gc-divider-{b.Str("spacing")}\"{style}><div class=\"gc-container\">{inner}</div></div>";
            }

            case "Footer":
            {
                var links = b.Items("links").Count > 0
                    ? $"<nav>{string.Concat(b.Items("links").Select(l => $"<a href=\"{Attr(S(l, "url"))}\">{Enc(S(l, "text"))}</a>"))}</nav>"
                    : "";
                return $"<footer class=\"{cls}\"{style}><div class=\"gc-container gc-footer-inner\"><p>{Enc(b.Str("copyright"))}</p>{links}</div></footer>";
            }

            default: // Text and any unknown block type
                return $"<section class=\"gc-block gc-text\"{style}><div class=\"gc-container\"><p>{Enc(b.Content)}</p></div></section>";
        }
    }

    private static string Field(string blockId, Dictionary<string, object?> f)
    {
        var name = S(f, "name");
        var id = $"{blockId}-{name}";
        var required = PropertyValues.AsBool(f.GetValueOrDefault("required"));
        var req = required ? " required" : "";
        var placeholder = S(f, "placeholder").Length > 0 ? $" placeholder=\"{Attr(S(f, "placeholder"))}\"" : "";
        var label = $"<label for=\"{Attr(id)}\">{Enc(S(f, "label"))}{(required ? " <span class=\"gc-required\">*</span>" : "")}</label>";
        var input = S(f, "type") == "textarea"
            ? $"<textarea id=\"{Attr(id)}\" name=\"{Attr(name)}\"{placeholder}{req}></textarea>"
            : $"<input id=\"{Attr(id)}\" name=\"{Attr(name)}\" type=\"{Attr(S(f, "type"))}\"{placeholder}{req} />";
        return $"<div class=\"gc-field\">{label}{input}</div>";
    }

    private static string S(Dictionary<string, object?> item, string key) =>
        PropertyValues.AsString(item.GetValueOrDefault(key));

    private static string P(string text, string? cls = null) =>
        text.Length == 0 ? "" : $"<p{(cls != null ? $" class=\"{cls}\"" : "")}>{Enc(text)}</p>";

    private static string H2(string text, string cls = "gc-section-title") =>
        text.Length == 0 ? "" : $"<h2{(cls.Length > 0 ? $" class=\"{cls}\"" : "")}>{Enc(text)}</h2>";

    /// <summary>Escapes only what HTML needs (&lt; &gt; &amp; quotes), so Cyrillic and other text stays readable in the files.</summary>
    public static string Enc(string text)
    {
        if (string.IsNullOrEmpty(text) || text.IndexOfAny(new[] { '<', '>', '&', '"', '\'' }) < 0)
            return text ?? string.Empty;
        var sb = new StringBuilder(text.Length + 16);
        foreach (var c in text)
        {
            sb.Append(c switch
            {
                '<' => "&lt;",
                '>' => "&gt;",
                '&' => "&amp;",
                '"' => "&quot;",
                '\'' => "&#39;",
                _ => c.ToString()
            });
        }
        return sb.ToString();
    }

    private static string Attr(string text) => Enc(text);
    private static string Num(double d) => d.ToString("0.##", CultureInfo.InvariantCulture);
    private static string CssUrl(string url) => url.Replace("\\", "/").Replace("'", "%27").Replace("\"", "%22").Replace("(", "%28").Replace(")", "%29");

    private static string StyleAttr(Dictionary<string, object?> style)
    {
        var text = StyleText(style);
        return text.Length == 0 ? "" : $" style=\"{Attr(text)}\"";
    }

    /// <summary>Turns React-style keys into CSS declarations. Values were validated by SiteBuilder.</summary>
    public static string StyleText(Dictionary<string, object?> style)
    {
        var sb = new StringBuilder();
        foreach (var (key, value) in style)
        {
            var css = key switch
            {
                "color" => "color",
                "background" => "background",
                "fontSize" => "font-size",
                "textAlign" => "text-align",
                "fontFamily" => "font-family",
                "borderRadius" => "border-radius",
                _ => null
            };
            if (css == null || value == null) continue;
            var v = value is double d ? Num(d) + "px" : PropertyValues.AsString(value);
            sb.Append(css).Append(':').Append(v).Append(';');
        }
        return sb.ToString();
    }
}
