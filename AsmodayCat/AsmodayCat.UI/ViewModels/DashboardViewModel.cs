using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AsmodayCat.Shared.Models;
using AsmodayCat.UI.Services;

namespace AsmodayCat.UI.ViewModels;

public partial class DashboardViewModel : ObservableObject
{
    private readonly IpcClient       _ipc;
    private readonly DispatcherTimer _timer;

    private static readonly JsonSerializerOptions _jsonOpts = new() { PropertyNameCaseInsensitive = true };

    // ── Hardware metrics ─────────────────────────────────────────────────────
    [ObservableProperty] private double _cpuLoad;
    [ObservableProperty] private long   _ramFreeMb;
    [ObservableProperty] private long   _ramTotalMb;
    [ObservableProperty] private double _gpuLoad;
    [ObservableProperty] private long   _vramUsedMb;
    [ObservableProperty] private long   _vramTotalMb;

    // ── Service / Model ──────────────────────────────────────────────────────
    [ObservableProperty] private string _serviceStatus     = "Unknown";
    [ObservableProperty] private string _activeModelName   = "—";
    [ObservableProperty] private string _activeModelStatus = "Idle";
    [ObservableProperty] private int    _availableNodes;

    // ── Kill Switch ──────────────────────────────────────────────────────────
    [ObservableProperty] private string _killSwitchStatus = string.Empty;

    // ── Collections ──────────────────────────────────────────────────────────
    public ObservableCollection<AgentActivityLogDto> RecentActivity { get; } = new();

    // ── Charts ───────────────────────────────────────────────────────────────
    public HardwareChartViewModel Charts { get; } = new();

    public DashboardViewModel(IpcClient ipc)
    {
        _ipc   = ipc;
        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _timer.Tick += async (_, _) => await RefreshAsync();
        _timer.Start();
    }

    [RelayCommand]
    private async Task KillAllAsync()
    {
        var resp = await _ipc.SendCommandAsync(new IpcCommand { Type = IpcCommandType.KillSwitch });
        KillSwitchStatus = resp?.Success == true ? "All tasks stopped." : $"Error: {resp?.Error}";
    }

    [RelayCommand]
    private async Task UnloadActiveModelAsync()
    {
        var resp = await _ipc.SendCommandAsync(new IpcCommand
        {
            Type       = IpcCommandType.ChangeHardwareModel,
            Parameters = new() { ["action"] = "unload" }
        });
        ActiveModelStatus = resp?.Success == true ? "Unloaded" : "Error";
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        var statusResp = await _ipc.SendCommandAsync(
            new IpcCommand { Type = IpcCommandType.GetStatus });
        ServiceStatus = statusResp?.Success == true ? "Running" : "Offline";

        var dashResp = await _ipc.SendCommandAsync(
            new IpcCommand { Type = IpcCommandType.GetDashboardStats });

        if (dashResp?.Payload is not JsonElement payload) return;

        var dto = payload.Deserialize<SystemStatusDto>(_jsonOpts);
        if (dto is null) return;

        CpuLoad           = dto.CpuLoadPercent;
        RamFreeMb         = dto.RamFreeMb;
        RamTotalMb        = dto.RamTotalMb;
        GpuLoad           = dto.GpuLoadPercent;
        VramUsedMb        = dto.VramUsedMb;
        VramTotalMb       = dto.VramTotalMb;
        ActiveModelName   = string.IsNullOrEmpty(dto.ActiveModelName) ? "—" : dto.ActiveModelName;
        ActiveModelStatus = dto.ActiveModelStatus;
        AvailableNodes    = dto.AvailableNodesCount;

        RecentActivity.Clear();
        foreach (var entry in dto.RecentActivity.TakeLast(5))
            RecentActivity.Add(entry);

        Charts.UpdateFromDto(dto);
    }
}
