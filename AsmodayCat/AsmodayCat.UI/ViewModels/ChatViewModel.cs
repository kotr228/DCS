using AsmodayCat.UI.Localization;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using AsmodayCat.Shared.Models;
using AsmodayCat.UI.Services;
using AsmodayCat.UI.Voice;

namespace AsmodayCat.UI.ViewModels;

public partial class ChatViewModel : ObservableObject
{
    private readonly IpcClient       _ipc;
    private readonly UiSettingsStore _settings;
    private readonly SpeechInput     _speechIn;
    private readonly SpeechOutput    _speechOut;

    // ── Message list ─────────────────────────────────────────────────────────
    public ObservableCollection<ChatMessageDto> Messages { get; } = [];

    // ── Input state ───────────────────────────────────────────────────────────
    [ObservableProperty] private string _userInput    = string.Empty;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowStop))] private bool _isAgentBusy;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowStop))] private bool _isSpeaking;

    // STOP is useful while the answer is generated and while it is read aloud
    public bool ShowStop => IsAgentBusy || IsSpeaking;
    [ObservableProperty] private string _agentStateText = string.Empty;

    // ── Tool switches: what the assistant may use for the next message ───────
    [ObservableProperty] private bool _allowWeb;
    [ObservableProperty] private bool _allowTools;

    // ── Voice ────────────────────────────────────────────────────────────────
    [ObservableProperty] private bool   _voiceReplies;     // read answers aloud
    [ObservableProperty] private bool   _isListening;
    [ObservableProperty] private string _voiceStatus = string.Empty;

    // ── Saved conversations ──────────────────────────────────────────────────
    public ObservableCollection<ChatSessionDto> Sessions { get; } = [];
    [ObservableProperty] private ChatSessionDto? _selectedSession;
    [ObservableProperty] private string _sessionQuery = string.Empty;

    // Filter the conversation list as the user types
    partial void OnSessionQueryChanged(string value) => _ = RefreshSessionsAsync();
    private bool _syncingSelection;      // true while the code (not the user) sets SelectedSession
    private bool _historyLoaded;

    private static readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };

    // ── Pending attachments (FR-CH5/CH6) ─────────────────────────────────────
    public ObservableCollection<string> PendingAttachments  { get; } = [];
    public bool HasPendingAttachments => PendingAttachments.Count > 0;

    public ChatViewModel(IpcClient ipc, UiSettingsStore settings, SpeechInput speechIn, SpeechOutput speechOut)
    {
        _ipc          = ipc;
        _settings     = settings;
        _speechIn     = speechIn;
        _speechOut    = speechOut;
        _allowWeb     = settings.Current.ChatAllowWeb;
        _allowTools   = settings.Current.ChatAllowTools;
        _voiceReplies = settings.Current.VoiceReplies;
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
        AgentStateText = Loc.T("chat.thinking");
        var failed = false;

        try
        {
            var parameters = new Dictionary<string, string>
            {
                ["Message"]     = text,
                ["UseVision"]   = hasImages.ToString().ToLowerInvariant(),
                ["Attachments"] = string.Join(";", attachments),
                ["AllowWeb"]    = AllowWeb.ToString().ToLowerInvariant(),
                ["AllowTools"]  = AllowTools.ToString().ToLowerInvariant()
            };

            await foreach (var chunk in _ipc.StreamCommandAsync(
                new IpcCommand { Type = IpcCommandType.Chat, Parameters = parameters }))
            {
                if (!string.IsNullOrEmpty(chunk.Error))
                {
                    assistantMsg.Content += "\n" + Loc.F("chat.errorInline", chunk.Error);
                    failed = true;
                    break;
                }

                if (chunk.ToolUsed is not null)
                    AgentStateText = DescribeTool(chunk.ToolUsed);

                if (!string.IsNullOrEmpty(chunk.Chunk))
                {
                    assistantMsg.Content += chunk.Chunk;
                    AgentStateText = Loc.T("chat.writing");
                }

                if (chunk.Done) break;
            }
        }
        finally
        {
            IsAgentBusy    = false;
            AgentStateText = string.Empty;
        }

        await RefreshSessionsAsync();

        if (VoiceReplies && !failed)
            await SpeakAsync(assistantMsg.Content);
    }

    private bool CanSend() => !IsAgentBusy;

    // ── Voice input: mic button / Ctrl+Shift+Space ───────────────────────────
    // First press: listen until a pause, recognize, send. Press again while listening: finish now.
    [RelayCommand(AllowConcurrentExecutions = true)]   // a second press must reach the running one to stop it
    private async Task ToggleListenAsync()
    {
        if (IsListening)
        {
            _speechIn.Stop();
            return;
        }
        if (IsAgentBusy) return;

        _speechOut.Stop();
        IsListening = true;
        try
        {
            if (!SpeechInput.HasMicrophone)
            {
                VoiceStatus = Loc.T("chat.noMic");
                return;
            }

            if (!VoiceAssets.WhisperReady)
                await VoiceAssets.EnsureWhisperAsync(DownloadProgress(), CancellationToken.None);

            var text = await _speechIn.ListenAsync(s => VoiceStatus = s, CancellationToken.None);
            if (string.IsNullOrWhiteSpace(text))
            {
                VoiceStatus = Loc.T("chat.notHeard");
                return;
            }

            VoiceStatus = string.Empty;
            UserInput   = text;
            IsListening = false;
            if (SendCommand.CanExecute(null)) await SendCommand.ExecuteAsync(null);
        }
        catch (Exception ex)
        {
            VoiceStatus = Loc.F("chat.voiceError", ex.Message);
        }
        finally
        {
            IsListening = false;
        }
    }

    private async Task SpeakAsync(string markdown)
    {
        try
        {
            if (!VoiceAssets.PiperReady)
                await VoiceAssets.EnsurePiperAsync(DownloadProgress(), CancellationToken.None);
            VoiceStatus = Loc.T("chat.speaking");
            IsSpeaking  = true;
            await _speechOut.SpeakAsync(markdown);
        }
        catch (Exception ex)
        {
            VoiceStatus = Loc.F("chat.voiceError", ex.Message);
            return;
        }
        finally
        {
            IsSpeaking = false;
        }
        VoiceStatus = string.Empty;
    }

    private IProgress<(string What, double Percent)> DownloadProgress() =>
        new Progress<(string What, double Percent)>(p =>
            VoiceStatus = Loc.F("chat.firstRunDownload", p.What, p.Percent));

    partial void OnVoiceRepliesChanged(bool value)
    {
        if (!value) _speechOut.Stop();
        _settings.Current.VoiceReplies = value;
        _settings.Save();
    }

    // Remember the tool switches between app restarts
    partial void OnAllowWebChanged(bool value)
    {
        _settings.Current.ChatAllowWeb = value;
        _settings.Save();
    }

    partial void OnAllowToolsChanged(bool value)
    {
        _settings.Current.ChatAllowTools = value;
        _settings.Save();
    }

    [RelayCommand]
    private async Task StopAsync()
    {
        _speechOut.Stop();
        if (!IsAgentBusy) return;           // was only speaking
        AgentStateText = Loc.T("chat.stopping");
        await _ipc.SendCommandAsync(new IpcCommand { Type = IpcCommandType.CancelChat });
    }

    private static string DescribeTool(string tool) => tool switch
    {
        "web_search"     => Loc.T("tool.web_search"),
        "web_fetch"      => Loc.T("tool.web_fetch"),
        "file_system"    => Loc.T("tool.file_system"),
        "shell_exec"     => Loc.T("tool.shell_exec"),
        "image_generate" => Loc.T("tool.image_generate"),
        "vision"         => Loc.T("tool.vision"),
        _                => Loc.F("tool.other", tool)
    };

    // Keep CanExecute in sync when IsAgentBusy changes
    partial void OnIsAgentBusyChanged(bool value) => SendCommand.NotifyCanExecuteChanged();

    // ── Clear (FR-CH4: clears UI + resets service context) ──────────────────

    [RelayCommand]
    private async Task ClearChatAsync()
    {
        // Starts a new conversation; the previous one stays in the history list
        Messages.Clear();
        await _ipc.SendCommandAsync(new IpcCommand { Type = IpcCommandType.ClearContext });
        await RefreshSessionsAsync();
    }

    // Called when the chat is first shown: brings back the last conversation
    public async Task LoadHistoryAsync()
    {
        if (_historyLoaded) return;
        var resp = await _ipc.SendCommandAsync(new IpcCommand { Type = IpcCommandType.GetChatHistory });
        if (resp?.Success != true || resp.Payload is not { } payload) return;   // service not up yet: try next time

        _historyLoaded = true;
        ShowMessages(payload.Deserialize<ChatHistoryDto>(_json));
        await RefreshSessionsAsync();
    }

    private void ShowMessages(ChatHistoryDto? history)
    {
        Messages.Clear();
        foreach (var m in history?.Messages ?? []) Messages.Add(m);
    }

    private async Task RefreshSessionsAsync()
    {
        var resp = await _ipc.SendCommandAsync(new IpcCommand
        {
            Type = IpcCommandType.GetChatSessions, Parameters = { ["Query"] = SessionQuery }
        });
        if (resp?.Success != true || resp.Payload is not { } payload) return;

        var list = payload.Deserialize<List<ChatSessionDto>>(_json) ?? [];
        _syncingSelection = true;
        Sessions.Clear();
        foreach (var s in list) Sessions.Add(s);
        SelectedSession = Sessions.FirstOrDefault(s => s.IsCurrent);
        _syncingSelection = false;
    }

    // The user picked a conversation in the list: open it and continue there
    partial void OnSelectedSessionChanged(ChatSessionDto? value)
    {
        if (_syncingSelection || value is null) return;
        _ = OpenSessionAsync(value.Id);
    }

    private async Task OpenSessionAsync(Guid id)
    {
        if (IsAgentBusy) { await RefreshSessionsAsync(); return; }
        _speechOut.Stop();

        var resp = await _ipc.SendCommandAsync(new IpcCommand
        {
            Type = IpcCommandType.OpenChatSession, Parameters = { ["Id"] = id.ToString() }
        });
        if (resp?.Success == true && resp.Payload is { } payload)
            ShowMessages(payload.Deserialize<ChatHistoryDto>(_json));
        await RefreshSessionsAsync();
    }

    // ── Message actions ──────────────────────────────────────────────────────

    [RelayCommand]
    private void CopyMessage(ChatMessageDto message)
    {
        try { Clipboard.SetText(message.Content); }
        catch (System.Runtime.InteropServices.COMException) { /* clipboard busy */ }
        VoiceStatus = Loc.T("chat.copied");
        _ = Task.Delay(1500).ContinueWith(_ => { if (VoiceStatus == Loc.T("chat.copied")) VoiceStatus = string.Empty; },
            TaskScheduler.FromCurrentSynchronizationContext());
    }

    // Edit a sent message: the conversation is cut at it and its text goes back to the input box
    [RelayCommand]
    private async Task EditMessageAsync(ChatMessageDto message)
    {
        var index = Messages.IndexOf(message);
        if (index < 0 || IsAgentBusy) return;
        if (index < Messages.Count - 2 && MessageBox.Show(
                Loc.T("chat.confirmEdit"), "AsmodayCat",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        if (!await TruncateAsync(index)) return;
        UserInput = message.Content;
        PendingAttachments.Clear();
        foreach (var path in message.AttachmentPaths.Where(File.Exists)) PendingAttachments.Add(path);
    }

    // Ask again: drop this answer (and what followed) and resend the question before it
    [RelayCommand]
    private async Task RegenerateMessageAsync(ChatMessageDto message)
    {
        var index = Messages.IndexOf(message);
        if (index < 0 || IsAgentBusy) return;
        var questionIndex = Messages.Take(index).ToList().FindLastIndex(m => m.Role == ChatRole.User);
        if (questionIndex < 0) return;
        if (index < Messages.Count - 1 && MessageBox.Show(
                Loc.T("chat.confirmRegenerate"), "AsmodayCat",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        var question = Messages[questionIndex];
        if (!await TruncateAsync(questionIndex)) return;
        UserInput = question.Content;
        PendingAttachments.Clear();
        foreach (var path in question.AttachmentPaths.Where(File.Exists)) PendingAttachments.Add(path);
        if (SendCommand.CanExecute(null)) await SendCommand.ExecuteAsync(null);
    }

    private async Task<bool> TruncateAsync(int fromIndex)
    {
        _speechOut.Stop();
        var resp = await _ipc.SendCommandAsync(new IpcCommand
        {
            Type = IpcCommandType.TruncateChat, Parameters = { ["FromIndex"] = fromIndex.ToString() }
        });
        if (resp?.Success != true || resp.Payload is not { } payload)
        {
            VoiceStatus = Loc.F("chat.failed", resp?.Error ?? Loc.T("chat.serviceUnavailable"));
            return false;
        }
        ShowMessages(payload.Deserialize<ChatHistoryDto>(_json));
        return true;
    }

    [RelayCommand]
    private async Task DeleteSessionAsync()
    {
        if (SelectedSession is not { } session) return;
        var answer = MessageBox.Show(Loc.F("chat.confirmDelete", session.Title),
            Loc.T("chat.confirmDeleteTitle"), MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;

        await _ipc.SendCommandAsync(new IpcCommand
        {
            Type = IpcCommandType.DeleteChatSession, Parameters = { ["Id"] = session.Id.ToString() }
        });
        if (session.IsCurrent) Messages.Clear();
        await RefreshSessionsAsync();
    }

    // ── Attach file (FR-CH5/CH6) ─────────────────────────────────────────────

    [RelayCommand]
    private void AttachFile()
    {
        var dlg = new OpenFileDialog
        {
            Multiselect = true,
            Title       = Loc.T("chat.attachTitle"),
            Filter      = Loc.T("chat.filterImages") + "|*.png;*.jpg;*.jpeg;*.bmp;*.gif|" +
                          Loc.T("chat.filterDocs") + "|*.txt;*.md;*.csv;*.cs;*.py;*.json;*.xml|" +
                          Loc.T("chat.filterAll") + "|*.*"
        };

        if (dlg.ShowDialog() != true) return;
        AddAttachments(dlg.FileNames);
    }

    // Files dropped on the chat or chosen in the dialog (folders are skipped)
    public void AddAttachments(IEnumerable<string> paths)
    {
        foreach (var path in paths.Where(File.Exists))
            if (!PendingAttachments.Contains(path)) PendingAttachments.Add(path);
    }

    // Ctrl+V: a copied picture becomes a PNG attachment, copied files are attached.
    // Returns false for plain text so the text box pastes it normally.
    public bool PasteFromClipboard()
    {
        try
        {
            if (Clipboard.ContainsFileDropList())
            {
                AddAttachments(Clipboard.GetFileDropList().Cast<string>());
                return true;
            }
            if (Clipboard.ContainsImage() && Clipboard.GetImage() is { } image)
            {
                var dir = Path.Combine(Path.GetTempPath(), "AsmodayCat", "pasted");
                Directory.CreateDirectory(dir);
                var file = Path.Combine(dir, $"image_{DateTime.Now:yyyyMMdd_HHmmss}.png");
                using (var stream = File.Create(file))
                {
                    var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                    encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));
                    encoder.Save(stream);
                }
                AddAttachments([file]);
                return true;
            }
        }
        catch (System.Runtime.InteropServices.COMException) { /* clipboard busy */ }
        return false;
    }

    // Save the visible conversation as a Markdown file
    [RelayCommand]
    private void ExportChat()
    {
        if (Messages.Count == 0) { VoiceStatus = Loc.T("chat.emptyConversation"); return; }

        var title = SelectedSession?.Title ?? Loc.T("chat.conversation");
        var dlg = new SaveFileDialog
        {
            Title    = Loc.T("chat.saveTitle"),
            Filter   = "Markdown (*.md)|*.md|" + Loc.T("chat.filterText") + " (*.txt)|*.txt",
            FileName = ChatExport.SuggestFileName(title, DateTime.Now)
        };
        if (dlg.ShowDialog() != true) return;

        File.WriteAllText(dlg.FileName, ChatExport.ToMarkdown(title, Messages), new System.Text.UTF8Encoding(false));
        VoiceStatus = Loc.F("chat.saved", Path.GetFileName(dlg.FileName));
    }

    [RelayCommand]
    private void RemoveAttachment(string path) => PendingAttachments.Remove(path);

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static readonly HashSet<string> ImageExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".bmp", ".gif" };

    private static bool IsImagePath(string path) =>
        ImageExtensions.Contains(Path.GetExtension(path));
}
