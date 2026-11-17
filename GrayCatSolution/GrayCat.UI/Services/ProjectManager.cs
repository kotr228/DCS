namespace GrayCat.UI.Services;

using GrayCat.Core.Constants;
using GrayCat.Core.Generation;
using GrayCat.Core.Models;
using GrayCat.Core.Serialization;
using System.IO;
using System.Text;
using System.Text.Json;

/// <summary>An open project: the .graycat file, where it is unpacked while editing, and where its code goes.</summary>
public sealed class OpenedProject
{
    public required ProjectModel Project { get; init; }

    /// <summary>The .graycat file the project is saved to.</summary>
    public required string DocumentPath { get; init; }

    /// <summary>Temporary folder holding the unpacked assets while the project is open.</summary>
    public required string WorkFolder { get; init; }

    /// <summary>Folder for generated code: next to the .graycat file, with the same name.</summary>
    public string OutputFolder => ProjectManager.OutputFolderFor(DocumentPath);

    /// <summary>The project was opened from a GrayCat 1.0 graycat.project.json and will be saved as .graycat.</summary>
    public bool ConvertedFromLegacy { get; init; }
}

/// <summary>Opens and saves .graycat files and keeps the recent-projects list.</summary>
public class ProjectManager
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private readonly string _projectsDirectory;
    private readonly string _workRoot;

    public ProjectManager() : this(
        AppConstants.Paths.Projects,
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CatSuite", "GrayCat", "Work"))
    {
    }

    /// <param name="projectsDirectory">Where the recent-projects list is kept (tests use a temp folder).</param>
    public ProjectManager(string projectsDirectory) : this(projectsDirectory, Path.Combine(projectsDirectory, "work"))
    {
    }

    /// <param name="workRoot">Where open projects are unpacked while being edited.</param>
    public ProjectManager(string projectsDirectory, string workRoot)
    {
        _projectsDirectory = projectsDirectory;
        _workRoot = workRoot;
        Directory.CreateDirectory(_projectsDirectory);
        CleanOldWorkFolders();
    }

    /// <summary>Generated code for "Shop.graycat" goes to the "Shop" folder beside it.</summary>
    public static string OutputFolderFor(string documentPath) =>
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(documentPath))!, Path.GetFileNameWithoutExtension(documentPath));

    /// <summary>Creates a new .graycat file (no code is generated).</summary>
    public async Task<OpenedProject> CreateAsync(ProjectModel project, string documentPath)
    {
        if (File.Exists(documentPath))
            throw new IOException($"{documentPath} already exists.");
        var opened = new OpenedProject { Project = project, DocumentPath = Path.GetFullPath(documentPath), WorkFolder = NewWorkFolder() };
        ProjectPages.Normalize(project);
        await SaveAsync(opened);
        await AddToRecentProjectsAsync(opened.DocumentPath);
        return opened;
    }

    /// <summary>Opens a .graycat file, or a GrayCat 1.0 graycat.project.json (converted on the next save).</summary>
    public async Task<OpenedProject> OpenAsync(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Project file not found: {filePath}");

        var work = NewWorkFolder();
        OpenedProject opened;
        try
        {
            if (GrayCatDocument.IsDocumentPath(filePath))
            {
                var project = await Task.Run(() => GrayCatDocument.Open(filePath, work));
                opened = new OpenedProject { Project = project, DocumentPath = Path.GetFullPath(filePath), WorkFolder = work };
            }
            else
            {
                opened = await OpenLegacyAsync(filePath, work);
            }
        }
        catch
        {
            DeleteWorkFolder(work);
            throw;
        }

        Prepare(opened.Project);
        if (opened.ConvertedFromLegacy)
            await SaveAsync(opened); // write the .graycat right away; the old file stays untouched

        await AddToRecentProjectsAsync(opened.DocumentPath);
        return opened;
    }

    public async Task SaveAsync(OpenedProject opened)
    {
        opened.Project.ModifiedAt = DateTime.UtcNow;
        await Task.Run(() => GrayCatDocument.Save(opened.Project, opened.WorkFolder, opened.DocumentPath));
    }

    /// <summary>
    /// Saves the project to another .graycat file, which becomes the open one. The work folder is shared,
    /// so images come along; the old file is left as it was at its last save.
    /// </summary>
    public async Task<OpenedProject> SaveAsAsync(OpenedProject opened, string documentPath)
    {
        var copy = new OpenedProject { Project = opened.Project, DocumentPath = Path.GetFullPath(documentPath), WorkFolder = opened.WorkFolder };
        await SaveAsync(copy);
        await AddToRecentProjectsAsync(copy.DocumentPath);
        return copy;
    }

    /// <summary>Removes the temporary unpacked copy of a closed project.</summary>
    public void Close(OpenedProject? opened)
    {
        if (opened != null)
            DeleteWorkFolder(opened.WorkFolder);
    }

    private async Task<OpenedProject> OpenLegacyAsync(string jsonPath, string work)
    {
        var json = await File.ReadAllTextAsync(jsonPath);
        ProjectModel? project;
        try
        {
            project = JsonSerializer.Deserialize<ProjectModel>(json, GrayCatJson.Options);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"The file is not a valid GrayCat project: {ex.Message}", ex);
        }
        if (project == null)
            throw new InvalidDataException("The file is empty or not a GrayCat project.");

        // Old layout: Sites\Shop\graycat.project.json (+ Sites\Shop\assets).
        // New layout: Sites\Shop.graycat, with the code still in Sites\Shop.
        var oldFolder = Path.GetDirectoryName(Path.GetFullPath(jsonPath))!;
        ProjectAssets.CopyTo(oldFolder, Path.Combine(work, ProjectAssets.FolderName));

        var parent = Path.GetDirectoryName(oldFolder) ?? oldFolder;
        var name = Path.GetFileName(oldFolder);
        var document = Path.Combine(parent, name + GrayCatDocument.Extension);
        if (File.Exists(document))
        {
            // Never overwrite an existing .graycat; keep the code folder pairing as close as possible.
            var n = 2;
            while (File.Exists(document))
                document = Path.Combine(parent, $"{name} (converted {n++}){GrayCatDocument.Extension}");
        }

        return new OpenedProject { Project = project, DocumentPath = document, WorkFolder = work, ConvertedFromLegacy = true };
    }

    private static void Prepare(ProjectModel project)
    {
        project.Blocks ??= new List<BlockModel>();
        project.Settings ??= new Dictionary<string, string>();
        foreach (var block in project.Blocks)
        {
            block.Properties ??= new Dictionary<string, object>();
            block.Styles ??= new Dictionary<string, string>();
            // JSON values come back as JsonElement; turn them into plain values the editor can work with.
            PropertyValues.NormalizeForEditing(block.Properties);
        }
        ProjectPages.Normalize(project);
    }

    private string NewWorkFolder()
    {
        var folder = Path.Combine(_workRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    private void DeleteWorkFolder(string folder)
    {
        // Only ever delete our own temporary folders.
        if (!GenerationValidator.IsSameOrInside(folder, _workRoot) || !Directory.Exists(folder))
            return;
        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Left for the next start-up clean-up.
        }
    }

    /// <summary>Work folders of sessions that ended without closing properly.</summary>
    private void CleanOldWorkFolders()
    {
        if (!Directory.Exists(_workRoot)) return;
        foreach (var folder in Directory.EnumerateDirectories(_workRoot))
        {
            if (Directory.GetLastWriteTimeUtc(folder) < DateTime.UtcNow.AddDays(-2))
                DeleteWorkFolder(folder);
        }
    }

    public async Task<List<string>> GetRecentProjectsAsync()
    {
        var recentFile = Path.Combine(_projectsDirectory, "recent.json");
        if (!File.Exists(recentFile))
            return new List<string>();

        try
        {
            var json = await File.ReadAllTextAsync(recentFile);
            var recent = JsonSerializer.Deserialize<List<string>>(json, GrayCatJson.Options);
            return recent?.Where(File.Exists).ToList() ?? new List<string>();
        }
        catch (Exception)
        {
            return new List<string>();
        }
    }

    private async Task AddToRecentProjectsAsync(string filePath)
    {
        try
        {
            var recentFile = Path.Combine(_projectsDirectory, "recent.json");
            var recent = await GetRecentProjectsAsync();
            recent.RemoveAll(p => p.Equals(filePath, StringComparison.OrdinalIgnoreCase));
            recent.Insert(0, filePath);
            await File.WriteAllTextAsync(recentFile, JsonSerializer.Serialize(recent.Take(10), GrayCatJson.Options));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The recent list is a convenience; never fail a load because of it.
        }
    }

    /// <summary>Writes the static site (one .html per page, styles.css, script.js, assets) into <paramref name="folder"/>.</summary>
    public async Task<string> ExportStaticSiteAsync(ProjectModel project, string folder, string? workFolder)
    {
        Directory.CreateDirectory(folder);
        foreach (var (name, content) in ProjectScaffolder.BuildStaticSite(project))
            await File.WriteAllTextAsync(Path.Combine(folder, name), content, Utf8NoBom);
        if (workFolder != null)
            ProjectAssets.CopyTo(workFolder, Path.Combine(folder, ProjectAssets.FolderName));
        return Path.Combine(folder, "index.html");
    }

    /// <summary>Writes every page to a temp folder (links between them work) and returns the file of <paramref name="pageSlug"/>.</summary>
    public async Task<string> WritePreviewAsync(ProjectModel project, string? workFolder, string pageSlug = "")
    {
        var folder = Path.Combine(Path.GetTempPath(), "GrayCat", "preview", ProjectScaffolder.PackageName(project.ProjectName));
        Directory.CreateDirectory(folder);
        foreach (var (name, html) in ProjectScaffolder.BuildPreviewSite(project, workFolder))
            await File.WriteAllTextAsync(Path.Combine(folder, name), html, Utf8NoBom);
        return Path.Combine(folder, ProjectPages.FileOf(pageSlug));
    }
}
