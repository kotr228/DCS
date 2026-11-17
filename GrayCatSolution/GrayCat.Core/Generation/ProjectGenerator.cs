namespace GrayCat.Core.Generation;

using System.Diagnostics;
using System.Text;
using GrayCat.Core.Models;
using GrayCat.Core.Models.Generation;

/// <summary>Validates a request, builds the project files and writes them to disk.</summary>
public static class ProjectGenerator
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public static async Task<GenerationResult> GenerateAsync(GenerationRequest request, CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        List<string> errors;
        try
        {
            errors = GenerationValidator.Validate(request);
        }
        catch (UnauthorizedAccessException ex)
        {
            return new GenerationResult { Success = false, AccessDenied = true, Message = ex.Message, Errors = { ex.Message } };
        }
        if (errors.Count > 0)
        {
            return new GenerationResult
            {
                Success = false,
                Message = string.Join(Environment.NewLine, errors),
                Errors = errors
            };
        }

        var target = GenerationValidator.TargetDirectory(request);
        try
        {
            var project = PrepareProject(request);
            var assetsRoot = string.IsNullOrWhiteSpace(request.AssetsDirectory) ? target : request.AssetsDirectory;
            var files = ProjectScaffolder.Build(project, request, ProjectAssets.List(assetsRoot));

            var dirsCreated = 0;
            var changed = 0;
            if (!Directory.Exists(target))
            {
                Directory.CreateDirectory(target);
                dirsCreated++;
            }

            foreach (var (relative, content) in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var full = Path.GetFullPath(Path.Combine(target, relative));
                if (!GenerationValidator.IsSameOrInside(full, target))
                    throw new InvalidOperationException($"Refusing to write outside the project folder: {relative}");

                var dir = Path.GetDirectoryName(full)!;
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                    dirsCreated++;
                }
                // Unchanged files are left alone so running dev servers only reload what really changed.
                if (File.Exists(full) && await File.ReadAllTextAsync(full, cancellationToken) == content)
                    continue;
                await File.WriteAllTextAsync(full, content, Utf8NoBom, cancellationToken);
                changed++;
            }

            var filesWritten = files.Count;
            if (ProjectAssets.PublishFolder(request.Type) is { } publish)
                filesWritten += ProjectAssets.CopyTo(assetsRoot, Path.Combine(target, publish));
            else if (!GenerationValidator.IsSameOrInside(assetsRoot, target))
                filesWritten += ProjectAssets.CopyTo(assetsRoot, Path.Combine(target, ProjectAssets.FolderName));

            sw.Stop();
            return new GenerationResult
            {
                Success = true,
                Message = request.Overwrite ? "Project code regenerated" : "Project generated successfully",
                ProjectPath = target,
                FilesCreated = filesWritten,
                FilesChanged = changed,
                DirectoriesCreated = dirsCreated,
                DurationMs = sw.ElapsedMilliseconds,
                Warnings = Warnings(project)
            };
        }
        catch (OperationCanceledException)
        {
            return new GenerationResult { Success = false, Message = "Generation was cancelled.", Errors = { "Cancelled" } };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or NotSupportedException)
        {
            return new GenerationResult
            {
                Success = false,
                Message = $"Generation failed: {ex.Message}",
                ProjectPath = target,
                AccessDenied = ex is UnauthorizedAccessException,
                Errors = { ex.Message }
            };
        }
    }

    /// <summary>Copies the editor project and fills in name, author and type from the request.</summary>
    public static ProjectModel PrepareProject(GenerationRequest request)
    {
        var source = request.ProjectData ?? new ProjectModel();
        return new ProjectModel
        {
            Id = string.IsNullOrWhiteSpace(source.Id) ? Guid.NewGuid().ToString() : source.Id,
            ProjectName = string.IsNullOrWhiteSpace(source.ProjectName) || source.ProjectName == "Untitled"
                ? request.ProjectName.Trim()
                : source.ProjectName,
            Author = string.IsNullOrWhiteSpace(request.Author) ? source.Author : request.Author,
            Type = request.Type,
            Blocks = source.Blocks ?? new List<BlockModel>(),
            Pages = source.Pages ?? new List<PageInfo>(),
            Settings = source.Settings ?? new Dictionary<string, string>(),
            CreatedAt = source.CreatedAt == default ? DateTime.UtcNow : source.CreatedAt,
            ModifiedAt = source.ModifiedAt == default ? DateTime.UtcNow : source.ModifiedAt,
            Version = string.IsNullOrWhiteSpace(source.Version) ? "1.0.0" : source.Version
        };
    }

    private static List<string> Warnings(ProjectModel project)
    {
        var warnings = new List<string>();
        foreach (var block in project.Blocks)
        {
            if (!BlockDefaults.BlockTypes.Contains(block.Type))
                warnings.Add($"Unknown block type '{block.Type}' was rendered as text.");
        }
        return warnings;
    }
}
