using AsmodayCat.UI.Localization;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace AsmodayCat.UI.Services;

// Finds and starts the background service (AsmodayCat.Service.exe) for the signed-in user,
// and manages "start with Windows".
public static class ServiceLauncher
{
    private const string ExeName   = "AsmodayCat.Service.exe";
    private const string RunKey    = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue  = "AsmodayCat";

    // Installed layout: <app>\UI\AsmodayCat.UI.exe + <app>\Service\AsmodayCat.Service.exe.
    // Development: ...\AsmodayCat.UI\bin\<cfg>\net8.0-windows\ + ...\AsmodayCat.Service\bin\<cfg>\net8.0\.
    public static string? FindServiceExe()
    {
        var baseDir = AppContext.BaseDirectory;
        var candidates = new List<string>
        {
            Path.Combine(baseDir, ExeName),
            Path.GetFullPath(Path.Combine(baseDir, "..", "Service", ExeName))
        };

        // Walk up to the solution folder and look into the service project's output
        var dir = new DirectoryInfo(baseDir);
        for (var i = 0; i < 6 && dir is not null; i++, dir = dir.Parent)
        {
            var project = Path.Combine(dir.FullName, "AsmodayCat.Service", "bin");
            if (!Directory.Exists(project)) continue;
            candidates.AddRange(Directory.EnumerateFiles(project, ExeName, SearchOption.AllDirectories)
                .OrderByDescending(File.GetLastWriteTimeUtc));
            break;
        }

        return candidates.FirstOrDefault(File.Exists);
    }

    public static async Task<bool> IsRunningAsync(IpcClient ipc)
    {
        var resp = await ipc.SendCommandAsync(new IpcCommand { Type = IpcCommandType.GetStatus });
        return resp?.Success == true;
    }

    // Starts the service if it isn't answering; returns true once it does.
    public static async Task<bool> EnsureRunningAsync(IpcClient ipc, Action<string>? status = null)
    {
        if (await IsRunningAsync(ipc)) return true;

        var exe = FindServiceExe();
        if (exe is null)
        {
            status?.Invoke(Loc.T("svc.exeMissing"));
            return false;
        }

        status?.Invoke(Loc.T("svc.starting"));
        try
        {
            Process.Start(new ProcessStartInfo(exe)
            {
                WorkingDirectory = Path.GetDirectoryName(exe)!,
                UseShellExecute  = false,
                CreateNoWindow   = true
            });
        }
        catch (Exception ex)
        {
            status?.Invoke(Loc.F("svc.startFailed", ex.Message));
            return false;
        }

        for (var i = 0; i < 60; i++)
        {
            await Task.Delay(500);
            if (await IsRunningAsync(ipc)) return true;
        }
        status?.Invoke(Loc.T("svc.timeout"));
        return false;
    }

    // Asks the service to exit, waits until it's gone, starts it again
    public static async Task<bool> RestartAsync(IpcClient ipc, Action<string>? status = null)
    {
        status?.Invoke(Loc.T("svc.restarting"));
        await ipc.SendCommandAsync(new IpcCommand { Type = IpcCommandType.RestartService });
        for (var i = 0; i < 40 && await IsRunningAsync(ipc); i++) await Task.Delay(250);
        await Task.Delay(500);   // let the old process release its single-instance lock
        return await EnsureRunningAsync(ipc, status);
    }

    // ── Start with Windows (per-user Run key, no admin rights needed) ────────

    public static bool IsAutostartEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(RunValue) is string;
    }

    public static bool SetAutostart(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (!enabled)
        {
            key.DeleteValue(RunValue, throwOnMissingValue: false);
            return true;
        }

        // The app in the tray: it starts the service and keeps approvals, hotkeys and notifications working
        var exe = Environment.ProcessPath;
        if (exe is null || FindServiceExe() is null) return false;
        key.SetValue(RunValue, $"\"{exe}\" --tray");
        return true;
    }
}
