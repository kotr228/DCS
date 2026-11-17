using AsmodayCat.Shared.Text;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AsmodayCat.Agent.Memory;
using AsmodayCat.Agent;
using AsmodayCat.Agent.Documents;
using AsmodayCat.Agent.Tools;
using AsmodayCat.Core.Data.Repositories;
using AsmodayCat.Core.Engine;
using AsmodayCat.Core.Hardware;
using AsmodayCat.Shared.Enums;
using AsmodayCat.Shared.Interfaces;
using AsmodayCat.Shared.Models;

namespace AsmodayCat.Service.Chat;

// The assistant conversation behind the UI chat: keeps history, adds attachments,
// and runs the tool loop when the user enabled tools.
public sealed class ChatSession
{
    private const int MaxAttachmentChars = 20000;

    private readonly AgentRunner               _runner;
    private readonly ModelSwitcher             _models;
    private readonly IApprovalService          _approvals;
    private readonly MetricsCollector          _metrics;
    private readonly HardwareSettingsRepository _hwRepo;
    private readonly AssistantMemory           _memory;
    private readonly ILogger<ChatSession>      _logger;
    private readonly string                    _workspace;

    // Messages that clearly ask for fresh information from the internet…
    private static readonly Regex WebIntent = new(
        @"(знайд|знайти|пошука|пошук|загугл|новин|подій|погод|курс\s+(валют|долар|євро|гривн)|" +
        @"найд|поищ|новост|погод|news|search|google|look\s+up|weather|exchange\s+rate|latest)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex RememberIntent = new(
        @"^\s*(запам['’ʼ]?ятай|запомни|remember)\s*(,\s*що|,\s*что|\s+що|\s+что|\s+that)?\s*[:,—-]?\s*(?<fact>.{3,})",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    // "глянь / покажи / що у мене / що лежить…" — a request to look into a folder
    private static readonly Regex LookIntent = new(
        @"(глян|подив|покаж|переглян|що\s+(є|лежить|там|у\s+мене|в\s+мене)|які\s+файли|список\s+файл|" +
        @"посмотр|покажи|что\s+(есть|лежит|у\s+меня)|look|show|list|what'?s\s+(on|in))",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ExplicitPath = new(@"[A-Za-z]:\\[^\s""'?*<>|]*", RegexOptions.Compiled);

    private static string DownloadsFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

    private static string? FindFolderToList(string message)
    {
        if (!LookIntent.IsMatch(message)) return null;

        if (ExplicitPath.Match(message) is { Success: true } m && Directory.Exists(m.Value.TrimEnd('.', ',')))
            return m.Value.TrimEnd('.', ',');

        var lower = message.ToLowerInvariant();
        if (Regex.IsMatch(lower, @"робоч\w*\s+стол|рабоч\w*\s+стол|desktop"))
            return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (Regex.IsMatch(lower, @"документ|documents"))
            return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (Regex.IsMatch(lower, @"завантаж|загрузк|downloads"))
            return DownloadsFolder;
        return null;
    }

    // …unless they are about local files
    private static readonly Regex LocalIntent = new(
        @"(файл|папк|теці|теку|теки|диск|робоч\w*\s+стол|desktop|folder|file|документ)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly List<LlmChatMessage> _history = [];
    // Quick questions from the Ctrl+Space window: own short history, not saved
    private readonly List<LlmChatMessage> _quickHistory = [];
    private readonly SemaphoreSlim        _gate    = new(1, 1);
    private CancellationTokenSource?      _currentCts;

    // Saved conversations (SQLite): the current one continues after restarts
    private readonly ChatRepository _chats;
    private Guid?                   _sessionId;

    public ChatSession(
        AgentRunner runner, ModelSwitcher models, IApprovalService approvals,
        MetricsCollector metrics, HardwareSettingsRepository hwRepo, AssistantMemory memory,
        ChatRepository chats, IConfiguration configuration, ILogger<ChatSession> logger)
    {
        _chats     = chats;
        _memory    = memory;
        _runner    = runner;
        _models    = models;
        _approvals = approvals;
        _metrics   = metrics;
        _hwRepo    = hwRepo;
        _logger    = logger;
        _workspace = configuration["Chat:WorkspacePath"] is { Length: > 0 } p
            ? Environment.ExpandEnvironmentVariables(p)
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments), "AsmodayCat");
    }

    public string WorkspacePath => _workspace;

    // ── Saved conversations ──────────────────────────────────────────────────

    private bool _restored;

    // On first use after a start, continue the most recent conversation
    private void EnsureRestored()
    {
        if (_restored) return;
        _restored = true;
        try
        {
            var latest = _chats.GetSessions().FirstOrDefault();
            if (latest is not null) LoadSession(latest.Id);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Could not restore the last conversation: {Error}", ex.Message);
        }
    }

    // Text of a saved conversation becomes the model's history again (tool details aren't kept)
    private void LoadSession(Guid id)
    {
        var saved = _chats.GetMessagesBySession(id);
        lock (_history)
        {
            _history.Clear();
            foreach (var m in saved)
            {
                if (m.Role is not (ChatRole.User or ChatRole.Assistant) || string.IsNullOrWhiteSpace(m.Content)) continue;
                _history.Add(new LlmChatMessage { Role = m.Role == ChatRole.User ? "user" : "assistant", Content = m.Content });
            }
        }
        _sessionId = id;
    }

    public List<ChatSessionDto> GetSessions(string? query = null)
    {
        EnsureRestored();
        var sessions = string.IsNullOrWhiteSpace(query) ? _chats.GetSessions() : _chats.SearchSessions(query.Trim());
        return sessions.Select(s => new ChatSessionDto
        {
            Id        = s.Id,
            Title     = s.Title,
            UpdatedAt = s.UpdatedAt.ToLocalTime(),
            IsCurrent = s.Id == _sessionId
        }).ToList();
    }

    public ChatHistoryDto GetHistory(Guid? id = null)
    {
        EnsureRestored();
        var sessionId = id ?? _sessionId;
        return new ChatHistoryDto
        {
            SessionId = sessionId,
            Messages  = sessionId is { } sid ? _chats.GetMessagesBySession(sid) : []
        };
    }

    public ChatHistoryDto Open(Guid id)
    {
        if (!_gate.Wait(0)) throw new InvalidOperationException(L.T("Зачекайте, поки помічник закінчить відповідь.", "Please wait until the assistant finishes its answer.", "Подождите, пока помощник закончит ответ."));
        try
        {
            _restored = true;
            LoadSession(id);
        }
        finally { _gate.Release(); }
        return GetHistory(id);
    }

    // Drops the current conversation from message `fromIndex` on (for "edit" and "regenerate")
    public ChatHistoryDto Truncate(int fromIndex)
    {
        if (!_gate.Wait(0)) throw new InvalidOperationException(L.T("Зачекайте, поки помічник закінчить відповідь.", "Please wait until the assistant finishes its answer.", "Подождите, пока помощник закончит ответ."));
        try
        {
            EnsureRestored();
            if (_sessionId is { } sid)
            {
                _chats.TruncateSession(sid, fromIndex);
                LoadSession(sid);
            }
        }
        finally { _gate.Release(); }
        return GetHistory();
    }

    public void Delete(Guid id)
    {
        _chats.DeleteSession(id);
        if (_sessionId == id) Clear();
    }

    // "CLEAR" starts a new conversation; the old one stays in the history list
    public void Clear()
    {
        _restored  = true;
        _sessionId = null;
        lock (_history) _history.Clear();
    }

    private void Save(ChatRole role, string content, List<string>? attachments = null)
    {
        try
        {
            if (_sessionId is not { } sid) return;
            _chats.AddMessage(sid, new ChatMessageDto { Role = role, Content = content, AttachmentPaths = attachments ?? [] });
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Could not save chat message: {Error}", ex.Message);
        }
    }

    private static string MakeTitle(string message)
    {
        var t = Regex.Replace(message, @"\s+", " ").Trim();
        if (t.Length == 0) t = "Attachment";
        return t.Length > 60 ? t[..60] + "…" : t;
    }

    public void CancelCurrent()
    {
        try { _currentCts?.Cancel(); } catch (ObjectDisposedException) { }
    }

    public async Task RunAsync(
        string message, IReadOnlyList<string> attachments, bool allowWeb, bool allowTools,
        Func<StreamChunkDto, Task> sendRaw, CancellationToken ct, bool quick = false)
    {
        if (!await _gate.WaitAsync(0, ct))
        {
            await sendRaw(new StreamChunkDto { Error = L.T("Помічник ще відповідає на попереднє повідомлення.", "The assistant is still answering the previous message.", "Помощник ещё отвечает на предыдущее сообщение."), Done = true });
            return;
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _currentCts = cts;

        // Everything shown to the user is also saved as the assistant's message
        var shown = new StringBuilder();
        async Task send(StreamChunkDto chunk)
        {
            if (!string.IsNullOrEmpty(chunk.Chunk)) shown.Append(chunk.Chunk);
            if (!string.IsNullOrEmpty(chunk.Error)) shown.Append(L.T($"\n[Помилка: {chunk.Error}]", $"\n[Error: {chunk.Error}]", $"\n[Ошибка: {chunk.Error}]"));
            await sendRaw(chunk);
        }

        var history = quick ? _quickHistory : _history;
        if (!quick)
        {
            EnsureRestored();
            try
            {
                _sessionId ??= _chats.CreateSession(MakeTitle(message));
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Could not create chat session: {Error}", ex.Message);
            }
            Save(ChatRole.User, message, attachments.ToList());
        }

        try
        {
            var images  = attachments.Where(DocumentReader.IsImage).ToList();
            var content = await BuildUserContentAsync(message, attachments.Except(images).ToList(), cts.Token);

            // Images: the vision model describes them first; the main model answers from the text
            // (better Ukrainian, tools available, and the description stays in the history)
            foreach (var image in images.Where(File.Exists))
            {
                await send(new StreamChunkDto { ToolUsed = "vision" });
                var description = await new ImageDescriber(_runner, _models).DescribeAsync(image, cts.Token);
                content += "\n\n" + ImageDescriber.AsPromptBlock(Path.GetFileName(image), description);
            }

            var userMessage = new LlmChatMessage { Role = "user", Content = content };
            var useVision   = false;
            var model = await _models.ResolveModelAsync(useVision ? ModelTaskType.Vision : ModelTaskType.General, cts.Token);
            _metrics.ActiveModelName   = model;
            _metrics.ActiveModelStatus = "Generating";

            // Vision models in the pool don't support tools. Memory is always on otherwise.
            ToolContext? context = useVision ? null : new ToolContext
            {
                Memory               = _memory,
                Source               = L.T("Чат", "Chat", "Чат"),
                Approvals            = _approvals,
                TrustedRoots         = [_workspace],
                ReadAnywhere         = true,
                AllowFileSystem      = allowTools,
                AllowShell           = allowTools,
                AllowInternet        = allowWeb,
                AllowImages          = allowTools,
                WorkingDirectory     = _workspace,
                ImageOutputDirectory = _workspace
            };
            Directory.CreateDirectory(_workspace);

            var messages = BuildMessages(userMessage, includeImages: useVision, context, message, history);
            var before   = messages.Count;

            // Search first when the user plainly asks for news / web info: small models often
            // skip the tool and make things up
            List<LlmToolCall>? upfront = null;
            if (allowWeb && context is not null && WebIntent.IsMatch(message) && !LocalIntent.IsMatch(message))
            {
                var query = message.Length > 200 ? message[..200] : message;
                upfront = [new LlmToolCall { Name = "web_search", Arguments = JsonSerializer.SerializeToElement(new { query }) }];
            }
            // Same for "what's on my desktop / in D:\Projects": list the folder up front
            else if (allowTools && context is not null && FindFolderToList(message) is { } folder)
            {
                upfront = [new LlmToolCall { Name = "file_system", Arguments = JsonSerializer.SerializeToElement(new { action = "list", path = folder }) }];
            }

            var anyText = false;
            var result = await _runner.RunAsync(model, messages, context, async e =>
            {
                switch (e.Kind)
                {
                    case AgentEventKind.Token:
                        anyText = true;
                        await send(new StreamChunkDto { Chunk = e.Text });
                        break;
                    case AgentEventKind.ToolCall:
                        await send(new StreamChunkDto { ToolUsed = e.ToolName });
                        break;
                    case AgentEventKind.ToolResult:
                        // Separate the text before and after a tool call
                        if (anyText) await send(new StreamChunkDto { Chunk = "\n\n" });
                        break;
                    case AgentEventKind.Notice:
                        await send(new StreamChunkDto { Chunk = $"_({e.Text})_\n\n" });
                        break;
                }
            }, cts.Token, upfront);

            if (result.TokensPerSecond > 0) _metrics.LastTokensPerSec = result.TokensPerSecond;
            _memory.RecordExchange(message, result.Content);

            // "Запам'ятай: …" is kept as a fact even if the model didn't call the memory tool
            if (RememberIntent.Match(message) is { Success: true } remember)
                _memory.Remember(remember.Groups["fact"].Value.Trim(), "Chat");

            // Keep the user turn and everything the run produced
            lock (history)
            {
                history.Add(userMessage);
                history.AddRange(messages.Skip(before));
            }

            await send(new StreamChunkDto { Done = true });
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            await send(new StreamChunkDto { Chunk = L.T("\n\n_(зупинено)_", "\n\n_(stopped)_", "\n\n_(остановлено)_"), Done = true });
        }
        catch (Exception ex)
        {
            _logger.LogError("Chat failed: {Error}", ex.Message);
            await send(new StreamChunkDto { Error = ex.Message, Done = true });
        }
        finally
        {
            if (shown.Length > 0 && !quick) Save(ChatRole.Assistant, shown.ToString().Trim());
            _metrics.ActiveModelStatus = "Idle";
            _currentCts = null;
            _gate.Release();
        }
    }

    // Tool results from earlier turns are shortened to this; the latest turn keeps them whole
    private const int OldToolResultChars = 1500;

    private List<LlmChatMessage> BuildMessages(
        LlmChatMessage userMessage, bool includeImages, ToolContext? context, string rawMessage,
        List<LlmChatMessage> source)
    {
        var messages = new List<LlmChatMessage> { LlmChatMessage.System(BuildSystemPrompt(context)) };

        List<LlmChatMessage> history;
        lock (source) history = TrimHistory(source, userMessage.Content.Length);

        foreach (var m in history)
        {
            // Text models can't take images; older image turns stay as text only
            messages.Add(includeImages || m.Images is null ? m : new LlmChatMessage
            {
                Role = m.Role, Content = m.Content, ToolCalls = m.ToolCalls, ToolName = m.ToolName
            });
        }

        // Memory goes right before the new message: small models pay most attention to the end
        var memory = _memory.BuildContext(rawMessage);
        if (memory.Length > 0) messages.Add(LlmChatMessage.System(memory));

        messages.Add(userMessage);
        return messages;
    }

    // Fits the history into the largest context the engine may use (~2 chars per token),
    // keeping room for the new message, tool results of this turn and the answer.
    private List<LlmChatMessage> TrimHistory(List<LlmChatMessage> history, int newMessageChars)
    {
        var maxTokens = Math.Max(_hwRepo.GetConfig().ContextWindowSize, OllamaClient.MaxAutoContextTokens);
        var budget    = (int)(maxTokens * 2.0 * 0.5) - newMessageChars;

        // Shorten tool results of all turns but the latest one
        var lastUser = history.FindLastIndex(m => m.Role == "user");
        var view = history.Select((m, i) => m.Role == "tool" && i < lastUser && m.Content.Length > OldToolResultChars
            ? new LlmChatMessage { Role = "tool", ToolName = m.ToolName, Content = m.Content[..OldToolResultChars] + "… [shortened]" }
            : m).ToList();

        var used  = 0;
        var start = view.Count;
        for (var i = view.Count - 1; i >= 0; i--)
        {
            used += view[i].Content.Length;
            if (used > budget) break;
            start = i;
        }
        while (start < view.Count && view[start].Role != "user") start++;

        // Drop what can no longer be sent
        if (start > 0) history.RemoveRange(0, start);
        return view.Skip(start).ToList();
    }

    // What the app can do, in user terms — so "що можеш?" gets a complete, accurate answer
    // that also tells the user which switch turns a feature on.
    private static string BuildCapabilities(ToolContext? context)
    {
        string State(bool on) => on ? "on" : "off — the user can turn it on with the switch above the chat";

        return
            "ABOUT YOU (use this when the user asks what you can do; answer as a short bullet list grouped by topic):\n" +
            "- Chat: answer questions, explain, write and review code, translate, summarize; attach files (txt, code, .docx, .pdf) or images to discuss them.\n" +
            $"- Internet (\"Web\" switch: {State(context?.AllowInternet == true)}): search the web, read pages, get fresh news with sources.\n" +
            $"- Files & commands (\"Files & commands\" switch: {State(context?.AllowFileSystem == true)}): look into folders (Desktop, Documents, any path), " +
            "read and create files, append to them, run commands (git, dotnet, scripts), generate images if Stable Diffusion WebUI runs. " +
            "Risky actions (commands, writing outside your folder) need the user's approval in a pop-up — by button or by voice (\"approve\" / \"no\").\n" +
            "- Memory: remember facts (\"запам'ятай…\"), files you created and past conversations, even after a restart.\n" +
            "- Folder agent (Agent Rules tab): watch folders and automatically process every new file — report, translation or code refactoring — " +
            "saving results to an output folder.\n" +
            "- Models tab: download, unload or delete local AI models; everything runs locally on this PC via Ollama." +
            Disabled(context);
    }

    private static string Disabled(ToolContext? context)
    {
        var off = new List<string>();
        if (context?.AllowInternet != true)   off.Add("Internet (\"Web\" switch)");
        if (context?.AllowFileSystem != true) off.Add("Files & commands (\"Files & commands\" switch)");
        return off.Count == 0 ? string.Empty :
            $"\nRIGHT NOW these are OFF: {string.Join(", ", off)}. You can't use them in this message. " +
            "When listing what you can do, or when the user asks for such a task, say it is available after turning on that switch above the chat.";
    }

    private string BuildSystemPrompt(ToolContext? context)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are AsmodayCat, a helpful AI assistant running locally on the user's Windows PC.");
        sb.AppendLine("LANGUAGE: always reply in the same language as the user's last message. " +
                      "If the user writes in Ukrainian, reply in Ukrainian — never in Russian.");
        sb.AppendLine("STYLE: write clearly and well structured. Use Markdown: short paragraphs, bullet lists for several items, " +
                      "**bold** for key words, headings only for long answers, code blocks for code. " +
                      "Be complete but don't pad; end without generic filler questions.");
        sb.AppendLine($"Current date and time: {DateTime.Now:yyyy-MM-dd HH:mm}.");
        sb.AppendLine();
        sb.AppendLine(BuildCapabilities(context));

        if (context is not null)
        {
            sb.AppendLine();
            sb.AppendLine("You can call tools when they genuinely help; otherwise answer directly. " +
                          "Call tools only through the function-calling mechanism — never write tool-call JSON in your reply, " +
                          "and never claim an action is done before the tool result confirms it. " +
                          "Don't ask the user for permission to use a tool — just call it; the app asks the user itself when needed.");
            sb.AppendLine($"User folders: Desktop = {Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)}, " +
                          $"Documents = {Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)}, " +
                          $"Downloads = {DownloadsFolder}. Use full paths.");
            if (context.AllowFileSystem)
                sb.AppendLine("You DO have access to the user's files and folders through file_system (list, read, write, append). " +
                              "Never say you can't open or see files — use the tool.");
            if (context.AllowInternet)
            {
                sb.AppendLine("- web_search finds pages online (write queries in the user's language or English). It returns titles, links and the main text of the top pages — " +
                              "answer from that text (the actual news/facts), don't just list sites. Cite the URLs you used.");
                sb.AppendLine("- web_fetch reads a whole page when you need more than the search gave.");
            }
            if (context.AllowFileSystem)
                sb.AppendLine($"- file_system reads and writes files. Your own folder is {_workspace}; writing elsewhere needs the user's approval.");
            if (context.AllowShell)
                sb.AppendLine("- shell_exec runs cmd.exe commands (git, dotnet, scripts); the user approves each one. " +
                              "To read, create or append to files ALWAYS use file_system, never echo/type in the shell.");
            sb.AppendLine("- memory saves facts the user asks you to remember (\"запам'ятай…\") and searches past conversations, " +
                          "files you created and commands you ran. When the user refers to something from before " +
                          "(\"той файл, який ти зробив\"), use the MEMORY section or search memory first.");
            sb.AppendLine("If the user denies an action, don't repeat it; explain or continue without it.");
        }
        return sb.ToString();
    }

    private static async Task<string> BuildUserContentAsync(string message, List<string> files, CancellationToken ct)
    {
        if (files.Count == 0) return message;

        var sb = new StringBuilder(message);
        foreach (var path in files)
        {
            if (!File.Exists(path)) continue;
            var doc = await DocumentReader.ReadAsync(path, MaxAttachmentChars, ct);
            sb.AppendLine().AppendLine();
            sb.AppendLine($"<attached_file name=\"{Path.GetFileName(path)}\" path=\"{path}\"{(doc.Truncated ? " truncated=\"true\"" : "")}>");
            sb.AppendLine(doc.Text);
            sb.AppendLine("</attached_file>");
        }
        return sb.ToString();
    }
}
