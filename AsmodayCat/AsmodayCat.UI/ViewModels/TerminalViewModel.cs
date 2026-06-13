using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows.Data;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AsmodayCat.Shared.Models;
using AsmodayCat.UI.Services;
using Microsoft.Win32;

namespace AsmodayCat.UI.ViewModels;

public partial class TerminalViewModel : ObservableObject
{
    private readonly IpcClient _ipc;

    private static readonly JsonSerializerOptions _opts =
        new() { PropertyNameCaseInsensitive = true };

    public ObservableCollection<LogEntryDto> AllLogs { get; } = [];
    public ICollectionView FilteredLogs { get; }

    [ObservableProperty] private bool   _showInfo    = true;
    [ObservableProperty] private bool   _showWarning = true;
    [ObservableProperty] private bool   _showError   = true;
    [ObservableProperty] private bool   _showAgent   = true;
    [ObservableProperty] private bool   _showSystem  = true;
    [ObservableProperty] private bool   _showNetwork = true;
    [ObservableProperty] private bool   _autoScroll  = true;
    [ObservableProperty] private string _currentCommand = string.Empty;

    private int _logOffset;
    private DispatcherTimer? _pollTimer;

    public TerminalViewModel(IpcClient ipc)
    {
        _ipc = ipc;

        FilteredLogs = CollectionViewSource.GetDefaultView(AllLogs);
        FilteredLogs.Filter = ApplyFilter;

        _pollTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _pollTimer.Tick += async (_, _) => await PollLogsAsync();
        _pollTimer.Start();
    }

    // ── Filter predicates ────────────────────────────────────────────────────

    private bool ApplyFilter(object item) =>
        item is LogEntryDto e && e.Level switch
        {
            LogEntryLevel.Info    => ShowInfo,
            LogEntryLevel.Warning => ShowWarning,
            LogEntryLevel.Error   => ShowError,
            LogEntryLevel.Agent   => ShowAgent,
            LogEntryLevel.System  => ShowSystem,
            LogEntryLevel.Network => ShowNetwork,
            _ => true
        };

    partial void OnShowInfoChanged(bool _)    => FilteredLogs.Refresh();
    partial void OnShowWarningChanged(bool _) => FilteredLogs.Refresh();
    partial void OnShowErrorChanged(bool _)   => FilteredLogs.Refresh();
    partial void OnShowAgentChanged(bool _)   => FilteredLogs.Refresh();
    partial void OnShowSystemChanged(bool _)  => FilteredLogs.Refresh();
    partial void OnShowNetworkChanged(bool _) => FilteredLogs.Refresh();

    // ── Log polling ──────────────────────────────────────────────────────────

    private async Task PollLogsAsync()
    {
        var resp = await _ipc.SendCommandAsync(new IpcCommand
        {
            Type       = IpcCommandType.GetLogs,
            Parameters = { ["Offset"] = _logOffset.ToString() }
        });

        if (resp?.Success != true || resp.Payload is null) return;

        var batch = resp.Payload.Value.Deserialize<LogBatchDto>(_opts);
        if (batch is null || batch.Entries.Count == 0) return;

        foreach (var entry in batch.Entries)
            AllLogs.Add(entry);

        while (AllLogs.Count > 1000)
            AllLogs.RemoveAt(0);

        _logOffset = batch.NextOffset;
    }

    // ── Commands ─────────────────────────────────────────────────────────────

    [RelayCommand]
    private void Clear()
    {
        AllLogs.Clear();
        _logOffset = 0;
    }

    [RelayCommand]
    private async Task SendCommandAsync()
    {
        var cmd = CurrentCommand.Trim();
        if (string.IsNullOrEmpty(cmd)) return;
        CurrentCommand = string.Empty;

        AllLogs.Add(new LogEntryDto
        {
            Level   = LogEntryLevel.System,
            Message = $"> {cmd}",
            Source  = "CLI"
        });

        var resp = await _ipc.SendCommandAsync(new IpcCommand
        {
            Type       = IpcCommandType.ExecuteCliCommand,
            Parameters = { ["Command"] = cmd }
        });

        if (resp?.Success == true && resp.Payload.HasValue)
        {
            var text = resp.Payload.Value.TryGetProperty("response", out var prop)
                ? prop.GetString()
                : null;
            if (text is not null)
                AllLogs.Add(new LogEntryDto
                {
                    Level   = LogEntryLevel.System,
                    Message = text,
                    Source  = "CLI"
                });
        }
        else
        {
            AllLogs.Add(new LogEntryDto
            {
                Level   = LogEntryLevel.Error,
                Message = resp?.Error ?? "Service offline",
                Source  = "CLI"
            });
        }
    }

    [RelayCommand]
    private async Task ExportLogsAsync()
    {
        var dialog = new SaveFileDialog
        {
            Title    = "Export Terminal Logs",
            Filter   = "Log files (*.log)|*.log|Text files (*.txt)|*.txt",
            FileName = $"asmodaycat_{DateTime.Now:yyyyMMdd_HHmmss}.log"
        };
        if (dialog.ShowDialog() != true) return;

        var lines = AllLogs
            .Select(e => $"[{e.Timestamp:yyyy-MM-dd HH:mm:ss}] [{e.Level,-7}] [{e.Source}] {e.Message}");

        await File.WriteAllLinesAsync(dialog.FileName, lines);

        AllLogs.Add(new LogEntryDto
        {
            Level   = LogEntryLevel.System,
            Message = $"Exported to {dialog.FileName}",
            Source  = "Terminal"
        });
    }
}
