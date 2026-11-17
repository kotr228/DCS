using AsmodayCat.Shared.Text;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AsmodayCat.Agent.Tools.System;

// FR7.2: run a cmd.exe command. Every command needs the user's approval.
public class ShellExecutionTool : IAgentTool
{
    private const int DefaultTimeoutSeconds = 120;
    private const int MaxTimeoutSeconds     = 600;
    private const int MaxOutputChars        = 8000;

    // Never run these, even if approved: they can wreck the machine in one call
    private static readonly Regex HardBlocked = new(
        @"\b(format|diskpart|bcdedit|vssadmin|cipher\s+/w)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public string Name => "shell_exec";

    public string Description =>
        "Run a Windows cmd.exe command (e.g. git, dotnet build, python script.py) and get its output. " +
        "The user must approve every command.";

    public object Parameters => new
    {
        type = "object",
        properties = new
        {
            command         = new { type = "string",  description = "The command line to run" },
            working_dir     = new { type = "string",  description = "Folder to run in (optional)" },
            timeout_seconds = new { type = "integer", description = $"Max run time, default {DefaultTimeoutSeconds}" }
        },
        required = new[] { "command" }
    };

    public bool IsEnabled(ToolContext context) => context.AllowShell;

    public async Task<string> ExecuteAsync(JsonElement args, ToolContext ctx, CancellationToken ct)
    {
        var command = args.Str("command")?.Trim() ?? string.Empty;
        if (command.Length == 0)
            return ToolRegistry.Error("'command' is required.");

        if (HardBlocked.IsMatch(command))
            return ToolRegistry.Error("This command is blocked for safety.");

        var workingDir = args.Str("working_dir") is { Length: > 0 } wd
            ? ctx.ResolvePath(wd)
            : ctx.WorkingDirectory;

        if (!Directory.Exists(workingDir))
            return ToolRegistry.Error($"Working folder not found: {workingDir}");

        var timeout = Math.Clamp(args.Int("timeout_seconds", DefaultTimeoutSeconds), 5, MaxTimeoutSeconds);

        if (!await ctx.ConfirmAsync(L.T("Помічник хоче виконати команду", "The assistant wants to run a command", "Помощник хочет выполнить команду"), $"{workingDir}> {command}", ct))
            return ToolRegistry.Denied($"run `{command}`");

        // chcp 65001 makes most tools print UTF-8, so Cyrillic output survives
        var psi = new ProcessStartInfo("cmd.exe", $"/d /s /c \"chcp 65001>nul & {command}\"")
        {
            WorkingDirectory       = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            RedirectStandardInput  = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding  = Encoding.UTF8,
            UseShellExecute        = false,
            CreateNoWindow         = true
        };

        using var process = new Process { StartInfo = psi };
        process.Start();
        process.StandardInput.Close();   // commands waiting for input get EOF instead of hanging

        var stdoutTask = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var stderrTask = process.StandardError.ReadToEndAsync(CancellationToken.None);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeout));

        var timedOut = false;
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            ct.ThrowIfCancellationRequested();
            timedOut = true;
        }

        ctx.RecordAction($"Ran command `{command}` in {workingDir} → {(timedOut ? "timed out" : $"exit {process.ExitCode}")}");

        var stdout = Trim(await stdoutTask);
        var stderr = Trim(await stderrTask);

        return ToolJson.Serialize(new
        {
            exit_code = timedOut ? (int?)null : process.ExitCode,
            timed_out = timedOut,
            stdout,
            stderr
        });
    }

    private static string Trim(string s)
    {
        s = s.Trim();
        return s.Length > MaxOutputChars ? "…" + s[^MaxOutputChars..] : s;
    }
}
