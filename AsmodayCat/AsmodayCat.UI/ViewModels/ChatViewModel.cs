using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AsmodayCat.Shared.Models;
using AsmodayCat.UI.Services;

namespace AsmodayCat.UI.ViewModels;

public partial class ChatViewModel : ObservableObject
{
    private readonly IpcClient _ipc;

    public ObservableCollection<ChatMessage> Messages { get; } = [];

    [ObservableProperty] private string _userInput = string.Empty;
    [ObservableProperty] private bool _isThinking;
    [ObservableProperty] private string _activeToolName = string.Empty;

    public ChatViewModel(IpcClient ipc)
    {
        _ipc = ipc;
    }

    [RelayCommand]
    private async Task SendAsync()
    {
        var text = UserInput.Trim();
        if (string.IsNullOrEmpty(text)) return;

        UserInput = string.Empty;
        Messages.Add(new ChatMessage { Role = "user", Content = text });

        IsThinking = true;
        ActiveToolName = string.Empty;

        try
        {
            var resp = await _ipc.SendCommandAsync(new IpcCommand
            {
                Type = IpcCommandType.Chat,
                Parameters = { ["Message"] = text }
            });

            if (resp?.Success == true && resp.Payload.HasValue)
            {
                var content = resp.Payload.Value.TryGetProperty("Content", out var c)
                    ? c.GetString() ?? string.Empty
                    : string.Empty;

                var toolUsed = resp.Payload.Value.TryGetProperty("ToolUsed", out var t)
                    ? t.GetString()
                    : null;

                if (toolUsed != null)
                {
                    Messages.Add(new ChatMessage
                    {
                        Role     = "tool",
                        Content  = $"Used tool: {toolUsed}",
                        ToolName = toolUsed
                    });
                }

                Messages.Add(new ChatMessage { Role = "assistant", Content = content });
            }
            else
            {
                Messages.Add(new ChatMessage
                {
                    Role    = "assistant",
                    Content = $"[Service offline or error: {resp?.Error}]"
                });
            }
        }
        finally
        {
            IsThinking = false;
            ActiveToolName = string.Empty;
        }
    }

    [RelayCommand]
    private void ClearChat() => Messages.Clear();
}
