using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AsmodayCat.UI.Services;

namespace AsmodayCat.UI.ViewModels;

public partial class StartupViewModel : ObservableObject
{
    private readonly IpcClient _ipc;

    [ObservableProperty] private string _statusText      = "Starting up…";
    [ObservableProperty] private bool   _isChecking      = true;
    [ObservableProperty] private bool   _canContinue;
    [ObservableProperty] private string _serviceStatus   = "Checking…";
    [ObservableProperty] private string _serviceColor    = "#BC907E";
    [ObservableProperty] private string _llmStatus       = "Checking…";
    [ObservableProperty] private string _llmColor        = "#BC907E";

    public StartupViewModel(IpcClient ipc) => _ipc = ipc;

    public async Task RunChecksAsync()
    {
        IsChecking  = true;
        CanContinue = false;

        await Task.Delay(400);

        StatusText = "Connecting to AsmodayCat Service…";
        var response = await _ipc.SendCommandAsync(new IpcCommand { Type = IpcCommandType.GetStatus });

        var serviceOk = response?.Success == true;
        ServiceStatus = serviceOk ? "Running"     : "Not Running";
        ServiceColor  = serviceOk ? "#5E9D55"     : "#BC907E";

        StatusText = serviceOk
            ? "Checking LLM engine…"
            : "Service not running — some features may be unavailable.";

        if (serviceOk)
        {
            await Task.Delay(200);
            var stats = await _ipc.SendCommandAsync(new IpcCommand { Type = IpcCommandType.GetDashboardStats });
            var llmOk = stats?.Success == true;
            LlmStatus = llmOk ? "Ready"    : "Unavailable";
            LlmColor  = llmOk ? "#5E9D55"  : "#BC907E";
            StatusText = "Ready.";
        }
        else
        {
            LlmStatus = "Unavailable";
            LlmColor  = "#BC907E";
        }

        IsChecking  = false;
        CanContinue = true;
    }

    [RelayCommand]
    private static void Exit() => Application.Current.Shutdown();
}
