using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using AsmodayCat.Shared.Models;
using AsmodayCat.UI.Services;

namespace AsmodayCat.UI.ViewModels;

public partial class ChatViewModel : ObservableObject
{
    private readonly IpcClient _ipc;

    // ── Message list ─────────────────────────────────────────────────────────
    public ObservableCollection<ChatMessageDto> Messages { get; } = [];

    // ── Input state ───────────────────────────────────────────────────────────
    [ObservableProperty] private string _userInput    = string.Empty;
    [ObservableProperty] private bool   _isAgentBusy;
    [ObservableProperty] private string _agentStateText = string.Empty;

    // ── Pending attachments (FR-CH5/CH6) ─────────────────────────────────────
    public ObservableCollection<string> PendingAttachments  { get; } = [];
    public bool HasPendingAttachments => PendingAttachments.Count > 0;

    public ChatViewModel(IpcClient ipc)
    {
        _ipc = ipc;
        PendingAttachments.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasPendingAttachments));
    }

    // ── Send (FR-CH1 streaming) ───────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync()
    {
        var text = UserInput.Trim();
        if (string.IsNullOrWhiteSpace(text) && PendingAttachments.Count == 0) return;

        var attachments = PendingAttachments.ToList();
        var hasImages   = attachments.Any(IsImagePath);

        UserInput = string.Empty;
        PendingAttachments.Clear();

        // Add user message
        Messages.Add(new ChatMessageDto
        {
            Role            = ChatRole.User,
            Content         = text,
            AttachmentPaths = attachments
        });

        // Placeholder assistant message that grows as tokens arrive
        var assistantMsg = new ChatMessageDto { Role = ChatRole.Assistant };
        Messages.Add(assistantMsg);

        IsAgentBusy    = true;
        AgentStateText = "Agent is thinking...";

        try
        {
            var parameters = new Dictionary<string, string>
            {
                ["Message"]    = text,
                ["UseVision"]  = hasImages.ToString().ToLowerInvariant(),
                ["Attachments"] = string.Join(";", attachments)
            };

            await foreach (var chunk in _ipc.StreamCommandAsync(
                new IpcCommand { Type = IpcCommandType.Chat, Parameters = parameters }))
            {
                if (!string.IsNullOrEmpty(chunk.Error))
                {
                    assistantMsg.Content += $"\n[Error: {chunk.Error}]";
                    break;
                }

                if (chunk.ToolUsed is not null)
                    AgentStateText = $"Using tool: {chunk.ToolUsed}…";

                if (!string.IsNullOrEmpty(chunk.Chunk))
                    assistantMsg.Content += chunk.Chunk;

                if (chunk.Done) break;
            }
        }
        finally
        {
            IsAgentBusy    = false;
            AgentStateText = string.Empty;
        }
    }

    private bool CanSend() => !IsAgentBusy;

    // Keep CanExecute in sync when IsAgentBusy changes
    partial void OnIsAgentBusyChanged(bool value) => SendCommand.NotifyCanExecuteChanged();

    // ── Clear (FR-CH4: clears UI + resets service context) ──────────────────

    [RelayCommand]
    private async Task ClearChatAsync()
    {
        Messages.Clear();
        await _ipc.SendCommandAsync(new IpcCommand { Type = IpcCommandType.ClearContext });
    }

    // ── Attach file (FR-CH5/CH6) ─────────────────────────────────────────────

    [RelayCommand]
    private void AttachFile()
    {
        var dlg = new OpenFileDialog
        {
            Multiselect = true,
            Title       = "Attach files",
            Filter      = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif|" +
                          "Documents|*.txt;*.md;*.csv;*.cs;*.py;*.json;*.xml|" +
                          "All files|*.*"
        };

        if (dlg.ShowDialog() != true) return;

        foreach (var path in dlg.FileNames)
        {
            if (!PendingAttachments.Contains(path))
                PendingAttachments.Add(path);
        }
    }

    [RelayCommand]
    private void RemoveAttachment(string path) => PendingAttachments.Remove(path);

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static readonly HashSet<string> ImageExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".bmp", ".gif" };

    private static bool IsImagePath(string path) =>
        ImageExtensions.Contains(Path.GetExtension(path));
}
