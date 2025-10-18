namespace GrayCat.UI.Services;

using GrayCat.Core.Constants;
using GrayCat.Core.Models;
using GrayCat.Shared.Models;
using System.IO;
using System.Text.Json;

public class ProjectManager
{
    private readonly string _projectsDirectory;
    private readonly JsonSerializerOptions _jsonOptions;

    public ProjectManager()
    {
        _projectsDirectory = AppConstants.Paths.Projects;
        Directory.CreateDirectory(_projectsDirectory);

        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };
    }

    public async Task<ProjectModel> LoadProjectAsync(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Project file not found: {filePath}");
        }

        var json = await File.ReadAllTextAsync(filePath);
        var project = JsonSerializer.Deserialize<ProjectModel>(json, _jsonOptions);

        if (project == null)
        {
            throw new InvalidDataException("Failed to deserialize project file");
        }

        return project;
    }

    public async Task<bool> SaveProjectAsync(ProjectModel project, string filePath)
    {
        try
        {
            project.ModifiedAt = DateTime.UtcNow;

            var json = JsonSerializer.Serialize(project, _jsonOptions);
            await File.WriteAllTextAsync(filePath, json);

            // Also save to recent projects list
            await AddToRecentProjectsAsync(filePath);

            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Save failed: {ex.Message}");
            return false;
        }
    }

    public async Task<ProjectModel> CreateNewProjectAsync(string name, string author, ProjectType type)
    {
        var project = new ProjectModel
        {
            Id = Guid.NewGuid().ToString(),
            ProjectName = name,
            Author = author,
            Type = type,
            CreatedAt = DateTime.UtcNow,
            ModifiedAt = DateTime.UtcNow,
            Version = "1.0.0"
        };

        return await Task.FromResult(project);
    }

    public async Task<List<string>> GetRecentProjectsAsync()
    {
        var recentFile = Path.Combine(_projectsDirectory, "recent.json");

        if (!File.Exists(recentFile))
        {
            return new List<string>();
        }

        try
        {
            var json = await File.ReadAllTextAsync(recentFile);
            var recent = JsonSerializer.Deserialize<List<string>>(json, _jsonOptions);
            return recent?.Where(File.Exists).ToList() ?? new List<string>();
        }
        catch
        {
            return new List<string>();
        }
    }

    private async Task AddToRecentProjectsAsync(string filePath)
    {
        var recentFile = Path.Combine(_projectsDirectory, "recent.json");
        var recent = await GetRecentProjectsAsync();

        // Remove if already exists
        recent.Remove(filePath);

        // Add to beginning
        recent.Insert(0, filePath);

        // Keep only last 10
        if (recent.Count > 10)
        {
            recent = recent.Take(10).ToList();
        }

        var json = JsonSerializer.Serialize(recent, _jsonOptions);
        await File.WriteAllTextAsync(recentFile, json);
    }

    public async Task<bool> ExportProjectAsync(ProjectModel project, string outputPath)
    {
        try
        {
            // Create export directory
            var exportDir = Path.Combine(outputPath, $"{project.ProjectName}_export_{DateTime.Now:yyyyMMdd_HHmmss}");
            Directory.CreateDirectory(exportDir);

            // Save project file
            var projectFile = Path.Combine(exportDir, "graycat.project.json");
            await SaveProjectAsync(project, projectFile);

            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Export failed: {ex.Message}");
            return false;
        }
    }
}