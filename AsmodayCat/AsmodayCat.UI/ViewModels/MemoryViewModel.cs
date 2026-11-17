using AsmodayCat.UI.Localization;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AsmodayCat.Shared.Models;
using AsmodayCat.UI.Services;

namespace AsmodayCat.UI.ViewModels;

// What the assistant remembers: browse, search, delete, add facts by hand.
public partial class MemoryViewModel : ObservableObject
{
    private readonly IpcClient _ipc;
    private static readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };

    public ObservableCollection<MemoryItem> Entries { get; } = [];

    [ObservableProperty] private string _query         = string.Empty;
    [ObservableProperty] private string _newFact       = string.Empty;
    [ObservableProperty] private string _statusMessage = string.Empty;

    public MemoryViewModel(IpcClient ipc) => _ipc = ipc;

    [RelayCommand]
    public async Task LoadAsync()
    {
        var resp = await _ipc.SendCommandAsync(new IpcCommand
        {
            Type = IpcCommandType.GetMemory, Parameters = { ["Query"] = Query }
        });
        if (resp?.Success != true || resp.Payload is not { } payload)
        {
            StatusMessage = Loc.T("memory.serviceDown");
            return;
        }

        var list = payload.Deserialize<List<MemoryEntryDto>>(_json) ?? [];
        Entries.Clear();
        foreach (var e in list) Entries.Add(new MemoryItem(e));
        StatusMessage = string.IsNullOrWhiteSpace(Query)
            ? Loc.F("memory.count", list.Count)
            : Loc.F("memory.found", list.Count);
    }

    // Search as you type
    partial void OnQueryChanged(string value) => _ = LoadAsync();

    [RelayCommand]
    private async Task DeleteAsync(MemoryItem item)
    {
        await _ipc.SendCommandAsync(new IpcCommand
        {
            Type = IpcCommandType.DeleteMemory, Parameters = { ["Id"] = item.Id.ToString() }
        });
        Entries.Remove(item);
        StatusMessage = Loc.T("memory.deleted");
    }

    [RelayCommand]
    private async Task AddFactAsync()
    {
        var text = NewFact.Trim();
        if (text.Length == 0) return;
        var resp = await _ipc.SendCommandAsync(new IpcCommand
        {
            Type = IpcCommandType.AddMemoryFact, Parameters = { ["Text"] = text }
        });
        if (resp?.Success == true)
        {
            NewFact = string.Empty;
            await LoadAsync();
            StatusMessage = Loc.T("memory.factSaved");
        }
        else StatusMessage = Loc.F("common.error", resp?.Error ?? Loc.T("chat.serviceUnavailable"));
    }

    [RelayCommand]
    private async Task ClearAllAsync()
    {
        if (MessageBox.Show(Loc.T("memory.confirmClear"), Loc.T("memory.confirmTitle"),
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        await _ipc.SendCommandAsync(new IpcCommand { Type = IpcCommandType.ClearMemory });
        await LoadAsync();
        StatusMessage = Loc.T("memory.cleared");
    }
}

public sealed class MemoryItem(MemoryEntryDto dto)
{
    public long   Id   => dto.Id;
    public string Text => dto.Text;
    public string Date => dto.CreatedAt.ToString("dd.MM.yyyy HH:mm");
    public string Kind => dto.Kind;   // fact / action / conversation, shown via {l:TrLabel}
    public string Source => dto.Source;
}
