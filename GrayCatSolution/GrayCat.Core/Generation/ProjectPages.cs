namespace GrayCat.Core.Generation;

using System.Text.RegularExpressions;
using GrayCat.Core.Models;

/// <summary>Rules for pages: the home page always exists, slugs are URL-safe and unique.</summary>
public static class ProjectPages
{
    private static readonly Regex SlugPattern = new("^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.Compiled);
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "index", "api", "assets", "public", "src", "components", "styles", "data", "pages", "node_modules", "_next", "404"
    };

    /// <summary>Makes sure the project has a home page and every block points at an existing page.</summary>
    public static void Normalize(ProjectModel project)
    {
        project.Pages ??= new List<PageInfo>();
        project.Blocks ??= new List<BlockModel>();

        project.Pages.RemoveAll(p => p == null);
        var home = project.Pages.FirstOrDefault(p => string.IsNullOrEmpty(p.Slug));
        if (home == null)
        {
            home = new PageInfo { Slug = string.Empty, Title = "Home" };
            project.Pages.Insert(0, home);
        }
        else if (project.Pages.IndexOf(home) != 0)
        {
            project.Pages.Remove(home);
            project.Pages.Insert(0, home);
        }

        // Drop duplicate slugs (keep the first).
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        project.Pages.RemoveAll(p => !seen.Add(p.Slug ?? string.Empty));

        foreach (var page in project.Pages)
        {
            page.Slug ??= string.Empty;
            if (string.IsNullOrWhiteSpace(page.Title))
                page.Title = string.IsNullOrEmpty(page.Slug) ? "Home" : char.ToUpperInvariant(page.Slug[0]) + page.Slug[1..];
        }

        foreach (var block in project.Blocks)
        {
            block.Page ??= string.Empty;
            if (!seen.Contains(block.Page))
                block.Page = string.Empty;
        }
    }

    public static IEnumerable<BlockModel> BlocksOf(ProjectModel project, string slug) =>
        (project.Blocks ?? new List<BlockModel>()).Where(b => string.Equals(b.Page ?? "", slug ?? "", StringComparison.OrdinalIgnoreCase));

    /// <summary>Turns a title like "About Us" into "about-us".</summary>
    public static string SlugFrom(string title)
    {
        var slug = Regex.Replace((title ?? "").Trim().ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        return slug.Length > 40 ? slug[..40].Trim('-') : slug;
    }

    /// <returns>An error message, or null when the slug can be used for a new page.</returns>
    public static string? ValidateSlug(ProjectModel project, string slug, string? currentSlug = null)
    {
        if (string.IsNullOrWhiteSpace(slug))
            return "The page address cannot be empty (only the home page has no address).";
        if (!SlugPattern.IsMatch(slug))
            return "Use lowercase letters, digits and dashes, e.g. about-us.";
        if (Reserved.Contains(slug))
            return $"'{slug}' is reserved; choose another address.";
        if (project.Pages.Any(p => p.Slug.Equals(slug, StringComparison.OrdinalIgnoreCase) &&
                                   !p.Slug.Equals(currentSlug ?? "\0", StringComparison.OrdinalIgnoreCase)))
            return $"A page with the address '{slug}' already exists.";
        return null;
    }

    public static string PathOf(string slug) => string.IsNullOrEmpty(slug) ? "/" : "/" + slug;
    public static string FileOf(string slug) => string.IsNullOrEmpty(slug) ? "index.html" : slug + ".html";
}
