using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AsmodayCat.Shared.Models;
using AsmodayCat.UI.Services;

namespace AsmodayCat.UI.ViewModels;

public partial class ModelsManagerViewModel : ObservableObject
{
    private readonly IpcClient _ipc;

    public ObservableCollection<LlmModelDto> ModelPool { get; } = [];

    [ObservableProperty] private string _customModelInput = string.Empty;
    [ObservableProperty] private string _statusMessage    = string.Empty;

    private DispatcherTimer? _pollTimer;

    private static readonly JsonSerializerOptions _opts =
        new() { PropertyNameCaseInsensitive = true };

    public ModelsManagerViewModel(IpcClient ipc)
    {
        _ipc = ipc;
    }

    // ── Refresh (FR-M1 / FR-M2) ─────────────────────────────────────────────

    [RelayCommand]
    private async Task RefreshAsync()
    {
        StatusMessage = "Refreshing…";
        var resp = await _ipc.SendCommandAsync(
            new IpcCommand { Type = IpcCommandType.GetModelPool });

        if (resp?.Success != true || resp.Payload is null)
        {
            StatusMessage = "Service offline";
            return;
        }

        var list = resp.Payload.Value.Deserialize<List<LlmModelDto>>(_opts) ?? [];

        // Merge into existing collection: preserve Downloading items so the
        // progress bar doesn't reset mid-download
        var downloading = ModelPool
            .Where(m => m.Status == ModelPoolStatus.Downloading)
            .Select(m => m.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        ModelPool.Clear();
        foreach (var dto in list)
        {
            if (downloading.Contains(dto.Name))
                dto.Status = ModelPoolStatus.Downloading;
            ModelPool.Add(dto);
        }

        StatusMessage = $"{ModelPool.Count} models • {ModelPool.Count(m => m.Status == ModelPoolStatus.Ready || m.Status == ModelPoolStatus.Loaded)} ready";
    }

    // ── Action (FR-M3 / FR-M4) — pull or unload based on current status ─────

    [RelayCommand]
    private async Task ActionAsync(LlmModelDto model)
    {
        switch (model.Status)
        {
            case ModelPoolStatus.NotInstalled:
                await BeginPullAsync(model);
                break;
            case ModelPoolStatus.Loaded:
                await UnloadAsync(model);
                break;
        }
    }

    // ── Pull (download model) ────────────────────────────────────────────────

    private async Task BeginPullAsync(LlmModelDto model)
    {
        model.Status          = ModelPoolStatus.Downloading;
        model.DownloadProgress = 0;
        StatusMessage         = $"Downloading {model.Name}…";

        await _ipc.SendCommandAsync(new IpcCommand
        {
            Type       = IpcCommandType.PullModel,
            Parameters = { ["ModelId"] = model.Name }
        });

        StartPolling(model.Name);
    }

    // FR-M5: pull custom model entered by user
    [RelayCommand]
    private async Task PullCustomModelAsync()
    {
        var name = CustomModelInput.Trim();
        if (string.IsNullOrEmpty(name)) return;

        // Check if already in the list; if not, add it
        var existing = ModelPool.FirstOrDefault(
            m => m.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        if (existing is null)
        {
            existing = new LlmModelDto { Name = name, RecommendedTask = "Custom" };
            ModelPool.Add(existing);
        }

        CustomModelInput = string.Empty;
        await BeginPullAsync(existing);
    }

    // ── Unload (FR-M4) ───────────────────────────────────────────────────────

    private async Task UnloadAsync(LlmModelDto model)
    {
        StatusMessage = $"Unloading {model.Name}…";
        var resp = await _ipc.SendCommandAsync(
            new IpcCommand { Type = IpcCommandType.UnloadModel });

        if (resp?.Success == true)
        {
            model.Status       = ModelPoolStatus.Ready;
            model.VramUsageBytes = 0;
            StatusMessage      = "Model unloaded";
        }
        else
        {
            StatusMessage = $"Unload failed: {resp?.Error}";
        }
    }

    // ── Poll pull progress ────────────────────────────────────────────────────

    private void StartPolling(string modelId)
    {
        StopPolling();
        _pollTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(800)
        };
        _pollTimer.Tick += async (_, _) => await PollAsync(modelId);
        _pollTimer.Start();
    }

    private void StopPolling()
    {
        _pollTimer?.Stop();
        _pollTimer = null;
    }

    private async Task PollAsync(string modelId)
    {
        var resp = await _ipc.SendCommandAsync(new IpcCommand
        {
            Type       = IpcCommandType.GetPullStatus,
            Parameters = { ["ModelId"] = modelId }
        });

        if (resp?.Payload is null) return;

        var progress = resp.Payload.Value.Deserialize<ModelPullProgress>(_opts);
        if (progress is null) return;

        var item = ModelPool.FirstOrDefault(
            m => m.Name.Equals(modelId, StringComparison.OrdinalIgnoreCase));
        if (item is null) return;

        item.DownloadProgress = progress.Percent;
        StatusMessage         = $"{modelId}: {progress.Status} {progress.Percent:F0}%";

        if (progress.Status == "success")
        {
            item.Status           = ModelPoolStatus.Ready;
            item.DownloadProgress = 100;
            StatusMessage         = $"{modelId} ready";
            StopPolling();
        }
        else if (progress.Status is "error" or "cancelled")
        {
            item.Status = ModelPoolStatus.NotInstalled;
            StatusMessage = $"{modelId}: {progress.Status}";
            StopPolling();
        }
    }
}
