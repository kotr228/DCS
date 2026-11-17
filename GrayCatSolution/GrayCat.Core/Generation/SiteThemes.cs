namespace GrayCat.Core.Generation;

using System.Globalization;
using GrayCat.Core.Models;

/// <summary>Site-wide colours and font, stored in <see cref="ProjectModel.Settings"/>.</summary>
public sealed class SiteTheme
{
    public string Primary { get; set; } = SiteThemes.DefaultPrimary;
    public string PrimaryDark { get; set; } = "#4338ca";
    public string Accent { get; set; } = SiteThemes.DefaultAccent;
    public string Font { get; set; } = SiteThemes.DefaultFont;

    /// <summary>CSS font-family value.</summary>
    public string FontStack { get; set; } = SiteThemes.SystemStack;

    /// <summary>Google Fonts stylesheet to load, or empty for system fonts.</summary>
    public string FontUrl { get; set; } = string.Empty;
}

public static class SiteThemes
{
    public const string DefaultPrimary = "#4f46e5";
    public const string DefaultAccent = "#7c3aed";
    public const string DefaultFont = "System";
    public const string SystemStack = "system-ui, -apple-system, \"Segoe UI\", Roboto, \"Helvetica Neue\", Arial, sans-serif";

    /// <summary>Fonts offered in the editor: name → (CSS stack, Google Fonts family or null).</summary>
    public static readonly IReadOnlyDictionary<string, (string Stack, string? Google)> Fonts =
        new Dictionary<string, (string, string?)>
        {
            ["System"] = (SystemStack, null),
            ["Inter"] = ("\"Inter\", " + SystemStack, "Inter:wght@400;500;600;700;800"),
            ["Roboto"] = ("\"Roboto\", " + SystemStack, "Roboto:wght@400;500;700;900"),
            ["Poppins"] = ("\"Poppins\", " + SystemStack, "Poppins:wght@400;500;600;700;800"),
            ["Montserrat"] = ("\"Montserrat\", " + SystemStack, "Montserrat:wght@400;500;600;700;800"),
            ["Open Sans"] = ("\"Open Sans\", " + SystemStack, "Open+Sans:wght@400;600;700;800"),
            ["Lora"] = ("\"Lora\", Georgia, serif", "Lora:wght@400;500;600;700"),
            ["Playfair Display"] = ("\"Playfair Display\", Georgia, serif", "Playfair+Display:wght@400;600;700;800"),
            ["Georgia"] = ("Georgia, \"Times New Roman\", serif", null),
            ["Monospace"] = ("ui-monospace, \"Cascadia Code\", Consolas, monospace", null)
        };

    public const string PrimaryKey = "theme.primary";
    public const string AccentKey = "theme.accent";
    public const string FontKey = "theme.font";

    public static SiteTheme From(ProjectModel project)
    {
        var settings = project.Settings ?? new Dictionary<string, string>();
        var primary = settings.TryGetValue(PrimaryKey, out var p) && SiteBuilder.IsColor(p) ? p.Trim() : DefaultPrimary;
        var accent = settings.TryGetValue(AccentKey, out var a) && SiteBuilder.IsColor(a) ? a.Trim() : DefaultAccent;
        var fontName = settings.TryGetValue(FontKey, out var f) && Fonts.ContainsKey(f) ? f : DefaultFont;
        var font = Fonts[fontName];

        return new SiteTheme
        {
            Primary = primary,
            PrimaryDark = Darken(primary, 0.15),
            Accent = accent,
            Font = fontName,
            FontStack = font.Stack,
            FontUrl = font.Google == null ? string.Empty : $"https://fonts.googleapis.com/css2?family={font.Google}&display=swap"
        };
    }

    /// <summary>CSS custom properties for the theme, applied on the .gc-site element.</summary>
    public static string CssVariables(SiteTheme theme) =>
        $"--gc-primary:{theme.Primary};--gc-primary-dark:{theme.PrimaryDark};--gc-accent:{theme.Accent};--gc-font:{theme.FontStack};";

    /// <summary>Makes a #rrggbb colour darker by <paramref name="amount"/> (0..1).</summary>
    public static string Darken(string hex, double amount)
    {
        var h = hex.TrimStart('#');
        if (h.Length == 3) h = string.Concat(h.Select(c => $"{c}{c}"));
        if (h.Length < 6) return hex;
        int Channel(int i) => (int)Math.Round(int.Parse(h.Substring(i, 2), NumberStyles.HexNumber) * (1 - amount));
        return $"#{Channel(0):x2}{Channel(2):x2}{Channel(4):x2}";
    }
}
