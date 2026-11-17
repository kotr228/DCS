namespace GrayCat.Tests;

using System.Text.Json;
using GrayCat.Core.Generation;
using GrayCat.Core.Models;
using GrayCat.Core.Models.Generation;
using GrayCat.Shared.Models;

public class PagesAndThemeTests
{
    private static ProjectModel TwoPages()
    {
        var project = new ProjectModel
        {
            ProjectName = "Acme",
            Pages = { new PageInfo { Slug = "", Title = "Home" }, new PageInfo { Slug = "about", Title = "About us" } }
        };
        project.Blocks.Add(TestData.Block("Navbar", 0));
        project.Blocks.Add(TestData.Block("Hero", 100, b => { b.Properties["ctaUrl"] = "/about#team"; }));
        project.Blocks.Add(TestData.Block("Navbar", 0, b => b.Page = "about"));
        project.Blocks.Add(TestData.Block("Text", 100, b => { b.Page = "about"; b.Content = "About text"; }));
        return project;
    }

    [Fact]
    public void LegacyProjects_GetAHomePage()
    {
        var project = new ProjectModel { Blocks = { TestData.Block("Text", 0) } };
        var site = SiteBuilder.Build(project);
        Assert.Single(site.Pages);
        Assert.Equal("/", site.Pages[0].Path);
        Assert.Single(site.Pages[0].Blocks);
    }

    [Fact]
    public void BlocksGoToTheirPages_AndMenusListPages()
    {
        var site = SiteBuilder.Build(TwoPages());
        Assert.Equal(new[] { "/", "/about" }, site.Pages.Select(p => p.Path));
        Assert.Equal(2, site.Pages[1].Blocks.Count);

        var aboutMenu = site.Pages[1].Blocks[0].Items("menuItems");
        Assert.Equal("Home", aboutMenu[0]["text"]);
        Assert.Equal("About us", aboutMenu[1]["text"]);
        Assert.Equal(true, aboutMenu[1]["active"]);
        Assert.Equal(false, aboutMenu[0]["active"]);
    }

    [Fact]
    public void PageLinks_AreWrittenForEachOutput()
    {
        var paths = SiteBuilder.Build(TwoPages(), new SiteBuildOptions { Links = PageLinkStyle.Paths });
        var files = SiteBuilder.Build(TwoPages(), new SiteBuildOptions { Links = PageLinkStyle.Files });
        Assert.Equal("/about#team", paths.Pages[0].Blocks[1].Str("ctaUrl"));
        Assert.Equal("about.html#team", files.Pages[0].Blocks[1].Str("ctaUrl"));
        Assert.Equal("index.html", files.Pages[1].Blocks[0].Str("homeUrl"));
    }

    [Fact]
    public void StaticSite_HasOneFilePerPage()
    {
        var files = ProjectScaffolder.Build(TwoPages(), new GenerationRequest { ProjectName = "a", Type = ProjectType.StaticHtml });
        Assert.Contains("index.html", files.Keys);
        Assert.Contains("about.html", files.Keys);
        Assert.Contains("<title>About us · Acme</title>", files["about.html"]);
        Assert.Contains("href=\"about.html\"", files["index.html"]);
    }

    [Fact]
    public async Task Generator_WritesEveryPageToDisk()
    {
        var dir = TestData.TempDir();
        try
        {
            var result = await ProjectGenerator.GenerateAsync(new GenerationRequest
            {
                ProjectName = "site", OutputPath = dir, Type = ProjectType.StaticHtml, ProjectData = TwoPages()
            });
            Assert.True(result.Success, result.Message);
            Assert.True(File.Exists(Path.Combine(dir, "site", "index.html")));
            Assert.True(File.Exists(Path.Combine(dir, "site", "about.html")));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void NodeApi_PublishesEveryPage()
    {
        var files = ProjectScaffolder.Build(TwoPages(), new GenerationRequest { ProjectName = "a", Type = ProjectType.NodeJsApi });
        Assert.Contains("public/about.html", files.Keys);
        Assert.Contains("extensions: ['html']", files["src/index.js"]);
    }

    [Fact]
    public void NextJs_GetsADynamicPageRoute()
    {
        var files = ProjectScaffolder.Build(TwoPages(), new GenerationRequest { ProjectName = "a", Type = ProjectType.NextJs });
        Assert.Contains("pages/[slug].jsx", files.Keys);
        Assert.Contains("getStaticPaths", files["pages/[slug].jsx"]);
    }

    [Fact]
    public void Theme_FlowsIntoCssVariablesAndFonts()
    {
        var project = new ProjectModel
        {
            Settings = { [SiteThemes.PrimaryKey] = "#ff0000", [SiteThemes.FontKey] = "Poppins" }
        };
        var site = SiteBuilder.Build(project);
        Assert.Equal("#ff0000", site.Theme.Primary);
        Assert.Equal("#d90000", site.Theme.PrimaryDark);
        Assert.Contains("fonts.googleapis.com", site.Theme.FontUrl);

        var html = ProjectScaffolder.BuildPreviewHtml(project);
        Assert.Contains("--gc-primary:#ff0000", html);
        Assert.Contains("family=Poppins", html);
    }

    [Fact]
    public void InvalidThemeValues_FallBackToDefaults()
    {
        var site = SiteBuilder.Build(new ProjectModel { Settings = { [SiteThemes.PrimaryKey] = "red;}", [SiteThemes.FontKey] = "Comic" } });
        Assert.Equal(SiteThemes.DefaultPrimary, site.Theme.Primary);
        Assert.Equal("System", site.Theme.Font);
    }

    [Fact]
    public void Anchors_AreAddedAndUnique()
    {
        var project = new ProjectModel
        {
            Blocks =
            {
                TestData.Block("ContactForm", 0),
                TestData.Block("ContactForm", 100),
                TestData.Block("Text", 200, b => b.Properties["anchor"] = "#Our Story!")
            }
        };
        var blocks = SiteBuilder.Build(project).Blocks;
        Assert.Equal("contact", blocks[0].Anchor);
        Assert.Equal("", blocks[1].Anchor);
        Assert.Equal("our-story", blocks[2].Anchor);
        Assert.Contains("id=\"contact\"", ProjectScaffolder.BuildPreviewHtml(project));
    }

    [Fact]
    public void HiddenBlocks_OnlyShowInTheEditor()
    {
        var project = new ProjectModel { Blocks = { TestData.Block("Text", 0, b => { b.Content = "secret"; b.Properties["hidden"] = true; }) } };
        Assert.DoesNotContain("secret", ProjectScaffolder.BuildPreviewHtml(project));
        var editor = ProjectScaffolder.BuildEditorHtml(project, null);
        Assert.Contains("secret", editor);
        Assert.Contains("data-gc-hidden", editor);
    }

    [Fact]
    public void LanguageAndFavicon_GoIntoEveryOutput()
    {
        var project = new ProjectModel
        {
            ProjectName = "Сайт",
            Settings = { [SettingKeys.Language] = "uk", [SettingKeys.Favicon] = "assets/icon.png" },
            Blocks = { TestData.Block("Text", 0, b => b.Content = "Привіт, світе") }
        };
        var html = ProjectScaffolder.BuildStaticSite(project)["index.html"];
        Assert.Contains("<html lang=\"uk\">", html);
        Assert.Contains("<link rel=\"icon\" href=\"assets/icon.png\" />", html);
        Assert.Contains("Привіт, світе", html); // readable, not &#1055;...

        var react = ProjectScaffolder.Build(project, new GenerationRequest { ProjectName = "a", Type = ProjectType.React });
        Assert.Contains("<html lang=\"uk\">", react["index.html"]);
        var next = ProjectScaffolder.Build(project, new GenerationRequest { ProjectName = "a", Type = ProjectType.NextJs });
        Assert.Contains("site.language", next["pages/_document.jsx"]);
    }

    [Fact]
    public void BadLanguageCodes_FallBackToEnglish()
    {
        var site = SiteBuilder.Build(new ProjectModel { Settings = { [SettingKeys.Language] = "\"><script>" } });
        Assert.Equal("en", site.Language);
    }

    [Fact]
    public void ConsecutiveCards_ShareARow()
    {
        var project = new ProjectModel
        {
            Blocks =
            {
                TestData.Block("Card", 0), TestData.Block("Card", 100), TestData.Block("Text", 200), TestData.Block("Card", 300)
            }
        };
        var html = ProjectScaffolder.BuildPreviewHtml(project);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(html, "gc-card-row\">"));
        Assert.Equal(3, System.Text.RegularExpressions.Regex.Matches(html, "class=\"gc-block gc-card-block\"").Count);
    }

    [Theory]
    [InlineData("about", null)]
    [InlineData("About", "Use lowercase")]
    [InlineData("api", "reserved")]
    [InlineData("", "cannot be empty")]
    public void PageSlugs_AreValidated(string slug, string? error)
    {
        var project = new ProjectModel();
        ProjectPages.Normalize(project);
        var result = ProjectPages.ValidateSlug(project, slug);
        if (error == null) Assert.Null(result);
        else Assert.Contains(error, result);
    }

    [Theory]
    [MemberData(nameof(TemplateNames))]
    public void Templates_ProduceValidProjects(string template)
    {
        var project = new ProjectModel { ProjectName = "Demo" };
        ProjectTemplates.Apply(project, template);
        var site = SiteBuilder.Build(project);

        if (template == ProjectTemplates.Blank)
        {
            Assert.Empty(site.Blocks);
            return;
        }
        Assert.NotEmpty(site.Blocks);
        if (template == ProjectTemplates.Business)
        {
            Assert.Equal(4, site.Pages.Count);
            Assert.All(site.Pages, p => Assert.NotEmpty(p.Blocks));
            Assert.Equal(4, site.Pages[2].Blocks[0].Items("menuItems").Count);
        }
        foreach (var type in Enum.GetValues<ProjectType>())
        {
            var files = ProjectScaffolder.Build(project, new GenerationRequest { ProjectName = "demo", Type = type });
            foreach (var (_, json) in files.Where(f => f.Key.EndsWith(".json")))
                JsonDocument.Parse(json).Dispose();
        }
    }

    public static IEnumerable<object[]> TemplateNames() => ProjectTemplates.Names.Select(n => new object[] { n });
}
