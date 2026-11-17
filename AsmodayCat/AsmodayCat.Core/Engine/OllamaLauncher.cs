using AsmodayCat.Shared.Text;
using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AsmodayCat.Core.Engine;

// Starts a local Ollama server ("ollama serve") when it isn't running, so the
// assistant works without the user launching Ollama by hand.
public sealed class OllamaLauncher
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(30);

    private readonly HttpClient              _http;
    private readonly IConfiguration          _config;
    private readonly ILogger<OllamaLauncher> _logger;
    private readonly SemaphoreSlim           _gate = new(1, 1);

    public OllamaLauncher(HttpClient http, IConfiguration config, ILogger<OllamaLauncher> logger)
    {
        _http   = http;
        _config = config;
        _logger = logger;
    }

    // Set after a failed start, for error messages
    public string? LastError { get; private set; }
    // ollama.exe is not installed (the app offers to install it)
    public bool ExeMissing { get; private set; }

    public async Task<bool> IsRunningAsync(CancellationToken ct = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(2));
            using var resp = await _http.GetAsync("/api/version", cts.Token);
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            ct.ThrowIfCancellationRequested();
            return false;
        }
    }

    public async Task<bool> EnsureRunningAsync(CancellationToken ct = default)
    {
        if (await IsRunningAsync(ct)) return true;

        await _gate.WaitAsync(ct);
        try
        {
            ExeMissing = false;
            if (await IsRunningAsync(ct)) return true;

            var host = _http.BaseAddress?.Host ?? "localhost";
            if (host is not ("localhost" or "127.0.0.1" or "::1"))
            {
                LastError = L.T($"Ollama за адресою {_http.BaseAddress} недоступна (віддалений сервер — запустити його звідси не можна).",
                                $"Ollama at {_http.BaseAddress} is not reachable (a remote server can't be started from here).",
                                $"Ollama по адресу {_http.BaseAddress} недоступна (удалённый сервер отсюда запустить нельзя).");
                return false;
            }

            var exe = FindExecutable();
            if (exe is null)
            {
                ExeMissing = true;
                LastError = L.T("Ollama не запущена, і ollama.exe не знайдено. Встановіть її з https://ollama.com або вкажіть шлях Ollama:ExePath в appsettings.json.",
                                "Ollama is not running and ollama.exe was not found. Install it from https://ollama.com or set Ollama:ExePath in appsettings.json.",
                                "Ollama не запущена, и ollama.exe не найден. Установите её с https://ollama.com или укажите путь Ollama:ExePath в appsettings.json.");
                _logger.LogWarning("{Error}", LastError);
                return false;
            }

            _logger.LogInformation("Starting Ollama: {Exe}", exe);
            Process.Start(new ProcessStartInfo(exe, "serve")
            {
                UseShellExecute = false,
                CreateNoWindow  = true,
                WindowStyle     = ProcessWindowStyle.Hidden
            });

            var deadline = DateTime.UtcNow + StartupTimeout;
            while (DateTime.UtcNow < deadline)
            {
                await Task.Delay(500, ct);
                if (await IsRunningAsync(ct))
                {
                    _logger.LogInformation("Ollama is running");
                    LastError = null;
                    return true;
                }
            }

            LastError = L.T($"Запущено {exe}, але Ollama не відповіла за {StartupTimeout.TotalSeconds:N0} с.",
                            $"Started {exe}, but Ollama did not respond within {StartupTimeout.TotalSeconds:N0} s.",
                            $"Запущен {exe}, но Ollama не ответила за {StartupTimeout.TotalSeconds:N0} с.");
            _logger.LogWarning("{Error}", LastError);
            return false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LastError = L.T($"Не вдалося запустити Ollama: {ex.Message}", $"Could not start Ollama: {ex.Message}", $"Не удалось запустить Ollama: {ex.Message}");
            _logger.LogWarning("{Error}", LastError);
            return false;
        }
        finally
        {
            _gate.Release();
        }
    }

    private string? FindExecutable()
    {
        var candidates = new List<string>();

        if (_config["Ollama:ExePath"] is { Length: > 0 } configured)
            candidates.Add(Environment.ExpandEnvironmentVariables(configured));

        candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs", "Ollama", "ollama.exe"));
        candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "Ollama", "ollama.exe"));

        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            if (dir.Length > 0) candidates.Add(Path.Combine(dir.Trim('"'), "ollama.exe"));
        }

        return candidates.FirstOrDefault(File.Exists);
    }
}
