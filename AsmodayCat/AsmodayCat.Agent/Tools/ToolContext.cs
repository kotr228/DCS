using AsmodayCat.Agent.Memory;
using AsmodayCat.Shared.Interfaces;

namespace AsmodayCat.Agent.Tools;

// Permissions and paths for one agent run (a chat turn or one processed file).
public sealed class ToolContext
{
    public required string           Source    { get; init; }
    public required IApprovalService Approvals { get; init; }

    // Folders the agent may read and write without asking
    public IReadOnlyList<string> TrustedRoots { get; init; } = [];

    // Chat: reading any file is allowed without asking (writes still need approval)
    public bool ReadAnywhere    { get; init; }

    public bool AllowFileSystem { get; init; }
    public bool AllowShell      { get; init; }
    public bool AllowInternet   { get; init; }
    public bool AllowImages     { get; init; }

    // Long-term memory; tools log what they did so later conversations know about it
    public AssistantMemory? Memory { get; init; }

    public void RecordAction(string text) => Memory?.RecordAction(Source, text);

    public string WorkingDirectory     { get; init; } = Environment.CurrentDirectory;
    public string ImageOutputDirectory { get; init; } = Environment.CurrentDirectory;

    public string ResolvePath(string path)
    {
        path = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
        return Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(WorkingDirectory, path));
    }

    public bool IsTrusted(string fullPath) =>
        TrustedRoots.Any(root => IsUnder(fullPath, root));

    public Task<bool> ConfirmAsync(string title, string details, CancellationToken ct) =>
        Approvals.RequestAsync(Source, title, details, ct);

    public static bool IsUnder(string fullPath, string root)
    {
        var r = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
        var p = Path.GetFullPath(fullPath);
        return p.StartsWith(r, StringComparison.OrdinalIgnoreCase)
            || string.Equals(p.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, r,
                             StringComparison.OrdinalIgnoreCase);
    }
}
