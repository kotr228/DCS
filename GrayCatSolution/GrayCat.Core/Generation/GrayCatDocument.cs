namespace GrayCat.Core.Generation;

using System.IO.Compression;
using System.Text;
using System.Text.Json;
using GrayCat.Core.Constants;
using GrayCat.Core.Models;
using GrayCat.Core.Serialization;

/// <summary>
/// The .graycat project file: a single package (ZIP) holding everything needed to edit a site before it is built:
/// <code>
///   graycat.json   - format marker and version
///   project.json   - pages, blocks, theme, settings
///   assets/...     - images and videos added in the editor
/// </code>
/// Generated code is never stored inside; it goes to a separate folder.
/// </summary>
public static class GrayCatDocument
{
    public const string Extension = ".graycat";
    public const int FormatVersion = 1;

    private const string ManifestEntry = "graycat.json";
    private const string ProjectEntry = "project.json";
    private const string FormatName = "graycat-project";

    /// <summary>Largest single entry accepted when opening (guards against zip bombs).</summary>
    private const long MaxEntryBytes = 512L * 1024 * 1024;

    private static readonly HashSet<string> StoredWithoutCompression = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".webp", ".mp4", ".webm", ".mov", ".ogg"
    };

    public static bool IsDocumentPath(string path) =>
        string.Equals(Path.GetExtension(path), Extension, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Writes <paramref name="project"/> and the assets found in <paramref name="workFolder"/>/assets to <paramref name="path"/>.
    /// The file is replaced only after the new one is complete.
    /// </summary>
    public static void Save(ProjectModel project, string workFolder, string path)
    {
        var target = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var temp = target + ".saving";

        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            WriteText(zip, ManifestEntry, JsonSerializer.Serialize(new
            {
                format = FormatName,
                formatVersion = FormatVersion,
                app = AppConstants.AppName,
                appVersion = AppConstants.AppVersion
            }, GrayCatJson.Web));
            WriteText(zip, ProjectEntry, JsonSerializer.Serialize(project, GrayCatJson.Options));

            var assets = Path.Combine(workFolder, ProjectAssets.FolderName);
            if (Directory.Exists(assets))
            {
                foreach (var file in Directory.EnumerateFiles(assets, "*", SearchOption.AllDirectories))
                {
                    var entryName = ProjectAssets.FolderName + "/" + Path.GetRelativePath(assets, file).Replace('\\', '/');
                    var level = StoredWithoutCompression.Contains(Path.GetExtension(file)) ? CompressionLevel.NoCompression : CompressionLevel.Optimal;
                    zip.CreateEntryFromFile(file, entryName, level);
                }
            }
        }

        File.Move(temp, target, overwrite: true);
    }

    /// <summary>Reads a .graycat file and unpacks its assets into <paramref name="workFolder"/>/assets.</summary>
    /// <exception cref="InvalidDataException">The file is not a GrayCat project or is damaged.</exception>
    public static ProjectModel Open(string path, string workFolder)
    {
        ZipArchive zip;
        try
        {
            zip = ZipFile.OpenRead(path);
        }
        catch (InvalidDataException)
        {
            throw new InvalidDataException("This file is not a GrayCat project (or it is damaged).");
        }

        using (zip)
        {
            var manifest = zip.GetEntry(ManifestEntry);
            if (manifest != null)
            {
                using var doc = JsonDocument.Parse(ReadText(manifest));
                if (doc.RootElement.TryGetProperty("formatVersion", out var v) && v.TryGetInt32(out var version) && version > FormatVersion)
                    throw new InvalidDataException("This project was saved by a newer version of GrayCat Studio. Please update the app.");
            }

            var projectEntry = zip.GetEntry(ProjectEntry)
                ?? throw new InvalidDataException("This file is not a GrayCat project: project.json is missing.");

            ProjectModel project;
            try
            {
                project = JsonSerializer.Deserialize<ProjectModel>(ReadText(projectEntry), GrayCatJson.Options)
                          ?? throw new InvalidDataException("The project data is empty.");
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException($"The project data is damaged: {ex.Message}", ex);
            }

            var root = Path.GetFullPath(workFolder);
            Directory.CreateDirectory(root);
            foreach (var entry in zip.Entries)
            {
                if (!entry.FullName.StartsWith(ProjectAssets.FolderName + "/", StringComparison.OrdinalIgnoreCase) || entry.FullName.EndsWith('/'))
                    continue;
                if (entry.Length > MaxEntryBytes)
                    throw new InvalidDataException($"{entry.FullName} is too large.");

                // Only extract inside the work folder ("zip slip" protection).
                var destination = Path.GetFullPath(Path.Combine(root, entry.FullName));
                if (!GenerationValidator.IsSameOrInside(destination, Path.Combine(root, ProjectAssets.FolderName)))
                    continue;

                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                entry.ExtractToFile(destination, overwrite: true);
            }

            return project;
        }
    }

    private static void WriteText(ZipArchive zip, string name, string text)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(text);
    }

    private static string ReadText(ZipArchiveEntry entry)
    {
        if (entry.Length > MaxEntryBytes)
            throw new InvalidDataException($"{entry.FullName} is too large.");
        using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
