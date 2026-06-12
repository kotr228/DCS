using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AsmodayCat.UI.Services;

namespace AsmodayCat.UI.ViewModels;

public partial class DashboardViewModel : ObservableObject
{
    private readonly IpcClient _ipc;

    [ObservableProperty] private double _cpuLoad;
    [ObservableProperty] private double _ramFreeMb;
    [ObservableProperty] private string _serviceStatus = "Unknown";
    [ObservableProperty] private string _activeModel = "—";
    [ObservableProperty] private string _killSwitchStatus = string.Empty;

    public DashboardViewModel(IpcClient ipc)
    {
        _ipc = ipc;
    }

    [RelayCommand]
    private async Task KillAllAsync()
    {
        var resp = await _ipc.SendCommandAsync(new IpcCommand { Type = IpcCommandType.KillSwitch });
        KillSwitchStatus = resp?.Success == true ? "All tasks stopped." : $"Error: {resp?.Error}";
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        var statusResp = await _ipc.SendCommandAsync(
            new IpcCommand { Type = IpcCommandType.GetStatus });
        ServiceStatus = statusResp?.Success == true ? "Running" : "Offline";

        var loadResp = await _ipc.SendCommandAsync(
            new IpcCommand { Type = IpcCommandType.GetSystemLoad });

        if (loadResp?.Payload is JsonElement payload)
        {
            CpuLoad = payload.TryGetProperty("CpuLoad", out var cpu) ? cpu.GetDouble() : 0;
            RamFreeMb = payload.TryGetProperty("RamFree", out var ram)
                ? ram.GetInt64() / 1024.0 / 1024.0
                : 0;
        }
    }
}
