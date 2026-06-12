using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AsmodayCat.UI.Services;

namespace AsmodayCat.UI.ViewModels;

public partial class ModelEntry : ObservableObject
{
    [ObservableProperty] private string _modelId = string.Empty;
    [ObservableProperty] private string _taskType = string.Empty;
    [ObservableProperty] private int _requiredVramMb;
    [ObservableProperty] private bool _isLocal;
    [ObservableProperty] private string _status = string.Empty;
    [ObservableProperty] private double _pullPercent;
}

public partial class ModelsManagerViewModel : ObservableObject
{
    private readonly IpcClient _ipc;
    private Timer? _pollTimer;

    public ObservableCollection<ModelEntry> Models { get; } = [];

    [ObservableProperty] private string _statusMessage = string.Empty;

    public ModelsManagerViewModel(IpcClient ipc)
    {
        _ipc = ipc;
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        var resp = await _ipc.SendCommandAsync(new IpcCommand { Type = IpcCommandType.ListModels });
        if (resp?.Success != true || resp.Payload is null) return;

        Models.Clear();

        if (resp.Payload.Value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in resp.Payload.Value.EnumerateArray())
            {
                Models.Add(new ModelEntry
                {
                    ModelId       = item.TryGetProperty("ModelId",       out var mi) ? mi.GetString() ?? "" : "",
                    TaskType      = item.TryGetProperty("TaskType",      out var tt) ? tt.GetString() ?? "" : "",
                    RequiredVramMb = item.TryGetProperty("RequiredVramMb", out var rv) ? rv.GetInt32() : 0,
                    IsLocal       = item.TryGetProperty("IsLocal",       out var il) && il.GetBoolean(),
                    Status        = item.TryGetProperty("IsLocal", out var il2) && il2.GetBoolean() ? "Ready" : "Not downloaded"
                });
            }
        }
    }

    [RelayCommand]
    private async Task PullModelAsync(ModelEntry model)
    {
        model.Status      = "Pulling...";
        model.PullPercent = 0;

        await _ipc.SendCommandAsync(new IpcCommand
        {
            Type       = IpcCommandType.PullModel,
            Parameters = { ["ModelId"] = model.ModelId }
        });

        StartPolling(model.ModelId);
    }

    private void StartPolling(string modelId)
    {
        _pollTimer?.Dispose();
        _pollTimer = new Timer(async _ =>
        {
            var resp = await _ipc.SendCommandAsync(new IpcCommand
            {
                Type       = IpcCommandType.GetPullStatus,
                Parameters = { ["ModelId"] = modelId }
            });

            if (resp?.Payload is null) return;

            var percent = resp.Payload.Value.TryGetProperty("Percent", out var p) ? p.GetDouble() : 0;
            var status  = resp.Payload.Value.TryGetProperty("Status",  out var s) ? s.GetString() ?? "" : "";

            var entry = Models.FirstOrDefault(m => m.ModelId == modelId);
            if (entry != null)
            {
                entry.PullPercent = percent;
                entry.Status      = status == "success" ? "Ready" : $"Pulling {percent:F0}%";
                entry.IsLocal     = status == "success";
            }

            if (status == "success" || status == "error")
            {
                _pollTimer?.Dispose();
                _pollTimer = null;
            }
        }, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }
}
