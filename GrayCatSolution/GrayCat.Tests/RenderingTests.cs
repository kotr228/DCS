namespace GrayCat.Tests;

using GrayCat.Core.Generation;
using GrayCat.Core.Models;

public class RenderingTests
{
    [Fact]
    public void Blocks_AreOrderedTopToBottomThenLeftToRight()
    {
        var project = new ProjectModel
        {
            Blocks =
            {
                TestData.Block("Footer", 500),
                TestData.Block("Hero", 100, b => b.PositionX = 300),
                TestData.Block("Navbar", 100, b => b.PositionX = 10)
            }
        };
        var site = SiteBuilder.Build(project);
        Assert.Equal(new[] { "Navbar", "Hero", "Footer" }, site.Blocks.Select(b => b.Type));
    }

    [Fact]
    public void Html_IsEscaped()
    {
        var project = new ProjectModel
        {
            Blocks = { TestData.Block("Text", 0, b => b.Content = "<script>alert(1)</script>") }
        };
        var html = ProjectScaffolder.BuildPreviewHtml(project);
        Assert.DoesNotContain("<script>alert(1)</script>", html);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", html);
    }

    [Fact]
    public void ScriptUrls_AreNeutralised()
    {
        var project = new ProjectModel
        {
            Blocks = { TestData.Block("Button", 0, b => b.Properties["url"] = " JavaScript:alert(1)") }
        };
        var site = SiteBuilder.Build(project);
        Assert.Equal("#", site.Blocks[0].Str("url"));
    }

    [Fact]
    public void PlaceholderContent_IsDropped()
    {
        var project = new ProjectModel { Blocks = { TestData.Block("Hero", 0, b => b.Content = "New Hero block") } };
        Assert.Equal("", SiteBuilder.Build(project).Blocks[0].Content);
    }

    [Fact]
    public void DefaultEditorStyles_AreNotForcedOnTheSite()
    {
        var site = SiteBuilder.Build(new ProjectModel { Blocks = { TestData.Block("Hero", 0) } });
        Assert.Empty(site.Blocks[0].Style);
    }

    [Fact]
    public void CustomStyles_AreApplied()
    {
        var site = SiteBuilder.Build(new ProjectModel
        {
            Blocks = { TestData.Block("Text", 0, b => { b.Background = "#112233"; b.FontSize = 20; b.TextAlignment = "Center"; }) }
        });
        var html = HtmlRenderer.RenderBlock(site.Blocks[0]);
        Assert.Contains("background:#112233", html);
        Assert.Contains("font-size:20px", html);
        Assert.Contains("text-align:center", html);
    }

    [Theory]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ", "https://www.youtube.com/embed/dQw4w9WgXcQ")]
    [InlineData("https://youtu.be/dQw4w9WgXcQ", "https://www.youtube.com/embed/dQw4w9WgXcQ")]
    [InlineData("https://vimeo.com/76979871", "https://player.vimeo.com/video/76979871")]
    public void VideoUrls_BecomeEmbeds(string url, string embed)
    {
        var site = SiteBuilder.Build(new ProjectModel { Blocks = { TestData.Block("Video", 0, b => b.Properties["videoUrl"] = url) } });
        Assert.Equal(embed, site.Blocks[0].Str("embedUrl"));
        Assert.False(site.Blocks[0].Bool("isFile"));
    }

    [Fact]
    public void ContactForm_AddsPhoneField_WhenEnabled()
    {
        var site = SiteBuilder.Build(new ProjectModel { Blocks = { TestData.Block("ContactForm", 0) } });
        var names = site.Blocks[0].Items("fields").Select(f => f["name"]).ToList();
        Assert.Equal(new object?[] { "name", "email", "phone", "message" }, names);
    }

    [Fact]
    public void ListTypedAsPlainText_StillRenders()
    {
        var site = SiteBuilder.Build(new ProjectModel
        {
            Blocks = { TestData.Block("Pricing", 0, b => b.Properties["features"] = "Fast\nCheap\nGood") }
        });
        Assert.Equal(new[] { "Fast", "Cheap", "Good" }, site.Blocks[0].Strings("features"));
    }

    [Fact]
    public void EveryBlockType_RendersWithoutErrors()
    {
        var html = ProjectScaffolder.BuildPreviewHtml(TestData.FullProject());
        foreach (var cls in new[] { "gc-navbar", "gc-hero", "gc-card", "gc-gallery", "gc-form", "gc-video-frame", "gc-price-card", "gc-accordion-item", "gc-stat", "gc-social-list", "gc-footer" })
            Assert.Contains(cls, html);
    }

    [Fact]
    public void ReactLibrary_RegistersEveryBlockType()
    {
        var index = ReactLibrary.Components["index.jsx"];
        foreach (var type in BlockDefaults.BlockTypes)
            Assert.Contains($"  {type}: ", index);
    }
}
