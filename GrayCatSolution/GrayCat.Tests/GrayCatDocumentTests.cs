namespace GrayCat.Tests;

using System.IO.Compression;
using System.Text;
using GrayCat.Core.Generation;
using GrayCat.Core.Models;
using GrayCat.Core.Models.Generation;
using GrayCat.Shared.Models;

public class GrayCatDocumentTests : IDisposable
{
    private readonly string _dir = TestData.TempDir();

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private string Work(string name)
    {
        var path = Path.Combine(_dir, name);
        Directory.CreateDirectory(path);
        return path;
    }

    [Fact]
    public void SaveAndOpen_RoundTripsProjectAndAssets()
    {
        var work = Work("work1");
        Directory.CreateDirectory(Path.Combine(work, "assets", "photos"));
        File.WriteAllBytes(Path.Combine(work, "assets", "photos", "cat.png"), new byte[] { 1, 2, 3, 4 });

        var project = new ProjectModel { ProjectName = "Кіт", Type = ProjectType.NextJs };
        ProjectTemplates.Apply(project, ProjectTemplates.Business);
        var file = Path.Combine(_dir, "Cat.graycat");
        GrayCatDocument.Save(project, work, file);

        var reopenedWork = Work("work2");
        var reopened = GrayCatDocument.Open(file, reopenedWork);
        Assert.Equal("Кіт", reopened.ProjectName);
        Assert.Equal(ProjectType.NextJs, reopened.Type);
        Assert.Equal(4, reopened.Pages.Count);
        Assert.Equal(project.Blocks.Count, reopened.Blocks.Count);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, File.ReadAllBytes(Path.Combine(reopenedWork, "assets", "photos", "cat.png")));
        Assert.False(File.Exists(file + ".saving"));
    }

    [Fact]
    public void Package_HasManifestProjectAndAssetsOnly()
    {
        var work = Work("w");
        Directory.CreateDirectory(Path.Combine(work, "assets"));
        File.WriteAllText(Path.Combine(work, "assets", "a.svg"), "<svg/>");
        File.WriteAllText(Path.Combine(work, "stray.txt"), "not part of the project");
        var file = Path.Combine(_dir, "p.graycat");
        GrayCatDocument.Save(new ProjectModel(), work, file);

        using var zip = ZipFile.OpenRead(file);
        Assert.Equal(new[] { "assets/a.svg", "graycat.json", "project.json" }, zip.Entries.Select(e => e.FullName).OrderBy(n => n));
    }

    [Fact]
    public void Open_IgnoresEntriesThatEscapeTheWorkFolder()
    {
        var file = Path.Combine(_dir, "evil.graycat");
        using (var zip = ZipFile.Open(file, ZipArchiveMode.Create))
        {
            Write(zip, "project.json", "{\"ProjectName\":\"x\"}");
            Write(zip, "assets/../../escaped.txt", "bad");
            Write(zip, "assets/ok.txt", "good");
        }

        var work = Work("evil-work");
        GrayCatDocument.Open(file, work);
        Assert.True(File.Exists(Path.Combine(work, "assets", "ok.txt")));
        Assert.False(File.Exists(Path.Combine(_dir, "escaped.txt")));
        Assert.False(File.Exists(Path.Combine(work, "escaped.txt")));
    }

    [Fact]
    public void Open_RejectsNewerFormatsAndOtherFiles()
    {
        var newer = Path.Combine(_dir, "newer.graycat");
        using (var zip = ZipFile.Open(newer, ZipArchiveMode.Create))
        {
            Write(zip, "graycat.json", "{\"formatVersion\": 99}");
            Write(zip, "project.json", "{}");
        }
        var ex = Assert.Throws<InvalidDataException>(() => GrayCatDocument.Open(newer, Work("a")));
        Assert.Contains("newer version", ex.Message);

        var text = Path.Combine(_dir, "notes.graycat");
        File.WriteAllText(text, "hello");
        Assert.Throws<InvalidDataException>(() => GrayCatDocument.Open(text, Work("b")));
    }

    [Fact]
    public async Task Generator_TakesAssetsFromTheUnpackedProject()
    {
        var work = Work("assets-src");
        Directory.CreateDirectory(Path.Combine(work, "assets"));
        File.WriteAllBytes(Path.Combine(work, "assets", "logo.png"), new byte[] { 9 });

        foreach (var (type, expected) in new[] { (ProjectType.React, "public/assets/logo.png"), (ProjectType.StaticHtml, "assets/logo.png") })
        {
            var result = await ProjectGenerator.GenerateAsync(new GenerationRequest
            {
                ProjectName = "out-" + type, OutputPath = _dir, Type = type, AssetsDirectory = work
            });
            Assert.True(result.Success, result.Message);
            Assert.True(File.Exists(Path.Combine(result.ProjectPath!, expected)), $"{type}: {expected}");
        }
    }

    private static void Write(ZipArchive zip, string name, string text)
    {
        using var writer = new StreamWriter(zip.CreateEntry(name).Open(), Encoding.UTF8);
        writer.Write(text);
    }
}
