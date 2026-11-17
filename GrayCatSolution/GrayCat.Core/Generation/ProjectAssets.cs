namespace GrayCat.Core.Generation;

using GrayCat.Shared.Models;

/// <summary>
/// Local files (images, videos) used by a project live in the project's "assets" folder and are
/// referenced as "assets/name.ext". The generator copies them to wherever each project type serves them.
/// </summary>
public static class ProjectAssets
{
    public const string FolderName = "assets";

    public static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".svg", ".ico", ".bmp" };
    public static readonly string[] VideoExtensions = { ".mp4", ".webm", ".ogg", ".mov" };

    /// <summary>Extensions React Native's bundler can load with require().</summary>
    private static readonly string[] NativeImageExtensions = { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp" };

    /// <summary>Folder (relative to the project) where the type serves "assets/...", or null when no copy is needed.</summary>
    public static string? PublishFolder(ProjectType type) => type switch
    {
        ProjectType.React or ProjectType.NextJs or ProjectType.NodeJsApi => "public/" + FolderName,
        _ => null // StaticHtml and ReactNative use the project's own assets folder
    };

    /// <summary>All asset files as "assets/..." paths with forward slashes.</summary>
    public static List<string> List(string projectDirectory)
    {
        var dir = Path.Combine(projectDirectory, FolderName);
        if (!Directory.Exists(dir))
            return new List<string>();

        return Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
            .Select(f => FolderName + "/" + Path.GetRelativePath(dir, f).Replace('\\', '/'))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static bool IsNativeImage(string path) =>
        NativeImageExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    /// <summary>
    /// Copies <paramref name="sourceFile"/> into the project's assets folder (unless it is already there)
    /// and returns the "assets/..." path to store in the block.
    /// </summary>
    public static string Import(string projectDirectory, string sourceFile)
    {
        var assetsDir = Path.Combine(projectDirectory, FolderName);
        var source = Path.GetFullPath(sourceFile);

        if (GenerationValidator.IsSameOrInside(source, assetsDir))
            return FolderName + "/" + Path.GetRelativePath(assetsDir, source).Replace('\\', '/');

        Directory.CreateDirectory(assetsDir);
        var name = SafeFileName(Path.GetFileNameWithoutExtension(source));
        var ext = Path.GetExtension(source).ToLowerInvariant();
        var target = Path.Combine(assetsDir, name + ext);

        // Same name but different file: pick a free name instead of overwriting.
        var counter = 2;
        while (File.Exists(target) && !SameContent(target, source))
            target = Path.Combine(assetsDir, $"{name}-{counter++}{ext}");

        if (!File.Exists(target))
            File.Copy(source, target);

        return FolderName + "/" + Path.GetFileName(target);
    }

    /// <summary>Copies the project's assets folder to <paramref name="destination"/>; returns the number of files copied.</summary>
    public static int CopyTo(string projectDirectory, string destination)
    {
        var source = Path.Combine(projectDirectory, FolderName);
        if (!Directory.Exists(source))
            return 0;
        if (GenerationValidator.IsSameOrInside(destination, source) || GenerationValidator.IsSameOrInside(source, destination))
            return 0;

        var count = 0;
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
            count++;
        }
        return count;
    }

    private static string SafeFileName(string name)
    {
        var cleaned = new string(name.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? char.ToLowerInvariant(c) : '-').ToArray()).Trim('-');
        while (cleaned.Contains("--")) cleaned = cleaned.Replace("--", "-");
        return cleaned.Length == 0 ? "file" : cleaned.Length > 60 ? cleaned[..60] : cleaned;
    }

    private static bool SameContent(string a, string b)
    {
        var fa = new FileInfo(a);
        var fb = new FileInfo(b);
        if (fa.Length != fb.Length) return false;
        return File.ReadAllBytes(a).AsSpan().SequenceEqual(File.ReadAllBytes(b));
    }
}
