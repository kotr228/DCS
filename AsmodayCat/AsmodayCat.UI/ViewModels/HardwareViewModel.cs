using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AsmodayCat.Shared.Enums;
using AsmodayCat.UI.Services;

namespace AsmodayCat.UI.ViewModels;

public partial class HardwareViewModel : ObservableObject
{
    private readonly IpcClient _ipc;

    public IReadOnlyList<ExecutionDevice> AvailableDevices { get; } =
        Enum.GetValues<ExecutionDevice>();

    [ObservableProperty] private ExecutionDevice _selectedDevice = ExecutionDevice.CPU;
    [ObservableProperty] private string _statusMessage = string.Empty;

    public HardwareViewModel(IpcClient ipc)
    {
        _ipc = ipc;
    }

    [RelayCommand]
    private async Task ApplyDeviceAsync()
    {
        var resp = await _ipc.SendCommandAsync(new IpcCommand
        {
            Type = IpcCommandType.ChangeHardwareModel,
            Parameters = { ["Device"] = SelectedDevice.ToString() }
        });
        StatusMessage = resp?.Success == true
            ? $"Device set to {SelectedDevice}"
            : $"Error: {resp?.Error}";
    }
}
