namespace GrayCat.Core.Models;

/// <summary>One page of a multi-page site. Blocks belong to a page through <see cref="BlockModel.Page"/>.</summary>
public class PageInfo
{
    /// <summary>URL name: "" for the home page, otherwise e.g. "about" (→ /about, about.html).</summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>Shown in the menu and the browser tab.</summary>
    public string Title { get; set; } = "Home";

    /// <summary>Whether navigation bars list this page automatically.</summary>
    public bool ShowInMenu { get; set; } = true;
}
