namespace GrayCat.Tests;

using System.Text.Json;
using GrayCat.Core.Generation;
using GrayCat.Core.Models;
using GrayCat.Core.Models.Generation;
using GrayCat.Core.Serialization;
using GrayCat.Shared.Models;

public class GenerationTests
{
    public static IEnumerable<object[]> AllTypes() => Enum.GetValues<ProjectType>().Select(t => new object[] { t });

    [Theory]
    [MemberData(nameof(AllTypes))]
    public void EveryProjectType_ProducesItsEntryFiles(ProjectType type)
    {
        var project = TestData.FullProject(type);
        var files = ProjectScaffolder.Build(project, new GenerationRequest { ProjectName = "demo", Type = type });

        Assert.Contains(ProjectScaffolder.OutputMarker, files.Keys);
        Assert.DoesNotContain(ProjectScaffolder.ProjectFileName, files.Keys); // the design lives in .graycat, not in the code
        var expected = type switch
        {
            ProjectType.StaticHtml => new[] { "index.html", "styles.css", "script.js" },
            ProjectType.React => new[] { "package.json", "index.html", "vite.config.js", "src/main.jsx", "src/App.jsx", "src/data/site.json", "src/components/index.jsx" },
            ProjectType.NextJs => new[] { "package.json", "pages/index.jsx", "pages/_app.jsx", "pages/api/[form].js", "data/site.json", "components/index.jsx" },
            ProjectType.NodeJsApi => new[] { "package.json", "src/index.js", "src/routes/forms.js", "src/storage.js", "public/index.html" },
            ProjectType.ReactNative => new[] { "package.json", "app.json", "index.js", "App.js", "src/blocks.js", "src/data/site.json" },
            _ => throw new InvalidOperationException()
        };
        foreach (var file in expected)
            Assert.Contains(file, files.Keys);

        // Every JSON file must parse.
        foreach (var (name, content) in files.Where(f => f.Key.EndsWith(".json")))
            JsonDocument.Parse(content).Dispose();
    }

    [Fact]
    public void SiteJson_HasNoUnknownTypes_AndKeepsBlockData()
    {
        var files = ProjectScaffolder.Build(TestData.FullProject(), new GenerationRequest { ProjectName = "demo", Type = ProjectType.React });
        using var doc = JsonDocument.Parse(files["src/data/site.json"]);
        var blocks = doc.RootElement.GetProperty("pages")[0].GetProperty("blocks");
        Assert.Equal(BlockDefaults.BlockTypes.Count, blocks.GetArrayLength());
        var navbar = blocks.EnumerateArray().First(b => b.GetProperty("type").GetString() == "Navbar");
        Assert.Equal(JsonValueKind.Array, navbar.GetProperty("props").GetProperty("menuItems").ValueKind);
    }

    [Fact]
    public void NodeApi_UsesSqliteOnlyWhenAsked()
    {
        var project = TestData.FullProject(ProjectType.NodeJsApi);
        var json = ProjectScaffolder.Build(project, new GenerationRequest { ProjectName = "a", Type = ProjectType.NodeJsApi });
        var sqlite = ProjectScaffolder.Build(project, new GenerationRequest { ProjectName = "a", Type = ProjectType.NodeJsApi, UseSQLite = true });
        Assert.DoesNotContain("sqlite3", json["package.json"]);
        Assert.Contains("sqlite3", sqlite["package.json"]);
    }

    [Theory]
    [InlineData("My Cool Site!", "my-cool-site")]
    [InlineData("Сайт", "graycat-site")]
    [InlineData("  ok_name ", "ok_name")]
    public void PackageName_IsNpmSafe(string input, string expected) =>
        Assert.Equal(expected, ProjectScaffolder.PackageName(input));

    [Fact]
    public async Task Generate_WritesProject_AndTheProjectFileReopens()
    {
        var dir = TestData.TempDir();
        try
        {
            var request = new GenerationRequest
            {
                ProjectName = "Site One",
                OutputPath = dir,
                Type = ProjectType.StaticHtml,
                ProjectData = TestData.FullProject(ProjectType.StaticHtml)
            };
            var result = await ProjectGenerator.GenerateAsync(request);

            Assert.True(result.Success, result.Message);
            Assert.True(File.Exists(Path.Combine(result.ProjectPath!, ProjectScaffolder.OutputMarker)));
            Assert.False(File.Exists(Path.Combine(result.ProjectPath!, ProjectScaffolder.ProjectFileName)));
            Assert.True(File.Exists(Path.Combine(result.ProjectPath!, "index.html")));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task Generate_RefusesNonEmptyFolder_UnlessRegeneratingAGrayCatProject()
    {
        var dir = TestData.TempDir();
        try
        {
            var request = new GenerationRequest { ProjectName = "p", OutputPath = dir, Type = ProjectType.React };
            Assert.True((await ProjectGenerator.GenerateAsync(request)).Success);

            // Second create into the same folder fails...
            Assert.False((await ProjectGenerator.GenerateAsync(request)).Success);

            // ...but regenerating works.
            request.Overwrite = true;
            Assert.True((await ProjectGenerator.GenerateAsync(request)).Success);

            // Overwrite never applies to folders that are not GrayCat projects.
            var foreign = Path.Combine(dir, "foreign");
            Directory.CreateDirectory(foreign);
            File.WriteAllText(Path.Combine(foreign, "keep.txt"), "x");
            var bad = await ProjectGenerator.GenerateAsync(new GenerationRequest { ProjectName = "foreign", OutputPath = dir, Type = ProjectType.React, Overwrite = true });
            Assert.False(bad.Success);
            Assert.Single(Directory.GetFiles(foreign));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("CON")]
    [InlineData("a/b")]
    [InlineData("..")]
    [InlineData("name.")]
    public void InvalidProjectNames_AreRejected(string name) =>
        Assert.NotNull(GenerationValidator.ValidateProjectName(name));

    [Fact]
    public void SystemFolders_AreRejected()
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (string.IsNullOrEmpty(windows)) return;
        Assert.NotNull(GenerationValidator.ValidateOutputPath(Path.Combine(windows, "Temp")));
        Assert.NotNull(GenerationValidator.ValidateOutputPath("relative\\path"));
        Assert.Null(GenerationValidator.ValidateOutputPath(Path.GetTempPath()));
    }

    /// <summary>
    /// Writes the multi-page Business template (custom theme, Ukrainian content) for every project type to GRAYCAT_E2E_PAGES_DIR.
    /// Skipped (passes) when the variable is not set.
    /// </summary>
    [Fact]
    public async Task WriteMultiPageProjects_ForNpmVerification()
    {
        var dir = Environment.GetEnvironmentVariable("GRAYCAT_E2E_PAGES_DIR");
        if (string.IsNullOrEmpty(dir)) return;

        foreach (var type in Enum.GetValues<ProjectType>())
        {
            var project = new ProjectModel
            {
                ProjectName = "Northwind Studio",
                Settings =
                {
                    [SiteThemes.PrimaryKey] = "#0f766e", [SiteThemes.AccentKey] = "#0ea5e9", [SiteThemes.FontKey] = "Poppins",
                    [SettingKeys.Language] = "uk" // also checks that Ukrainian starter texts survive every toolchain
                }
            };
            ProjectTemplates.Apply(project, ProjectTemplates.Business, "uk");
            var name = "biz-" + type;
            // Existing folders are regenerated in place, which keeps node_modules between runs.
            var target = Path.Combine(dir, name);
            var result = await ProjectGenerator.GenerateAsync(new GenerationRequest
            {
                ProjectName = name, OutputPath = dir, Type = type, Author = "E2E", ProjectData = project, Overwrite = Directory.Exists(target)
            });
            Assert.True(result.Success, result.Message);
        }
    }

    /// <summary>
    /// Writes every project type to GRAYCAT_E2E_DIR so they can be installed and built with npm.
    /// Skipped (passes) when the variable is not set.
    /// </summary>
    [Fact]
    public async Task WriteSampleProjects_ForNpmVerification()
    {
        var dir = Environment.GetEnvironmentVariable("GRAYCAT_E2E_DIR");
        if (string.IsNullOrEmpty(dir)) return;

        foreach (var type in Enum.GetValues<ProjectType>())
        {
            foreach (var sqlite in type == ProjectType.NodeJsApi ? new[] { false, true } : new[] { false })
            {
                var name = sqlite ? $"{type}-sqlite" : type.ToString();
                var target = Path.Combine(dir, name);
                if (Directory.Exists(target)) Directory.Delete(target, true);
                var result = await ProjectGenerator.GenerateAsync(new GenerationRequest
                {
                    ProjectName = name,
                    OutputPath = dir,
                    Type = type,
                    UseSQLite = sqlite,
                    Author = "E2E",
                    ProjectData = TestData.FullProject(type)
                });
                Assert.True(result.Success, result.Message);
            }
        }
    }
}
