using System.Text.Json;
using AsmodayCat.Shared.Interfaces;

namespace AsmodayCat.Agent.Tools.System;

// FR7.4: інструмент для роботи з файловою системою
public class FileSystemTool : IAgentTool
{
    public string GetName() => "file_system";
    public string GetDescription() =>
        "Read/write files and list directories. Args: {\"action\": \"read|write|list\", \"path\": \"...\", \"content\": \"...(for write)\"}";

    public async Task<string> ExecuteAsync(string jsonArguments, CancellationToken cancellationToken = default)
    {
        using var doc  = JsonDocument.Parse(jsonArguments);
        var action     = doc.RootElement.GetProperty("action").GetString() ?? string.Empty;
        var path       = doc.RootElement.GetProperty("path").GetString() ?? string.Empty;

        return action.ToLowerInvariant() switch
        {
            "read"  => await ReadFileAsync(path, cancellationToken),
            "write" => await WriteFileAsync(path, doc.RootElement, cancellationToken),
            "list"  => ListDirectory(path),
            _       => JsonSerializer.Serialize(new { error = $"Unknown action: {action}" })
        };
    }

    private static async Task<string> ReadFileAsync(string path, CancellationToken ct)
    {
        if (!File.Exists(path))
            return JsonSerializer.Serialize(new { error = "File not found." });

        var content = await File.ReadAllTextAsync(path, ct);
        if (content.Length > 12000) content = content[..12000] + "\n... [truncated]";
        return JsonSerializer.Serialize(new { content });
    }

    private static async Task<string> WriteFileAsync(
        string path, JsonElement root, CancellationToken ct)
    {
        if (!root.TryGetProperty("content", out var contentEl))
            return JsonSerializer.Serialize(new { error = "'content' field is required for write." });

        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        await File.WriteAllTextAsync(path, contentEl.GetString() ?? string.Empty, ct);
        return JsonSerializer.Serialize(new { success = true, path });
    }

    private static string ListDirectory(string path)
    {
        if (!Directory.Exists(path))
            return JsonSerializer.Serialize(new { error = "Directory not found." });

        var entries = Directory
            .EnumerateFileSystemEntries(path, "*", SearchOption.TopDirectoryOnly)
            .Select(e => new
            {
                name  = Path.GetFileName(e),
                type  = Directory.Exists(e) ? "dir" : "file",
                size  = Directory.Exists(e) ? 0L : new FileInfo(e).Length
            })
            .OrderBy(e => e.type)
            .ThenBy(e => e.name);

        return JsonSerializer.Serialize(entries);
    }
}
