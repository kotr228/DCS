using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AsmodayCat.Shared.Enums;
using AsmodayCat.Shared.Models;
using AsmodayCat.UI.Services;

namespace AsmodayCat.UI.ViewModels;

public partial class AgentRulesViewModel : ObservableObject
{
    private readonly IpcClient _ipc;

    private static readonly JsonSerializerOptions _opts =
        new() { PropertyNameCaseInsensitive = true };

    public ObservableCollection<AgentRuleDto> ActiveRules { get; } = [];
    public IReadOnlyList<AgentAction>         AvailableActions { get; } = Enum.GetValues<AgentAction>();

    [ObservableProperty] private string      _newFolderPath        = string.Empty;
    [ObservableProperty] private string      _newOutputPath        = string.Empty;
    [ObservableProperty] private string      _newSystemPrompt      = string.Empty;
    [ObservableProperty] private string      _newExtensions        = "*.*";
    [ObservableProperty] private AgentAction _newAction            = AgentAction.CreateReport;
    [ObservableProperty] private bool        _allowInternetAccess  = false;
    [ObservableProperty] private string      _statusMessage        = string.Empty;

    public AgentRulesViewModel(IpcClient ipc)
    {
        _ipc = ipc;
        _ = LoadRulesAsync();
    }

    [RelayCommand]
    private async Task LoadRulesAsync()
    {
        var resp = await _ipc.SendCommandAsync(
            new IpcCommand { Type = IpcCommandType.GetAgentRules });

        if (resp?.Success != true || resp.Payload is null) return;

        var list = resp.Payload.Value.Deserialize<List<AgentRuleDto>>(_opts) ?? [];
        ActiveRules.Clear();
        foreach (var rule in list)
            ActiveRules.Add(rule);
    }

    [RelayCommand]
    private async Task AddRuleAsync()
    {
        if (string.IsNullOrWhiteSpace(NewFolderPath))
        {
            StatusMessage = "Input path is required.";
            return;
        }

        var rule = new AgentRuleDto
        {
            InputPath           = NewFolderPath,
            OutputPath          = NewOutputPath,
            ActionType          = NewAction.ToString(),
            SystemPrompt        = NewSystemPrompt,
            AllowedExtensions   = string.IsNullOrWhiteSpace(NewExtensions) ? "*.*" : NewExtensions,
            AllowInternetAccess = AllowInternetAccess,
            IsActive            = true
        };

        var json = JsonSerializer.Serialize(rule);

        var resp = await _ipc.SendCommandAsync(new IpcCommand
        {
            Type       = IpcCommandType.AddAgentRule,
            Parameters = { ["Rule"] = json }
        });

        if (resp?.Success == true)
        {
            ActiveRules.Add(rule);
            NewFolderPath       = string.Empty;
            NewOutputPath       = string.Empty;
            NewSystemPrompt     = string.Empty;
            NewExtensions       = "*.*";
            AllowInternetAccess = false;
            StatusMessage       = "Workspace added and monitoring started.";
        }
        else
        {
            StatusMessage = $"Error: {resp?.Error ?? "Service offline"}";
        }
    }

    [RelayCommand]
    private async Task RemoveRuleAsync(Guid id)
    {
        var resp = await _ipc.SendCommandAsync(new IpcCommand
        {
            Type       = IpcCommandType.RemoveAgentRule,
            Parameters = { ["Id"] = id.ToString() }
        });

        if (resp?.Success == true)
        {
            var rule = ActiveRules.FirstOrDefault(r => r.Id == id);
            if (rule is not null) ActiveRules.Remove(rule);
            StatusMessage = "Workspace removed.";
        }
        else
        {
            StatusMessage = $"Error: {resp?.Error ?? "Service offline"}";
        }
    }
}
