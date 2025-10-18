namespace GrayCat.Service.Services;

using GrayCat.Core.Interfaces;
using GrayCat.Core.Models;
using GrayCat.Core.Models.Generation;

public class ValidationService : IProjectValidator
{
    private readonly ILogger<ValidationService> _logger;
    private readonly List<string> _forbiddenPaths;

    public ValidationService(ILogger<ValidationService> logger)
    {
        _logger = logger;

        _forbiddenPaths = new List<string>
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Windows).ToUpperInvariant(),
            Environment.GetFolderPath(Environment.SpecialFolder.System).ToUpperInvariant(),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles).ToUpperInvariant(),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86).ToUpperInvariant()
        };
    }

    public async Task<(bool IsValid, List<string> Errors)> ValidateProjectAsync(ProjectModel project)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(project.ProjectName))
        {
            errors.Add("Project name cannot be empty");
        }

        if (!ValidateProjectName(project.ProjectName))
        {
            errors.Add("Project name contains invalid characters");
        }

        if (project.Blocks == null || !project.Blocks.Any())
        {
            errors.Add("Project must contain at least one block");
        }

        // Validate each block
        foreach (var block in project.Blocks ?? new List<BlockModel>())
        {
            if (string.IsNullOrWhiteSpace(block.Type))
            {
                errors.Add($"Block {block.Id} has no type specified");
            }
        }

        return await Task.FromResult((errors.Count == 0, errors));
    }

    public async Task<(bool IsValid, List<string> Errors)> ValidateGenerationRequestAsync(GenerationRequest request)
    {
        var errors = new List<string>();

        if (!ValidateProjectName(request.ProjectName))
        {
            errors.Add("Invalid project name");
        }

        if (!ValidateOutputPath(request.OutputPath))
        {
            errors.Add("Invalid or inaccessible output path");
        }

        if (_forbiddenPaths.Any(fp => request.OutputPath.ToUpperInvariant().StartsWith(fp)))
        {
            errors.Add("Cannot create project in system directory");
        }

        return await Task.FromResult((errors.Count == 0, errors));
    }

    public bool ValidateProjectName(string projectName)
    {
        if (string.IsNullOrWhiteSpace(projectName))
            return false;

        var invalidChars = Path.GetInvalidFileNameChars();
        return !projectName.Any(c => invalidChars.Contains(c));
    }

    public bool ValidateOutputPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        try
        {
            var fullPath = Path.GetFullPath(path);
            var root = Path.GetPathRoot(fullPath);

            if (string.IsNullOrEmpty(root))
                return false;

            // Check if drive exists
            if (!Directory.Exists(root))
                return false;

            // Try to create directory if it doesn't exist
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }

            // Test write permissions
            var testFile = Path.Combine(path, $".graycat_test_{Guid.NewGuid()}.tmp");
            File.WriteAllText(testFile, "test");
            File.Delete(testFile);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Path validation failed for: {Path}", path);
            return false;
        }
    }
}