namespace GrayCat.Core.Generation;

using System.IO.Compression;

/// <summary>Packs a project folder into a .zip, leaving out installed packages and build output.</summary>
public static class ProjectArchive
{
    private static readonly HashSet<string> SkippedFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules", ".next", "dist", "out", ".expo", "web-build", "dist-check", ".git", ".vs"
    };

    /// <returns>Number of files written to the archive.</returns>
    public static int Create(string projectFolder, string zipPath)
    {
        var root = Path.GetFullPath(projectFolder);
        var target = Path.GetFullPath(zipPath);
        if (GenerationValidator.IsSameOrInside(target, root))
            throw new IOException("Save the ZIP file outside the project folder.");

        if (File.Exists(target))
            File.Delete(target);

        var count = 0;
        using var archive = ZipFile.Open(target, ZipArchiveMode.Create);
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, file);
            var parts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (parts.Take(parts.Length - 1).Any(SkippedFolders.Contains) || relative.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
                continue;

            archive.CreateEntryFromFile(file, relative.Replace('\\', '/'), CompressionLevel.Optimal);
            count++;
        }
        return count;
    }
}
