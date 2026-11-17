using AsmodayCat.Shared.Text;
using System.Text.Json;
using AsmodayCat.Agent.Documents;

namespace AsmodayCat.Agent.Tools.System;

// FR7.4: read/write files and list folders. Anything outside the trusted folders
// needs the user's approval.
public class FileSystemTool : IAgentTool
{
    private const int MaxReadChars = 12000;

    public string Name => "file_system";

    public string Description =>
        "Read a file (text, .docx, .pdf), write or append text to a file, or list a folder. " +
        "Relative paths are resolved against the working folder.";

    public object Parameters => new
    {
        type = "object",
        properties = new
        {
            action  = new { type = "string", @enum = new[] { "read", "write", "append", "list" }, description = "What to do" },
            path    = new { type = "string", description = "File or folder path" },
            content = new { type = "string", description = "Text to write (for write/append)" }
        },
        required = new[] { "action", "path" }
    };

    public bool IsEnabled(ToolContext context) => context.AllowFileSystem;

    public async Task<string> ExecuteAsync(JsonElement args, ToolContext ctx, CancellationToken ct)
    {
        var action = args.Str("action")?.ToLowerInvariant() ?? string.Empty;
        var rawPath = args.Str("path");
        if (string.IsNullOrWhiteSpace(rawPath))
            return ToolRegistry.Error("'path' is required.");

        var path = ctx.ResolvePath(rawPath);

        switch (action)
        {
            case "read":
            case "list":
                if (!ctx.ReadAnywhere && !ctx.IsTrusted(path) &&
                    !await ctx.ConfirmAsync(L.T($"Помічник хоче {ActionUk(action)} поза своїми теками", $"The assistant wants to {ActionUk(action)} outside its folders", $"Помощник хочет {ActionUk(action)} вне своих папок"), path, ct))
                    return ToolRegistry.Denied($"{action} {path}");
                return action == "read" ? await ReadAsync(path, ct) : List(path);

            case "write":
            case "append":
                var content = args.Str("content") ?? string.Empty;

                // Appending to a missing file is almost always a guessed path: say so instead of creating it
                if (action == "append" && !File.Exists(path))
                    return ToolRegistry.Error($"File not found: {path}. Check the exact path (MEMORY or a 'list' of the folder); use 'write' only to create a new file.");

                // Start appended text on a new line
                if (action == "append" && content.Length > 0 && !content.StartsWith('\n') && EndsWithoutNewline(path))
                    content = Environment.NewLine + content;

                if (!ctx.IsTrusted(path))
                {
                    var preview = content.Length > 1500 ? content[..1500] + "\n…" : content;
                    if (!await ctx.ConfirmAsync(L.T($"Помічник хоче {ActionUk(action)} файл", $"The assistant wants to {ActionUk(action)} a file", $"Помощник хочет {ActionUk(action)} файл"), $"{path}\n\n{preview}", ct))
                        return ToolRegistry.Denied($"{action} {path}");
                }
                var written = await WriteAsync(path, content, append: action == "append", ct);
                var snippet = content.Length > 80 ? content[..80] + "…" : content;
                ctx.RecordAction($"{(action == "append" ? "Appended to" : "Wrote")} file {path} ({content.Length} chars): \"{snippet.ReplaceLineEndings(" ")}\"");
                return written;

            default:
                return ToolRegistry.Error($"Unknown action '{action}'. Use read, write, append or list.");
        }
    }

    // For approval titles shown to the user
    private static string ActionUk(string action) => action switch
    {
        "read"   => L.T("прочитати", "read", "прочитать"),
        "list"   => L.T("переглянути теку", "list the folder", "просмотреть папку"),
        "write"  => L.T("записати", "write", "записать"),
        "append" => L.T("дописати у", "append to", "дописать в"),
        _        => action
    };

    private static bool EndsWithoutNewline(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (fs.Length == 0) return false;
        fs.Seek(-1, SeekOrigin.End);
        return fs.ReadByte() != '\n';
    }

    private static async Task<string> ReadAsync(string path, CancellationToken ct)
    {
        if (!File.Exists(path))
            return ToolRegistry.Error($"File not found: {path}");

        var doc = await DocumentReader.ReadAsync(path, MaxReadChars, ct);
        return doc.Kind switch
        {
            DocumentKind.Image  => ToolJson.Serialize(new { note = "This is an image; its pixels can't be read as text.", metadata = doc.Text }),
            DocumentKind.Binary => ToolJson.Serialize(new { note = "Binary file; only metadata is available.", metadata = doc.Text }),
            _                   => ToolJson.Serialize(new { path, content = doc.Text, truncated = doc.Truncated })
        };
    }

    private static async Task<string> WriteAsync(string path, string content, bool append, CancellationToken ct)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        if (append) await File.AppendAllTextAsync(path, content, ct);
        else        await File.WriteAllTextAsync(path, content, ct);

        return ToolJson.Serialize(new { success = true, path, chars = content.Length });
    }

    private static string List(string path)
    {
        if (!Directory.Exists(path))
            return ToolRegistry.Error($"Folder not found: {path}");

        var entries = new DirectoryInfo(path)
            .EnumerateFileSystemInfos()
            .Where(e => (e.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0 && !e.Name.StartsWith("~$"))
            .Take(300)
            .Select(e => new
            {
                name     = e.Name,
                type     = e is DirectoryInfo ? "dir" : "file",
                size     = e is FileInfo f ? f.Length : 0L,
                modified = e.LastWriteTime.ToString("yyyy-MM-dd HH:mm")
            })
            .OrderBy(e => e.type)
            .ThenBy(e => e.name, StringComparer.OrdinalIgnoreCase);

        return ToolJson.Serialize(new { path, entries });
    }
}
