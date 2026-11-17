namespace GrayCat.UI.Tests;

using System.IO;
using System.Text.Json;
using GrayCat.Core.Generation;
using GrayCat.Core.Models;
using GrayCat.Core.Models.Generation;
using GrayCat.Core.Serialization;
using GrayCat.Shared.Models;
using GrayCat.UI.Services;
using GrayCat.UI.ViewModels;

/// <summary>Drives the editor's view model the way the window does, without any UI.</summary>
public sealed class EditorWorkflowTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "graycat-ui-tests", Guid.NewGuid().ToString("N"));
    private MainViewModel _vm = null!;
    private string _projectFile = null!;

    private ProjectManager NewManager() =>
        // Separate folders for the recent list and work copies so tests never touch the user's settings.
        new(Path.Combine(_root, "settings"), Path.Combine(_root, "work"));

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_root);
        _vm = new MainViewModel(NewManager());

        // Same path as File → New Project.
        var result = await _vm.CreateNewProjectAsync(
            new GenerationRequest { ProjectName = "shop", OutputPath = _root, Type = ProjectType.StaticHtml, Author = "Tester" },
            ProjectTemplates.Landing);
        Assert.True(result.Success, result.Message);
        _projectFile = result.ProjectPath!;
    }

    [Fact]
    public void NewProject_IsOnlyAGraycatFile()
    {
        Assert.Equal(Path.Combine(_root, "shop.graycat"), _projectFile);
        Assert.True(File.Exists(_projectFile));
        Assert.False(Directory.Exists(Path.Combine(_root, "shop")), "no code before Generate / Run / Build");
        Assert.Equal(Path.Combine(_root, "shop"), _vm.ProjectFolder);
    }

    [Fact]
    public async Task Reopen_KeepsPagesSettingsAndImages()
    {
        var image = Path.Combine(_root, "photo.png");
        File.WriteAllBytes(image, new byte[] { 7, 7, 7 });
        var assetPath = _vm.ImportAsset(image);
        _vm.Blocks.First(b => b.Type == "Hero").SetBlockProperty("backgroundImage", assetPath);
        _vm.AddPage("About", "about");
        _vm.SetSetting(SettingKeys.Language, "uk");
        Assert.True(await _vm.SaveProjectAsync());
        _vm.Shutdown(); // closes the project and removes its work copy

        var second = new MainViewModel(NewManager());
        await second.LoadProjectAsync(_projectFile);
        try
        {
            Assert.Equal(2, second.Pages.Count);
            Assert.Equal("uk", second.CurrentProject!.Settings[SettingKeys.Language]);
            Assert.Equal(new byte[] { 7, 7, 7 }, File.ReadAllBytes(Path.Combine(second.WorkFolder!, assetPath)));

            // The image travels into generated code too.
            var result = await second.GenerateCodeAsync();
            Assert.True(result.Success, result.Message);
            Assert.True(File.Exists(Path.Combine(second.ProjectFolder!, assetPath)));
        }
        finally
        {
            second.Shutdown();
        }
    }

    [Fact]
    public void ListBlocks_ShowASummaryOnTheCanvas()
    {
        var stats = _vm.AddBlock("Stats", 0, 0)!;
        Assert.Equal("1000+ Happy Clients · 50+ Projects Completed · 24/7 Support", stats.DisplayText);
        var footer = new BlockViewModel(new BlockModel { Type = "Footer" });
        footer.Model.InitializeDefaultProperties("en");
        Assert.Equal("Privacy · Terms · Contact", footer.DisplayText);
    }

    [Fact]
    public async Task SaveAs_ContinuesInTheNewFile_AndKeepsImages()
    {
        var image = Path.Combine(_root, "logo.png");
        File.WriteAllBytes(image, new byte[] { 1, 2, 3 });
        var assetPath = _vm.ImportAsset(image);
        Assert.True(await _vm.SaveProjectAsync());
        var before = File.ReadAllBytes(_projectFile);

        var copyPath = Path.Combine(_root, "shop 2.graycat");
        Assert.True(await _vm.SaveProjectAsAsync(copyPath));
        Assert.Equal(copyPath, _vm.DocumentPath);
        Assert.Equal("shop 2", _vm.ProjectName); // named after the file, so it follows it
        Assert.Equal(Path.Combine(_root, "shop 2"), _vm.ProjectFolder);

        // Further edits go to the copy; the original stays as it was.
        _vm.AddPage("About", "about");
        Assert.True(await _vm.SaveProjectAsync());
        Assert.Equal(before, File.ReadAllBytes(_projectFile));

        var reopened = new MainViewModel(NewManager());
        await reopened.LoadProjectAsync(copyPath);
        try
        {
            Assert.Equal(2, reopened.Pages.Count);
            Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(Path.Combine(reopened.WorkFolder!, assetPath)));
        }
        finally
        {
            reopened.Shutdown();
        }
    }

    [Fact]
    public async Task LegacyProject_IsConvertedToGraycat()
    {
        // GrayCat 1.0 layout: Sites\Old\graycat.project.json (+ assets) with the code in the same folder.
        var sites = Path.Combine(_root, "Sites");
        var old = Path.Combine(sites, "Old");
        Directory.CreateDirectory(Path.Combine(old, "assets"));
        File.WriteAllBytes(Path.Combine(old, "assets", "logo.png"), new byte[] { 1 });
        var legacy = new ProjectModel { ProjectName = "Old site", Blocks = { new BlockModel { Type = "Text", Content = "Hello" } } };
        var legacyFile = Path.Combine(old, ProjectScaffolder.ProjectFileName);
        File.WriteAllText(legacyFile, JsonSerializer.Serialize(legacy, GrayCatJson.Options));

        var vm = new MainViewModel(NewManager());
        try
        {
            await vm.LoadProjectAsync(legacyFile);
            Assert.Equal(Path.Combine(sites, "Old.graycat"), vm.DocumentPath);
            Assert.True(File.Exists(vm.DocumentPath));
            Assert.True(File.Exists(legacyFile), "the old file is kept");
            Assert.Equal(old, vm.ProjectFolder); // code keeps going to the same folder
            Assert.True(File.Exists(Path.Combine(vm.WorkFolder!, "assets", "logo.png")));

            // Regenerating into the old folder is allowed (it is a GrayCat folder).
            var result = await vm.GenerateCodeAsync();
            Assert.True(result.Success, result.Message);
        }
        finally
        {
            vm.Shutdown();
        }
    }

    [Fact]
    public async Task CreatingOverAnExistingFile_IsRefused()
    {
        var vm = new MainViewModel(NewManager());
        var result = await vm.CreateNewProjectAsync(
            new GenerationRequest { ProjectName = "shop", OutputPath = _root, Type = ProjectType.React }, ProjectTemplates.Blank);
        Assert.False(result.Success);
        Assert.Contains("already exists", result.Message);
        vm.Shutdown();
    }

    public Task DisposeAsync()
    {
        _vm.Shutdown();
        try { Directory.Delete(_root, true); } catch (IOException) { }
        return Task.CompletedTask;
    }

    private string[] Order() => _vm.Blocks.OrderBy(b => b.PositionY).ThenBy(b => b.PositionX).Select(b => b.Type).ToArray();

    [Fact]
    public void LoadsTheTemplate()
    {
        Assert.True(_vm.IsProjectOpen);
        Assert.Single(_vm.Pages);
        Assert.Equal("Navbar", Order()[0]);
        Assert.Equal("Footer", Order()[^1]);
    }

    [Fact]
    public void InsertAfterSelected_KeepsPageOrder()
    {
        var hero = _vm.Blocks.First(b => b.Type == "Hero");
        _vm.SelectedBlock = hero;
        _vm.AddBlockAfter("Text", hero);

        var order = Order();
        Assert.Equal("Text", order[Array.IndexOf(order, "Hero") + 1]);
        Assert.Equal("Text", _vm.SelectedBlock!.Type);
    }

    [Fact]
    public void MoveInOrder_SwapsNeighbours()
    {
        var stats = _vm.Blocks.First(b => b.Type == "Stats");
        var before = Order();
        _vm.MoveInOrder(stats, -1);
        var after = Order();
        var i = Array.IndexOf(before, "Stats");
        Assert.Equal("Stats", after[i - 1]);
        Assert.Equal(before[i - 1], after[i]);
    }

    [Fact]
    public void UndoRedo_RestoreBlocksAndEdits()
    {
        var count = _vm.Blocks.Count;
        _vm.AddBlockAfter("Divider", null);
        Assert.Equal(count + 1, _vm.Blocks.Count);

        _vm.Undo();
        Assert.Equal(count, _vm.Blocks.Count);
        _vm.Redo();
        Assert.Equal(count + 1, _vm.Blocks.Count);

        var hero = _vm.Blocks.First(b => b.Type == "Hero");
        var original = PropertyValues.AsString(hero.GetBlockProperty("headline"));
        hero.SetBlockProperty("headline", "Changed");
        _vm.Undo();
        Assert.Equal(original, PropertyValues.AsString(_vm.Blocks.First(b => b.Type == "Hero").GetBlockProperty("headline")));
    }

    [Fact]
    public void Pages_AddSwitchRenameDelete()
    {
        Assert.Null(_vm.AddPage("About us", "about"));
        Assert.Equal("about", _vm.CurrentPageSlug);
        Assert.Equal(new[] { "Navbar", "Footer" }, Order()); // copied from the home page

        _vm.AddBlockAfter("Text", null);
        Assert.Equal(3, _vm.Blocks.Count);
        Assert.All(_vm.Blocks, b => Assert.Equal("about", b.Model.Page));

        // Address taken / invalid.
        Assert.NotNull(_vm.AddPage("Again", "about"));
        Assert.NotNull(_vm.AddPage("Bad", "Not Valid"));

        // Renaming the address moves the blocks with the page.
        var about = _vm.CurrentPage!;
        Assert.Null(_vm.UpdatePage(about, "Team", "team", showInMenu: true));
        Assert.Equal("team", _vm.CurrentPageSlug);
        Assert.All(_vm.Blocks, b => Assert.Equal("team", b.Model.Page));

        // Switching pages swaps the blocks.
        _vm.CurrentPage = _vm.Pages[0];
        Assert.Contains(_vm.Blocks, b => b.Type == "Hero");

        _vm.DeletePage(_vm.Pages[1]);
        Assert.Single(_vm.Pages);
        Assert.DoesNotContain(_vm.CurrentProject!.Blocks, b => b.Page == "team");

        // Deleting a page can be undone, blocks included.
        _vm.Undo();
        Assert.Equal(2, _vm.Pages.Count);
        Assert.Contains(_vm.CurrentProject!.Blocks, b => b.Page == "team");
    }

    [Fact]
    public void HomePage_CannotBeDeletedOrReaddressed()
    {
        var home = _vm.Pages[0];
        _vm.DeletePage(home);
        Assert.Single(_vm.Pages);
        Assert.Null(_vm.UpdatePage(home, "Start", "whatever", true));
        Assert.Equal("", home.Slug);
        Assert.Equal("Start", home.Title);
    }

    [Fact]
    public async Task Save_WritesPagesThemeAndBlocks()
    {
        _vm.AddPage("Contact", "contact");
        _vm.SetSetting(SiteThemes.PrimaryKey, "#0f766e");
        Assert.True(await _vm.SaveProjectAsync());

        var saved = GrayCatDocument.Open(_projectFile, Path.Combine(_root, "check"));
        Assert.Equal(2, saved.Pages.Count);
        Assert.Equal("#0f766e", saved.Settings[SiteThemes.PrimaryKey]);
        Assert.Contains(saved.Blocks, b => b.Page == "contact" && b.Type == "Navbar");
    }

    [Fact]
    public async Task GenerateCode_WritesEveryPage()
    {
        _vm.AddPage("Contact", "contact");
        var result = await _vm.GenerateCodeAsync();
        Assert.True(result.Success, result.Message);
        Assert.True(File.Exists(Path.Combine(_vm.ProjectFolder!, "contact.html")));
    }

    [Fact]
    public async Task ExportZip_SkipsInstalledPackages()
    {
        Assert.True((await _vm.GenerateCodeAsync()).Success); // as after Run App: code + installed packages
        Directory.CreateDirectory(Path.Combine(_vm.ProjectFolder!, "node_modules", "x"));
        File.WriteAllText(Path.Combine(_vm.ProjectFolder!, "node_modules", "x", "big.js"), "x");
        var zip = Path.Combine(_root, "out.zip");

        await _vm.ExportZipAsync(zip);

        using var archive = System.IO.Compression.ZipFile.OpenRead(zip);
        Assert.Contains(archive.Entries, e => e.FullName == "index.html");
        Assert.DoesNotContain(archive.Entries, e => e.FullName.StartsWith("node_modules"));
    }

    [Fact]
    public async Task BuildForProduction_StaticSiteNeedsNoBuild()
    {
        var result = await _vm.BuildForProductionAsync();
        Assert.True(result.Success, result.Message);
        Assert.Equal(_vm.ProjectFolder, result.Target);
        Assert.True(File.Exists(Path.Combine(_vm.ProjectFolder!, "index.html")));
    }

    [Fact]
    public async Task Preview_WritesAllPagesAndReturnsTheCurrentOne()
    {
        _vm.AddPage("About", "about");
        var path = await _vm.WritePreviewAsync();
        Assert.EndsWith("about.html", path);
        Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(path)!, "index.html")));
    }
}
