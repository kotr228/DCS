namespace GrayCat.Tests;

using GrayCat.Core.Generation;
using GrayCat.Core.Models;
using GrayCat.Core.Models.Generation;
using GrayCat.Shared.Models;

public class AssetTests
{
    private static string MakeImage(string dir, string name, byte fill)
    {
        var path = Path.Combine(dir, name);
        File.WriteAllBytes(path, Enumerable.Repeat(fill, 64).ToArray());
        return path;
    }

    [Fact]
    public void Import_CopiesIntoAssets_AndAvoidsNameClashes()
    {
        var project = TestData.TempDir();
        var outside = TestData.TempDir();
        try
        {
            var first = ProjectAssets.Import(project, MakeImage(outside, "My Photo.PNG", 1));
            Assert.Equal("assets/my-photo.png", first);

            // Same file again: reused, not duplicated.
            Assert.Equal(first, ProjectAssets.Import(project, Path.Combine(outside, "My Photo.PNG")));

            // Different file, same name: gets a new name.
            var other = TestData.TempDir();
            var second = ProjectAssets.Import(project, MakeImage(other, "My Photo.PNG", 2));
            Assert.Equal("assets/my-photo-2.png", second);

            // A file already inside assets keeps its path.
            Assert.Equal(first, ProjectAssets.Import(project, Path.Combine(project, "assets", "my-photo.png")));
            Assert.Equal(2, ProjectAssets.List(project).Count);
        }
        finally
        {
            Directory.Delete(project, true);
            Directory.Delete(outside, true);
        }
    }

    [Theory]
    [InlineData(ProjectType.React, "public/assets/logo.png")]
    [InlineData(ProjectType.NextJs, "public/assets/logo.png")]
    [InlineData(ProjectType.NodeJsApi, "public/assets/logo.png")]
    [InlineData(ProjectType.StaticHtml, "assets/logo.png")]
    public async Task Regenerate_PublishesAssetsWhereTheTypeServesThem(ProjectType type, string expected)
    {
        var dir = TestData.TempDir();
        try
        {
            var request = new GenerationRequest { ProjectName = "site", OutputPath = dir, Type = type };
            Assert.True((await ProjectGenerator.GenerateAsync(request)).Success);

            var projectDir = Path.Combine(dir, "site");
            Directory.CreateDirectory(Path.Combine(projectDir, "assets"));
            MakeImage(Path.Combine(projectDir, "assets"), "logo.png", 7);

            request.Overwrite = true;
            Assert.True((await ProjectGenerator.GenerateAsync(request)).Success);
            Assert.True(File.Exists(Path.Combine(projectDir, expected)));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void ReactNative_RequiresLocalImages()
    {
        var files = ProjectScaffolder.Build(new ProjectModel(), new GenerationRequest { ProjectName = "a", Type = ProjectType.ReactNative },
            new[] { "assets/logo.png", "assets/icon.svg" });
        var map = files["src/assets.js"];
        Assert.Contains("\"assets/logo.png\": require(\"../assets/logo.png\")", map);
        Assert.DoesNotContain("icon.svg", map); // the native bundler cannot load SVG with require()
    }

    [Fact]
    public void Preview_ResolvesAssetsAgainstTheProjectFolder()
    {
        var project = new ProjectModel { Blocks = { TestData.Block("Image", 0, b => b.Properties["src"] = "assets/photo.png") } };
        var html = ProjectScaffolder.BuildPreviewHtml(project, @"C:\Sites\Demo");
        Assert.Contains("src=\"file:///C:/Sites/Demo/assets/photo.png\"", html);
        // The published site keeps the relative path.
        Assert.Contains("src=\"assets/photo.png\"", ProjectScaffolder.BuildStaticSite(project)["index.html"]);
    }

    [Fact]
    public void DescriptionSetting_IsUsedForMetadata()
    {
        var project = new ProjectModel { ProjectName = "Shop", Settings = { [SettingKeys.Description] = "Handmade mugs" } };
        Assert.Equal("Handmade mugs", SiteBuilder.Build(project).Description);
        Assert.Contains("content=\"Handmade mugs\"", ProjectScaffolder.BuildPreviewHtml(project));
    }
}
