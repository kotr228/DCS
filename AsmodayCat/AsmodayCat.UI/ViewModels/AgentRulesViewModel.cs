using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AsmodayCat.Shared.Models;
using AsmodayCat.UI.Services;

namespace AsmodayCat.UI.ViewModels;

public partial class AgentRulesViewModel : ObservableObject
{
    private readonly IpcClient _ipc;

    public ObservableCollection<AgentFolderConfig> WatchedFolders { get; } = [];

    [ObservableProperty] private string _newFolderPath = string.Empty;
    [ObservableProperty] private string _newSystemPrompt = string.Empty;
    [ObservableProperty] private string _newExtensions = ".txt,.md,.pdf";
    [ObservableProperty] private string _statusMessage = string.Empty;

    public AgentRulesViewModel(IpcClient ipc)
    {
        _ipc = ipc;
    }

    [RelayCommand]
    private async Task AddFolderAsync()
    {
        if (string.IsNullOrWhiteSpace(NewFolderPath)) return;

        var resp = await _ipc.SendCommandAsync(new IpcCommand
        {
            Type = IpcCommandType.StartAgent,
            Parameters =
            {
                ["Path"] = NewFolderPath,
                ["SystemPrompt"] = NewSystemPrompt
            }
        });

        if (resp?.Success == true)
        {
            WatchedFolders.Add(new AgentFolderConfig
            {
                Path = NewFolderPath,
                SystemPrompt = NewSystemPrompt,
                FileExtensionsAllowed = NewExtensions
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToList()
            });
            NewFolderPath = string.Empty;
            NewSystemPrompt = string.Empty;
            StatusMessage = "Agent started.";
        }
        else
        {
            StatusMessage = $"Error: {resp?.Error ?? "Service offline"}";
        }
    }

    [RelayCommand]
    private async Task RemoveFolderAsync(AgentFolderConfig config)
    {
        await _ipc.SendCommandAsync(new IpcCommand
        {
            Type = IpcCommandType.StopAgent,
            Parameters = { ["Path"] = config.Path }
        });
        WatchedFolders.Remove(config);
    }
}
