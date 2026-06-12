using System.Diagnostics;
using System.Text.Json;
using AsmodayCat.Shared.Interfaces;

namespace AsmodayCat.Agent.Tools.System;

// FR7.2: виконання команд у cmd/powershell з валідацією безпеки
public class ShellExecutionTool : IAgentTool
{
    // Заблоковані команди — жорсткий список для безпеки
    private static readonly HashSet<string> BlockedCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "rm", "del", "rmdir", "rd", "format", "shutdown", "reboot",
        "reg", "regedit", "netsh", "sc", "taskkill", "cipher"
    };

    public string GetName() => "shell_exec";
    public string GetDescription() =>
        "Execute a shell command (cmd/powershell). Args: {\"command\": \"dotnet build\", \"working_dir\": \"C:\\\\project\"}";

    public async Task<string> ExecuteAsync(string jsonArguments, CancellationToken cancellationToken = default)
    {
        using var doc = JsonDocument.Parse(jsonArguments);
        var command    = doc.RootElement.GetProperty("command").GetString() ?? string.Empty;
        var workingDir = doc.RootElement.TryGetProperty("working_dir", out var wd)
            ? wd.GetString() ?? Environment.CurrentDirectory
            : Environment.CurrentDirectory;

        if (!ValidateCommand(command, out var reason))
            return JsonSerializer.Serialize(new { error = $"Blocked: {reason}" });

        var psi = new ProcessStartInfo("cmd.exe", $"/c {command}")
        {
            WorkingDirectory       = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            CreateNoWindow         = true
        };

        using var process = new Process { StartInfo = psi };
        process.Start();

        var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);

        return JsonSerializer.Serialize(new
        {
            exit_code = process.ExitCode,
            stdout    = stdout.Trim(),
            stderr    = stderr.Trim()
        });
    }

    private static bool ValidateCommand(string command, out string reason)
    {
        var firstWord = command.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
        if (BlockedCommands.Contains(firstWord))
        {
            reason = $"Command '{firstWord}' is not allowed.";
            return false;
        }

        // Блокуємо спроби escap'у через & ; |
        if (command.Contains("&&") || command.Contains("||") || command.Contains(";"))
        {
            reason = "Command chaining is not allowed.";
            return false;
        }

        reason = string.Empty;
        return true;
    }
}
