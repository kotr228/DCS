using AsmodayCat.UI.Localization;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AsmodayCat.Shared.Models;
using AsmodayCat.UI.Services;

namespace AsmodayCat.UI.ViewModels;

public partial class HardwareViewModel : ObservableObject
{
    private readonly IpcClient _ipc;

    private static readonly JsonSerializerOptions _opts =
        new() { PropertyNameCaseInsensitive = true };

    public IReadOnlyList<string> DeviceOptions { get; } =
        ["Auto", "GPU", "CPU"];

    [ObservableProperty] private string _preferredDevice    = "Auto";
    [ObservableProperty] private int    _contextWindowSize  = 4096;
    [ObservableProperty] private int    _maxVramAllocationMb = 0;
    [ObservableProperty] private int    _cpuThreads         = 4;
    [ObservableProperty] private bool   _allowCpuFallback   = true;
    [ObservableProperty] private string _statusMessage      = string.Empty;

    public int MaxSystemThreads { get; } = Math.Min(Environment.ProcessorCount * 2, 64);

    public string VersionText => Loc.F("settings.version", AppVersion.Display, App.LogDirectory);

    // "Start with Windows": stored in the registry, applied at once
    [ObservableProperty] private bool _autostartEnabled = ServiceLauncher.IsAutostartEnabled();

    // ── Connections (saved in the service, applied after its restart) ────────
    [ObservableProperty] private string _ollamaBaseUrl          = string.Empty;
    [ObservableProperty] private string _stableDiffusionBaseUrl = string.Empty;
    [ObservableProperty] private string _chatWorkspacePath      = string.Empty;
    [ObservableProperty] private bool   _isRestarting;

    private async Task LoadConnectionsAsync()
    {
        var resp = await _ipc.SendCommandAsync(new IpcCommand { Type = IpcCommandType.GetConnectionSettings });
        if (resp?.Success != true || resp.Payload?.Deserialize<ConnectionSettingsDto>(_opts) is not { } c) return;
        OllamaBaseUrl          = c.OllamaBaseUrl;
        StableDiffusionBaseUrl = c.StableDiffusionBaseUrl;
        ChatWorkspacePath      = c.ChatWorkspacePath;
    }

    [RelayCommand]
    private async Task SaveConnectionsAsync()
    {
        var dto = new ConnectionSettingsDto
        {
            OllamaBaseUrl          = OllamaBaseUrl,
            StableDiffusionBaseUrl = StableDiffusionBaseUrl,
            ChatWorkspacePath      = ChatWorkspacePath
        };
        var resp = await _ipc.SendCommandAsync(new IpcCommand
        {
            Type = IpcCommandType.SaveConnectionSettings, Parameters = { ["Settings"] = JsonSerializer.Serialize(dto) }
        });
        if (resp?.Success != true)
        {
            StatusMessage = Loc.F("common.error", resp?.Error ?? Loc.T("chat.serviceUnavailable"));
            return;
        }

        IsRestarting = true;
        var ok = await ServiceLauncher.RestartAsync(_ipc, s => StatusMessage = s);
        IsRestarting = false;
        StatusMessage = Loc.T(ok ? "settings.savedRestarted" : "settings.savedNotStarted");
        await LoadConnectionsAsync();
    }

    private bool _revertingAutostart;

    partial void OnAutostartEnabledChanged(bool value)
    {
        if (_revertingAutostart) return;

        if (ServiceLauncher.SetAutostart(value))
        {
            StatusMessage = Loc.T(value ? "settings.autostartOn" : "settings.autostartOff");
            return;
        }

        StatusMessage = Loc.T("settings.autostartNoExe");
        _revertingAutostart = true;
        AutostartEnabled = false;          // switch back without touching the registry again
        _revertingAutostart = false;
    }

    public HardwareViewModel(IpcClient ipc)
    {
        _ipc = ipc;
        Loc.LanguageChanged += () => OnPropertyChanged(nameof(VersionText));
    }

    // Called when the tab opens, so it always shows what the service has saved
    [RelayCommand]
    private async Task LoadConfigAsync()
    {
        await LoadConnectionsAsync();
        var resp = await _ipc.SendCommandAsync(
            new IpcCommand { Type = IpcCommandType.GetHardwareConfig });

        if (resp?.Success != true || resp.Payload is null) return;

        var dto = resp.Payload.Value.Deserialize<HardwareConfigDto>(_opts);
        if (dto is null) return;

        PreferredDevice      = dto.PreferredDevice;
        ContextWindowSize    = dto.ContextWindowSize;
        MaxVramAllocationMb  = dto.MaxVramAllocationMb;
        CpuThreads           = dto.CpuThreads;
        AllowCpuFallback     = dto.AllowCpuFallback;
    }

    [RelayCommand]
    private async Task SaveConfigAsync()
    {
        if (CpuThreads < 1 || CpuThreads > MaxSystemThreads)
        {
            StatusMessage = Loc.F("settings.threadsRange", MaxSystemThreads);
            return;
        }

        if (ContextWindowSize < 2048 || ContextWindowSize > 32768)
        {
            StatusMessage = Loc.T("settings.contextRange");
            return;
        }

        var dto = new HardwareConfigDto
        {
            PreferredDevice      = PreferredDevice,
            ContextWindowSize    = ContextWindowSize,
            MaxVramAllocationMb  = MaxVramAllocationMb,
            CpuThreads           = CpuThreads,
            AllowCpuFallback     = AllowCpuFallback
        };

        var json = JsonSerializer.Serialize(dto);

        var resp = await _ipc.SendCommandAsync(new IpcCommand
        {
            Type       = IpcCommandType.SaveHardwareConfig,
            Parameters = { ["Config"] = json }
        });

        StatusMessage = resp?.Success == true
            ? Loc.T("settings.saved")
            : Loc.F("common.error", resp?.Error);
    }
}
