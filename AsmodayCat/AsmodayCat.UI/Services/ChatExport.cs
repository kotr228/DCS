using AsmodayCat.UI.Localization;
using System.IO;
using System.Text;
using AsmodayCat.Shared.Models;

namespace AsmodayCat.UI.Services;

// A conversation as a readable Markdown document
public static class ChatExport
{
    public static string ToMarkdown(string title, IEnumerable<ChatMessageDto> messages)
    {
        var sb = new StringBuilder($"# {title}\n\n");
        foreach (var m in messages)
        {
            sb.AppendLine($"### {(m.Role == ChatRole.User ? Loc.T("chat.you") : "AsmodayCat")} · {m.Timestamp:dd.MM.yyyy HH:mm}");
            foreach (var a in m.AttachmentPaths) sb.AppendLine($"📎 {Path.GetFileName(a)}  ");
            sb.AppendLine().AppendLine(m.Content.Trim()).AppendLine();
        }
        return sb.ToString();
    }

    // File name from the conversation title, without characters Windows doesn't allow
    public static string SuggestFileName(string title, DateTime date)
    {
        var name = string.Concat(title.Split(Path.GetInvalidFileNameChars())).Trim();
        if (name.Length == 0) name = Loc.T("chat.conversation");
        return $"{(name.Length > 40 ? name[..40].Trim() : name)} {date:yyyy-MM-dd}.md";
    }
}
