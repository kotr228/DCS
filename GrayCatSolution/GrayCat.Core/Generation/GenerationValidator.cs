namespace GrayCat.Core.Generation;

using GrayCat.Core.Models.Generation;

/// <summary>Checks a generation request before anything touches the disk.</summary>
public static class GenerationValidator
{
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    /// <returns>An error message, or null when the name is usable as a folder name.</returns>
    public static string? ValidateProjectName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Project name cannot be empty.";
        if (name.Length > 100)
            return "Project name must be 100 characters or fewer.";
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Contains('/') || name.Contains('\\'))
            return "Project name contains characters that are not allowed in a folder name.";
        if (name.Trim() is "." or "..")
            return "Project name cannot be '.' or '..'.";
        if (name.EndsWith('.') || name.EndsWith(' ') || name.StartsWith(' '))
            return "Project name cannot start with a space or end with a space or dot.";
        if (ReservedNames.Contains(name.Split('.')[0]))
            return $"'{name}' is a reserved name on Windows.";
        return null;
    }

    /// <returns>An error message, or null when the folder can hold projects.</returns>
    public static string? ValidateOutputPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "Choose a location for the project.";

        string full;
        try
        {
            if (!Path.IsPathFullyQualified(path))
                return "Location must be a full path, e.g. C:\\Users\\me\\Documents\\Sites.";
            full = Path.GetFullPath(path);
        }
        catch (Exception)
        {
            return "Location is not a valid path.";
        }

        var root = Path.GetPathRoot(full);
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
            return $"Drive '{root}' does not exist.";

        foreach (var forbidden in ForbiddenFolders())
        {
            if (IsSameOrInside(full, forbidden))
                return "Projects cannot be created inside system folders.";
        }

        return null;
    }

    public static List<string> Validate(GenerationRequest request)
    {
        var errors = new List<string>();
        if (ValidateProjectName(request.ProjectName) is { } nameError) errors.Add(nameError);
        if (ValidateOutputPath(request.OutputPath) is { } pathError) errors.Add(pathError);
        if (!Enum.IsDefined(request.Type)) errors.Add($"Unknown project type: {request.Type}.");
        if (errors.Count > 0) return errors;

        var target = TargetDirectory(request);
        if (Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any())
        {
            if (!request.Overwrite)
                errors.Add($"Folder '{target}' already exists and is not empty.");
            else if (!File.Exists(Path.Combine(target, ProjectScaffolder.OutputMarker)) &&
                     !File.Exists(Path.Combine(target, ProjectScaffolder.ProjectFileName)))
                errors.Add($"Folder '{target}' was not created by GrayCat, so it will not be overwritten.");
        }

        return errors;
    }

    public static string TargetDirectory(GenerationRequest request) =>
        Path.GetFullPath(Path.Combine(request.OutputPath, request.ProjectName.Trim()));

    public static bool IsSameOrInside(string path, string folder)
    {
        if (string.IsNullOrEmpty(folder)) return false;
        var p = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var f = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        return p.Equals(f, StringComparison.OrdinalIgnoreCase) ||
               p.StartsWith(f + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> ForbiddenFolders() => new[]
    {
        Environment.GetFolderPath(Environment.SpecialFolder.Windows),
        Environment.GetFolderPath(Environment.SpecialFolder.System),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
    }.Where(f => !string.IsNullOrEmpty(f));
}
