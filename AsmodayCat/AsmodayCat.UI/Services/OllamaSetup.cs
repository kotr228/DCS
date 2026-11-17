using AsmodayCat.UI.Localization;
using System.Diagnostics;
using System.IO;

namespace AsmodayCat.UI.Services;

// Downloads the official Ollama installer and runs it silently (per-user, no admin rights).
public static class OllamaSetup
{
    public const string InstallingStep = "install";

    private const string Url = "https://ollama.com/download/OllamaSetup.exe";

    public static async Task InstallAsync(IProgress<(string What, double Percent)>? progress, CancellationToken ct)
    {
        var setup = Path.Combine(Path.GetTempPath(), "AsmodayCat", "OllamaSetup.exe");
        if (!File.Exists(setup))
            await Downloader.DownloadAsync(Url, setup, "Ollama", progress, ct);

        progress?.Report((InstallingStep, 100));
        // Inno Setup switches: no windows, no reboot prompts
        using var process = Process.Start(new ProcessStartInfo(setup, "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART")
        {
            UseShellExecute = true
        })!;
        await process.WaitForExitAsync(ct);
        if (process.ExitCode != 0)
            throw new InvalidOperationException(Loc.F("startup.ollamaExitCode", process.ExitCode));

        try { File.Delete(setup); } catch (IOException) { }
    }
}
