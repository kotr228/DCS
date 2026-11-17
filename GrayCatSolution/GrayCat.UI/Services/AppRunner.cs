namespace GrayCat.UI.Services;

using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.RegularExpressions;
using GrayCat.Shared.Models;

/// <summary>
/// Runs a generated project the way a developer would: npm install (when needed), then its dev server.
/// Output is collected for the run log; the first local URL printed by the server is reported.
/// </summary>
public sealed class AppRunner : IDisposable
{
    private static readonly Regex Ansi = new(@"\x1B\[[0-9;?]*[ -/]*[@-~]", RegexOptions.Compiled);
    private static readonly Regex LocalUrl = new(@"https?://(?:localhost|127\.0\.0\.1|\[::1\]):\d+/?", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private const int MaxLogChars = 200_000;

    private readonly StringBuilder _log = new();
    private readonly object _lock = new();
    private Process? _server;
    private ProcessJob? _job;
    private TaskCompletionSource<string?>? _urlSource;

    /// <summary>Raised (on a background thread) for every output line.</summary>
    public event Action<string>? OutputReceived;

    /// <summary>Raised (on a background thread) when the dev server exits on its own.</summary>
    public event Action<int>? Exited;

    public bool IsRunning => _server is { HasExited: false };
    public string? Url { get; private set; }
    public string? RunningFolder { get; private set; }

    public string Log
    {
        get { lock (_lock) return _log.ToString(); }
    }

    /// <summary>True when Node.js and npm can be found on PATH.</summary>
    public static bool IsNodeInstalled() => FindOnPath("node.exe") != null && FindOnPath("npm.cmd") != null;

    /// <summary>Packages are missing or package.json changed since the last install.</summary>
    public static bool NeedsInstall(string folder)
    {
        var modules = Path.Combine(folder, "node_modules");
        var marker = Path.Combine(modules, ".package-lock.json");
        if (!Directory.Exists(modules) || !File.Exists(marker))
            return true;
        return File.GetLastWriteTimeUtc(Path.Combine(folder, "package.json")) > File.GetLastWriteTimeUtc(marker);
    }

    /// <summary>Runs "npm install"; returns true on success.</summary>
    public async Task<bool> InstallAsync(string folder, CancellationToken token = default)
    {
        AppendLog($"> npm install   ({folder})");
        using var process = CreateProcess(folder, "npm install --no-audit --no-fund", new Dictionary<string, string> { ["CI"] = "1" });
        process.Start();
        using var job = TryJob(process);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        try
        {
            await process.WaitForExitAsync(token);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }
        AppendLog(process.ExitCode == 0 ? "npm install finished." : $"npm install failed (exit code {process.ExitCode}).");
        return process.ExitCode == 0;
    }

    /// <summary>Folder that holds the result of <see cref="BuildAsync"/>.</summary>
    public static string OutputFolder(string folder, ProjectType type) => type switch
    {
        ProjectType.React or ProjectType.ReactNative => Path.Combine(folder, "dist"),
        ProjectType.NextJs => Path.Combine(folder, ".next"),
        _ => folder
    };

    /// <summary>Production build (Vite / Next / Expo web export). Types without a build step return true.</summary>
    public async Task<bool> BuildAsync(string folder, ProjectType type)
    {
        var command = type switch
        {
            ProjectType.React or ProjectType.NextJs => "npm run build",
            ProjectType.ReactNative => "npx expo export --platform web --output-dir dist",
            _ => null
        };
        if (command == null)
            return true;

        AppendLog($"> {command}   ({folder})");
        using var process = CreateProcess(folder, command, new Dictionary<string, string> { ["CI"] = "1" });
        process.Start();
        using var job = TryJob(process);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync();
        AppendLog(process.ExitCode == 0 ? "Build finished." : $"Build failed (exit code {process.ExitCode}).");
        return process.ExitCode == 0;
    }

    /// <summary>Starts the project's dev server and waits until it prints its address.</summary>
    /// <returns>The local URL, or null if the server stopped or did not report one in time.</returns>
    public async Task<string?> StartAsync(string folder, ProjectType type, TimeSpan timeout)
    {
        Stop();

        var env = new Dictionary<string, string>();
        string command;
        switch (type)
        {
            case ProjectType.React:
                command = $"npm run dev -- --port {FreePort(5173)}";
                break;
            case ProjectType.NextJs:
                command = $"npm run dev -- -p {FreePort(3000)}";
                break;
            case ProjectType.NodeJsApi:
                env["PORT"] = FreePort(3000).ToString();
                command = "npm run dev";
                break;
            case ProjectType.ReactNative:
                command = $"npx expo start --web --port {FreePort(8081)}";
                break;
            default:
                throw new NotSupportedException($"{type} projects do not have a dev server.");
        }

        _urlSource = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        Url = null;
        RunningFolder = folder;
        AppendLog($"> {command}   ({folder})");

        var process = CreateProcess(folder, command, env);
        process.EnableRaisingEvents = true;
        process.Exited += (_, _) =>
        {
            var code = SafeExitCode(process);
            AppendLog($"Dev server stopped (exit code {code}).");
            _urlSource?.TrySetResult(null);
            if (ReferenceEquals(_server, process))
            {
                _server = null;
                Url = null;
                Exited?.Invoke(code);
            }
        };
        _server = process;
        process.Start();
        _job = TryJob(process);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var finished = await Task.WhenAny(_urlSource.Task, Task.Delay(timeout));
        return finished == _urlSource.Task ? await _urlSource.Task : null;
    }

    public void Stop()
    {
        var server = _server;
        _server = null;
        Url = null;
        RunningFolder = null;

        // Ends cmd, npm, node and anything they started, even if the tree is already broken up.
        _job?.Dispose();
        _job = null;

        if (server != null)
        {
            TryKill(server);
            AppendLog("Dev server stopped.");
            server.Dispose();
        }
    }

    public void Dispose() => Stop();

    private Process CreateProcess(string folder, string command, Dictionary<string, string> env)
    {
        var info = new ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
            Arguments = $"/d /s /c \"{command}\"",
            WorkingDirectory = folder,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        // Keep tools from opening their own browser windows. (No CI flag here: it turns off live reload in Expo.)
        info.Environment["BROWSER"] = "none";
        info.Environment["NO_COLOR"] = "1";
        info.Environment["FORCE_COLOR"] = "0";
        info.Environment["NEXT_TELEMETRY_DISABLED"] = "1";
        info.Environment["EXPO_NO_TELEMETRY"] = "1";
        foreach (var (key, value) in env)
            info.Environment[key] = value;

        var process = new Process { StartInfo = info };
        process.OutputDataReceived += (_, e) => OnLine(e.Data);
        process.ErrorDataReceived += (_, e) => OnLine(e.Data);
        return process;
    }

    private void OnLine(string? line)
    {
        if (line == null) return;
        line = Ansi.Replace(line, "");
        AppendLog(line);

        if (Url == null && _urlSource != null && LocalUrl.Match(line) is { Success: true } match)
        {
            Url = match.Value.Replace("127.0.0.1", "localhost").Replace("[::1]", "localhost");
            _urlSource.TrySetResult(Url);
        }
    }

    private void AppendLog(string line)
    {
        lock (_lock)
        {
            _log.AppendLine(line);
            if (_log.Length > MaxLogChars)
                _log.Remove(0, _log.Length - MaxLogChars / 2);
        }
        OutputReceived?.Invoke(line);
    }

    private static int FreePort(int preferred)
    {
        var used = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Select(e => e.Port).ToHashSet();
        for (var port = preferred; port < preferred + 100; port++)
        {
            if (!used.Contains(port))
                return port;
        }
        // Fall back to any free port chosen by the OS.
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var free = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return free;
    }

    /// <summary>Puts the process (and, through inheritance, its children) into a kill-on-close job.</summary>
    private static ProcessJob? TryJob(Process process)
    {
        try
        {
            var job = new ProcessJob();
            job.Add(process);
            return job;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null; // fall back to killing the process tree
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // Already gone.
        }
    }

    private static int SafeExitCode(Process process)
    {
        try { return process.ExitCode; }
        catch (InvalidOperationException) { return -1; }
    }

    private static string? FindOnPath(string file)
    {
        var paths = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        return paths.Select(p => Path.Combine(p.Trim(), file)).FirstOrDefault(File.Exists);
    }
}
