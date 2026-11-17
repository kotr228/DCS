using AsmodayCat.Agent.Tools;
using AsmodayCat.Shared.Interfaces;

namespace AsmodayCat.Tests;

// A temporary folder removed after the test
public sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "asmodaycat_tests", Guid.NewGuid().ToString("N"));

    public TempDir() => Directory.CreateDirectory(Path);

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}

// Approvals answered by the test; records what was asked
public sealed class FakeApprovals(bool answer) : IApprovalService
{
    public List<string> Asked { get; } = [];

    public Task<bool> RequestAsync(string source, string title, string details, CancellationToken cancellationToken = default)
    {
        Asked.Add(title);
        return Task.FromResult(answer);
    }
}

public static class Ctx
{
    public static ToolContext For(string root, IApprovalService approvals, bool readAnywhere = false) => new()
    {
        Source               = "Test",
        Approvals            = approvals,
        TrustedRoots         = [root],
        ReadAnywhere         = readAnywhere,
        AllowFileSystem      = true,
        AllowShell           = true,
        AllowInternet        = true,
        WorkingDirectory     = root,
        ImageOutputDirectory = root
    };
}
