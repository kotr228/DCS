using AsmodayCat.UI.Localization;
using System.Collections.ObjectModel;
using System.Windows;
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
        StatusMessage = Loc.T("models.refreshing");
        var resp = await _ipc.SendCommandAsync(
            new IpcCommand { Type = IpcCommandType.GetModelPool });

        if (resp?.Success != true || resp.Payload is null)
        {
            StatusMessage = Loc.T("models.serviceDown");
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

        StatusMessage = Loc.F("models.summary", ModelPool.Count, ModelPool.Count(m => m.Status == ModelPoolStatus.Ready || m.Status == ModelPoolStatus.Loaded));
    }

    // ── Action (FR-M3 / FR-M4) — pull or unload based on current status ─────

    [RelayCommand]
    private async Task ActionAsync(LlmModelDto model)
    {
        switch (model.Status)
        {
            case ModelPoolStatus.NotInstalled when !model.IsExternal:
                await BeginPullAsync(model);
                break;
            case ModelPoolStatus.Loaded:
                await UnloadAsync(model);
                break;
        }
    }

    // Remove an installed model from disk (asks first: models are several GB to download again)
    [RelayCommand]
    private async Task DeleteAsync(LlmModelDto model)
    {
        var answer = MessageBox.Show(
            Loc.F("models.confirmDelete", model.Name),
            Loc.T("models.confirmDeleteTitle"), MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;

        StatusMessage = Loc.F("models.deleting", model.Name);
        var resp = await _ipc.SendCommandAsync(new IpcCommand
        {
            Type       = IpcCommandType.DeleteModel,
            Parameters = { ["ModelId"] = model.Name }
        });

        StatusMessage = resp?.Success == true ? Loc.F("models.deleted", model.Name) : Loc.F("models.deleteFailed", resp?.Error ?? Loc.T("chat.serviceUnavailable"));
        await RefreshAsync();
    }

    // ── Pull (download model) ────────────────────────────────────────────────

    private async Task BeginPullAsync(LlmModelDto model)
    {
        model.Status          = ModelPoolStatus.Downloading;
        model.DownloadProgress = 0;
        StatusMessage         = Loc.F("models.downloading", model.Name);

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
        StatusMessage = Loc.F("models.unloading", model.Name);
        var resp = await _ipc.SendCommandAsync(
            new IpcCommand { Type = IpcCommandType.UnloadModel });

        if (resp?.Success == true)
        {
            model.Status       = ModelPoolStatus.Ready;
            model.VramUsageBytes = 0;
            StatusMessage      = Loc.T("models.unloaded");
        }
        else
        {
            StatusMessage = Loc.F("models.unloadFailed", resp?.Error);
        }
    }

    // ── Poll pull progress (every model being downloaded, so several can run at once) ──

    private readonly HashSet<string> _downloading = new(StringComparer.OrdinalIgnoreCase);

    private void StartPolling(string modelId)
    {
        _downloading.Add(modelId);
        if (_pollTimer is not null) return;
        _pollTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(800) };
        _pollTimer.Tick += async (_, _) =>
        {
            foreach (var id in _downloading.ToList()) await PollAsync(id);
            if (_downloading.Count == 0) StopPolling();
        };
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

        if (resp?.Payload is not { } payload || payload.ValueKind != JsonValueKind.Object) return;

        var progress = payload.Deserialize<ModelPullProgress>(_opts);
        if (progress is null) return;

        var item = ModelPool.FirstOrDefault(m => m.Name.Equals(modelId, StringComparison.OrdinalIgnoreCase));
        if (item is null) return;

        item.DownloadProgress = progress.Percent;
        StatusMessage         = $"{modelId}: {progress.Percent:F0}%";

        if (progress.Status == "success")
        {
            _downloading.Remove(modelId);
            item.Status           = ModelPoolStatus.Ready;
            item.DownloadProgress = 100;
            if (_downloading.Count == 0) await RefreshAsync();
            StatusMessage         = Loc.F("models.ready", modelId);
        }
        else if (progress.Status.StartsWith("error", StringComparison.OrdinalIgnoreCase) || progress.Status == "cancelled")
        {
            // The service reports "error: <reason>"
            _downloading.Remove(modelId);
            item.Status           = ModelPoolStatus.NotInstalled;
            item.DownloadProgress = 0;
            StatusMessage         = progress.Status == "cancelled"
                ? Loc.F("models.cancelled", modelId)
                : $"{modelId}: {progress.Status}";
        }
    }

    [RelayCommand]
    private async Task CancelDownloadAsync(LlmModelDto model)
    {
        await _ipc.SendCommandAsync(new IpcCommand
        {
            Type = IpcCommandType.CancelPull, Parameters = { ["ModelId"] = model.Name }
        });
        StatusMessage = Loc.F("models.cancelling", model.Name);
    }

    // Every model of the recommended set that isn't installed yet (Stable Diffusion is separate)
    [RelayCommand]
    private async Task InstallRecommendedAsync()
    {
        var missing = ModelPool.Where(m => m.Status == ModelPoolStatus.NotInstalled && !m.IsExternal
                                           && m.RecommendedTask != "Custom" && m.RecommendedTask != "FastAnalytics").ToList();
        if (missing.Count == 0)
        {
            StatusMessage = Loc.T("models.allInstalled");
            return;
        }
        foreach (var model in missing) await BeginPullAsync(model);
        StatusMessage = Loc.F("models.downloadingList", string.Join(", ", missing.Select(m => m.Name)));
    }
}
