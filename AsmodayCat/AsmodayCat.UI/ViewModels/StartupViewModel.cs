using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using AsmodayCat.UI.Localization;
using AsmodayCat.UI.Services;

namespace AsmodayCat.UI.ViewModels;

public partial class StartupViewModel : ObservableObject
{
    private readonly IpcClient _ipc;

    private const string Good = "#4F8A4A";
    private const string Bad  = "#B5654A";

    [ObservableProperty] private string _statusText    = string.Empty;
    [ObservableProperty] private bool   _isChecking    = true;
    [ObservableProperty] private bool   _canContinue;
    [ObservableProperty] private string _serviceStatus = string.Empty;
    [ObservableProperty] private string _serviceColor  = Bad;
    [ObservableProperty] private bool   _serviceOk;
    [ObservableProperty] private string _llmStatus     = string.Empty;
    [ObservableProperty] private string _llmColor      = Bad;
    [ObservableProperty] private bool   _llmOk;

    // First run: what's missing and can be fixed from here
    [ObservableProperty] private bool   _ollamaMissing;
    [ObservableProperty] private bool   _noModels;
    [ObservableProperty] private bool   _isInstalling;
    [ObservableProperty] private double _installPercent;
    [ObservableProperty] private string _versionFooter = string.Empty;

    public const string RecommendedModel = "qwen2.5:7b";

    // Result of the last check, so the texts can be rebuilt in another language
    private enum Phase { Checking, Done }
    private Phase   _phase = Phase.Checking;
    private bool    _engineRunning;
    private int     _modelCount;
    private string? _engineError;
    private string? _progressKey;   // a step in progress ("startup.connecting"…), or null

    public StartupViewModel(IpcClient ipc)
    {
        _ipc = ipc;
        UpdateTexts();
        Loc.LanguageChanged += UpdateTexts;
    }

    public void Detach() => Loc.LanguageChanged -= UpdateTexts;

    private void UpdateTexts()
    {
        VersionFooter = Loc.F("startup.footer", AppVersion.Display);

        if (_phase == Phase.Checking)
        {
            ServiceStatus = Loc.T("startup.checking");
            LlmStatus     = Loc.T("startup.checking");
            StatusText    = Loc.T(_progressKey ?? "startup.starting");
            return;
        }

        ServiceStatus = Loc.T(ServiceOk ? "startup.svcRunning" : "startup.svcNotRunning");
        if (!ServiceOk)
        {
            LlmStatus  = Loc.T("startup.unavailable");
            StatusText = Loc.T("startup.svcFailed");
            return;
        }

        (LlmStatus, StatusText) = (_engineRunning, _modelCount) switch
        {
            (false, _) => (Loc.T("startup.llmNotRunning"), _engineError ?? Loc.T("startup.ollamaNotRunning")),
            (true, 0)  => (Loc.T("startup.noModels"), Loc.T("startup.noModelsHint")),
            _          => (Loc.F("startup.ready", _modelCount, Plural(_modelCount)), Loc.T("startup.allReady"))
        };
    }

    private void Step(string key)
    {
        _progressKey = key;
        StatusText   = Loc.T(key);
    }

    public async Task RunChecksAsync()
    {
        IsChecking  = true;
        CanContinue = false;
        _phase      = Phase.Checking;
        UpdateTexts();

        // 1. Background service: start it if it isn't running yet
        Step("startup.connecting");
        ServiceOk    = await ServiceLauncher.EnsureRunningAsync(_ipc, s => StatusText = s);
        ServiceColor = ServiceOk ? Good : Bad;

        // 2. LLM engine: the service starts Ollama if needed
        if (ServiceOk)
        {
            // The service speaks the interface language from now on
            await App.Services.GetRequiredService<LanguageService>().SyncServiceAsync();

            Step("startup.startingLlm");
            var engine = await _ipc.SendCommandAsync(new IpcCommand { Type = IpcCommandType.GetEngineStatus });

            _engineRunning = false;
            _modelCount    = 0;
            _engineError   = null;
            var missing    = false;
            if (engine?.Success == true && engine.Payload is { } p)
            {
                _engineRunning = p.TryGetProperty("Running", out var r) && r.GetBoolean();
                _modelCount    = p.TryGetProperty("Models", out var m) && m.ValueKind == System.Text.Json.JsonValueKind.Array
                    ? m.GetArrayLength() : 0;
                _engineError   = p.TryGetProperty("Error", out var e) && e.ValueKind == System.Text.Json.JsonValueKind.String
                    ? e.GetString() : null;
                missing        = p.TryGetProperty("Missing", out var x) && x.ValueKind == System.Text.Json.JsonValueKind.True;
            }

            LlmOk         = _engineRunning && _modelCount > 0;
            OllamaMissing = !_engineRunning && missing;
            NoModels      = _engineRunning && _modelCount == 0;
            LlmColor      = LlmOk ? Good : Bad;
        }
        else
        {
            LlmColor = Bad;
        }

        _phase       = Phase.Done;
        _progressKey = null;
        UpdateTexts();
        IsChecking  = false;
        CanContinue = true;
    }

    [RelayCommand]
    private async Task InstallOllamaAsync()
    {
        IsInstalling = true;
        CanContinue  = false;
        try
        {
            await OllamaSetup.InstallAsync(new Progress<(string What, double Percent)>(p =>
            {
                InstallPercent = p.Percent;
                StatusText = p.What == OllamaSetup.InstallingStep
                    ? Loc.T("startup.installingOllama")
                    : Loc.F("startup.downloadingOllama", p.Percent);
            }), CancellationToken.None);
        }
        catch (Exception ex)
        {
            StatusText   = Loc.F("startup.ollamaInstallFailed", ex.Message);
            IsInstalling = false;
            CanContinue  = true;
            return;
        }
        IsInstalling = false;
        await RunChecksAsync();
    }

    // Downloads the main model through the service, with progress
    [RelayCommand]
    private async Task InstallRecommendedModelAsync()
    {
        IsInstalling = true;
        CanContinue  = false;
        await _ipc.SendCommandAsync(new IpcCommand
        {
            Type = IpcCommandType.PullModel, Parameters = { ["ModelId"] = RecommendedModel }
        });

        while (true)
        {
            await Task.Delay(1000);
            var resp = await _ipc.SendCommandAsync(new IpcCommand
            {
                Type = IpcCommandType.GetPullStatus, Parameters = { ["ModelId"] = RecommendedModel }
            });
            if (resp?.Payload is not { } p || p.ValueKind != System.Text.Json.JsonValueKind.Object) continue;

            var status = p.GetProperty("Status").GetString() ?? string.Empty;
            InstallPercent = p.GetProperty("Percent").GetDouble();
            StatusText     = Loc.F("startup.downloadingModel", RecommendedModel, InstallPercent);

            if (status == "success") break;
            if (status.StartsWith("error"))
            {
                StatusText   = Loc.F("startup.modelFailed", status);
                IsInstalling = false;
                CanContinue  = true;
                return;
            }
        }
        IsInstalling = false;
        await RunChecksAsync();
    }

    // "3 моделі" / "5 моделей" / "3 models"
    private static string Plural(int n)
    {
        if (Loc.Instance.Lang == "en") return Loc.T(n == 1 ? "plural.model.one" : "plural.model.many");
        var key = n % 10 == 1 && n % 100 != 11 ? "plural.model.one"
            : n % 10 is >= 2 and <= 4 && (n % 100 < 12 || n % 100 > 14) ? "plural.model.few"
            : "plural.model.many";
        return Loc.T(key);
    }

    [RelayCommand]
    private static void Exit() => App.Services.GetRequiredService<TrayService>().Exit();
}
