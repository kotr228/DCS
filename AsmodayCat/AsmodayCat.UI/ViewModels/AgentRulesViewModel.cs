using AsmodayCat.UI.Localization;
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
    [ObservableProperty] private bool        _processExisting      = false;

    // Editing an existing rule instead of adding a new one
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsEditing), nameof(SubmitText))]
    private Guid? _editingRuleId;
    public bool   IsEditing  => EditingRuleId is not null;
    public string SubmitText => Loc.T(IsEditing ? "agents.saveChanges" : "agents.add");

    [RelayCommand]
    private void EditRule(AgentRuleDto rule)
    {
        EditingRuleId       = rule.Id;
        NewFolderPath       = rule.InputPath;
        NewOutputPath       = rule.OutputPath;
        NewAction           = Enum.TryParse<AgentAction>(rule.ActionType, out var a) ? a : AgentAction.CreateReport;
        NewSystemPrompt     = rule.SystemPrompt;
        NewExtensions       = rule.AllowedExtensions;
        AllowInternetAccess = rule.AllowInternetAccess;
        ProcessExisting     = false;
        StatusMessage       = Loc.T("agents.editing");
    }

    [RelayCommand]
    private void CancelEdit()
    {
        ClearForm();
        StatusMessage = string.Empty;
    }

    private void ClearForm()
    {
        EditingRuleId       = null;
        NewFolderPath       = string.Empty;
        NewOutputPath       = string.Empty;
        NewSystemPrompt     = string.Empty;
        NewExtensions       = "*.*";
        AllowInternetAccess = false;
        ProcessExisting     = false;
    }
    [ObservableProperty] private string      _statusMessage        = string.Empty;

    public AgentRulesViewModel(IpcClient ipc)
    {
        _ipc = ipc;
        Loc.LanguageChanged += () => OnPropertyChanged(nameof(SubmitText));
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
            StatusMessage = Loc.T("agents.needFolder");
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

        var editing = EditingRuleId;
        if (editing is { } id) rule.Id = id;
        var json = JsonSerializer.Serialize(rule);

        var resp = await _ipc.SendCommandAsync(new IpcCommand
        {
            Type       = editing is null ? IpcCommandType.AddAgentRule : IpcCommandType.UpdateAgentRule,
            Parameters = { ["Rule"] = json, ["ProcessExisting"] = ProcessExisting ? "true" : "false" }
        });

        if (resp?.Success == true)
        {
            await LoadRulesAsync();
            ClearForm();
            StatusMessage = editing is null
                ? Loc.T("agents.added")
                : Loc.T("agents.updated");
        }
        else
        {
            StatusMessage = Loc.F("common.error", resp?.Error ?? Loc.T("chat.serviceUnavailable"));
        }
    }

    [RelayCommand]
    private async Task ReprocessRuleAsync(AgentRuleDto rule)
    {
        if (System.Windows.MessageBox.Show(
                Loc.F("agents.confirmReprocess", rule.InputPath),
                Loc.T("agents.confirmTitle"), System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question)
            != System.Windows.MessageBoxResult.Yes) return;

        var resp = await _ipc.SendCommandAsync(new IpcCommand
        {
            Type       = IpcCommandType.ReprocessAgentRule,
            Parameters = { ["Id"] = rule.Id.ToString() }
        });
        StatusMessage = resp?.Success == true
            ? Loc.F("agents.queued", resp.Payload?.GetInt32())
            : Loc.F("common.error", resp?.Error ?? Loc.T("chat.serviceUnavailable"));
    }

    // Pause or resume a rule; the switch shows the state the service reports back.
    [RelayCommand]
    private async Task ToggleRuleAsync(AgentRuleDto rule)
    {
        var resp = await _ipc.SendCommandAsync(new IpcCommand
        {
            Type       = IpcCommandType.SetAgentRuleActive,
            Parameters = { ["Id"] = rule.Id.ToString(), ["Active"] = rule.IsActive ? "true" : "false" }
        });

        StatusMessage = resp?.Success == true
            ? Loc.T(rule.IsActive ? "agents.resumed" : "agents.paused")
            : Loc.F("common.error", resp?.Error ?? Loc.T("chat.serviceUnavailable"));

        await LoadRulesAsync();
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
            StatusMessage = Loc.T("agents.removed");
        }
        else
        {
            StatusMessage = Loc.F("common.error", resp?.Error ?? Loc.T("chat.serviceUnavailable"));
        }
    }
}
