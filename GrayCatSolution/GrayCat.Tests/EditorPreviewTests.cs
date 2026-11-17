namespace GrayCat.Tests;

using System.Text.Json;
using GrayCat.Core.Generation;
using GrayCat.Core.Models;
using GrayCat.Core.Models.Generation;
using GrayCat.Shared.Models;

public class EditorPreviewTests
{
    [Fact]
    public void EditorPage_MarksEveryBlockWithItsEditorId()
    {
        var project = TestData.FullProject();
        var html = ProjectScaffolder.BuildEditorHtml(project, null);

        foreach (var block in project.Blocks)
            Assert.Contains($"data-gc-id=\"{block.Id}\"", html);
        Assert.Contains("window.gcUpdate", html);
        Assert.Contains("chrome.webview.postMessage", html);
    }

    [Fact]
    public void EditorBody_MatchesThePublishedMarkup()
    {
        var project = TestData.FullProject();
        var body = ProjectScaffolder.BuildEditorBody(project);
        var published = HtmlRenderer.RenderBlocks(SiteBuilder.Build(project, new SiteBuildOptions { Links = PageLinkStyle.Files }));

        // Same page, only the editor markers added.
        var stripped = System.Text.RegularExpressions.Regex.Replace(body, " data-gc-id=\"[^\"]*\" data-gc-type=\"[^\"]*\"", "");
        Assert.Equal(published, stripped);
    }

    [Fact]
    public void EditorIds_AreNotWrittenToSiteJson()
    {
        var files = ProjectScaffolder.Build(TestData.FullProject(), new GenerationRequest { ProjectName = "a", Type = ProjectType.React });
        Assert.DoesNotContain("editorId", files["src/data/site.json"], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Regenerating_OnlyTouchesFilesThatChanged()
    {
        var dir = TestData.TempDir();
        try
        {
            var project = TestData.FullProject(ProjectType.React);
            var request = new GenerationRequest { ProjectName = "app", OutputPath = dir, Type = ProjectType.React, ProjectData = project };
            Assert.True((await ProjectGenerator.GenerateAsync(request)).Success);

            request.Overwrite = true;
            var unchanged = await ProjectGenerator.GenerateAsync(request);
            Assert.Equal(0, unchanged.FilesChanged);

            project.Blocks.First(b => b.Type == "Hero").Properties["headline"] = "New headline";
            var changed = await ProjectGenerator.GenerateAsync(request);
            // Only site.json; components stay untouched so dev servers reload only data.
            Assert.Equal(1, changed.FilesChanged);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void ReactNative_CanRunInTheBrowser()
    {
        var files = ProjectScaffolder.Build(new ProjectModel(), new GenerationRequest { ProjectName = "a", Type = ProjectType.ReactNative });
        using var doc = JsonDocument.Parse(files["package.json"]);
        var deps = doc.RootElement.GetProperty("dependencies");
        Assert.True(deps.TryGetProperty("react-dom", out _));
        Assert.True(deps.TryGetProperty("react-native-web", out _));
        Assert.True(deps.TryGetProperty("@expo/metro-runtime", out _));
    }
}
