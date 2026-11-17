using System.Text;
using System.Text.RegularExpressions;
using AsmodayCat.Core.Data.Repositories;

namespace AsmodayCat.Agent.Memory;

// What the assistant remembers between conversations and restarts:
// actions it took, short digests of chat exchanges and facts the user asked to keep.
// Only a small, relevant slice goes into each prompt.
public sealed class AssistantMemory
{
    private const int RecentActions   = 5;
    private const int RelevantEntries = 6;
    private const int MaxContextChars = 1500;
    private const int MaxConversations = 2;

    // Answers where the model refused or failed: remembering them teaches it to refuse again
    private static readonly Regex Refusal = new(
        @"(не\s+можу|не\s+маю\s+(доступу|можливост)|не\s+в\s+змозі|на\s+жаль|вибачте,?\s+але|" +
        @"не\s+могу|нет\s+доступа|к\s+сожалению|извините|" +
        @"I\s+can(no|')t|unable\s+to|I\s+don'?t\s+have\s+access|sorry)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly MemoryRepository _repo;

    public AssistantMemory(MemoryRepository repo) => _repo = repo;

    public void RecordAction(string source, string text) =>
        Safe(() => _repo.Add(MemoryKind.Action, text, source));

    public void RecordExchange(string userMessage, string answer)
    {
        if (string.IsNullOrWhiteSpace(userMessage) || string.IsNullOrWhiteSpace(answer)) return;
        if (Refusal.IsMatch(answer)) return;
        var digest = $"User: {Shorten(userMessage, 200)} → Assistant: {Shorten(answer, 300)}";
        Safe(() => _repo.Add(MemoryKind.Conversation, digest, "Chat"));
    }

    public long Remember(string fact, string source = "Chat") => _repo.Add(MemoryKind.Fact, fact, source);
    public bool Forget(long id) => _repo.Delete(id);
    public List<MemoryEntry> Search(string query, int limit = 10) => _repo.Search(query, limit);

    // Prompt section: the latest actions plus entries relevant to the message, within a char budget.
    public string BuildContext(string message)
    {
        List<MemoryEntry> recent, relevant;
        try
        {
            recent   = _repo.Recent(RecentActions, MemoryKind.Action);
            relevant = _repo.Search(message, RelevantEntries);
        }
        catch (Exception)
        {
            return string.Empty;   // memory must never break the chat
        }

        // Facts and actions first; at most a couple of past exchanges (they may have been wrong)
        var entries = relevant.Where(e => e.Kind != MemoryKind.Conversation)
            .Concat(recent)
            .Concat(relevant.Where(e => e.Kind == MemoryKind.Conversation).Take(MaxConversations))
            .DistinctBy(e => e.Id)
            .ToList();
        if (entries.Count == 0) return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine("MEMORY — facts from earlier conversations and things you did before (newest actions included). " +
                      "When the user mentions a file, folder or anything \"from before\", use the exact paths below — never guess a path. " +
                      "Past answers (conversation) may have been wrong: if a tool can do the task now, use it instead of repeating them:");
        foreach (var e in entries)
        {
            var line = $"- #{e.Id} [{e.CreatedAt:yyyy-MM-dd HH:mm}] ({e.Kind}) {Shorten(e.Text, 300)}";
            if (sb.Length + line.Length > MaxContextChars) break;
            sb.AppendLine(line);
        }
        return sb.ToString();
    }

    private static string Shorten(string s, int max)
    {
        s = s.Replace('\n', ' ').Replace('\r', ' ').Trim();
        return s.Length > max ? s[..max] + "…" : s;
    }

    private static void Safe(Action action)
    {
        try { action(); }
        catch (Exception) { /* memory is best-effort */ }
    }
}
