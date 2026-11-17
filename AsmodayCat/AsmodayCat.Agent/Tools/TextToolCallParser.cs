using System.Text.Json;
using System.Text.RegularExpressions;
using AsmodayCat.Shared.Models;

namespace AsmodayCat.Agent.Tools;

// Recovers tool calls that a model wrote as text instead of using tool_calls, e.g.
//   Sure, I'll create the file.
//   {"name": "file_system", "arguments": {"action": "write", ...}}
// The JSON may be wrapped in ```json fences or <tool_call> tags, and there may be several.
public static class TextToolCallParser
{
    // Chars kept unsent while streaming, so a start marker split across chunks is still seen
    public const int LookBehind = 16;

    // Where tool-call JSON may begin: a '{' or '[{' opening a line, a ```json fence,
    // a <tool_call> tag, or {"name": / {"function": anywhere.
    private static readonly Regex StartMarker = new(
        @"^[ \t]*(?:\{|\[\s*\{|```(?:json)?[ \t]*\r?\n?[ \t]*[\{\[]|<tool_call>)|\{\s*""(?:name|function)""\s*:",
        RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex Wrapper = new(
        @"\G\s*(?:```(?:json)?|<tool_call>|</tool_call>|```)\s*", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static int FindPossibleStart(string text, int from)
    {
        var m = StartMarker.Match(text, Math.Min(from, text.Length));
        return m.Success ? m.Index : -1;
    }

    // Tries each possible start at or after `from`; returns where the calls begin
    // (the text before it is the visible answer) and the calls found.
    public static (int PrefixEnd, List<LlmToolCall> Calls) ExtractFrom(
        string text, int from, IReadOnlyList<IAgentTool> enabled)
    {
        if (enabled.Count == 0) return (text.Length, []);

        for (var m = StartMarker.Match(text, Math.Min(from, text.Length)); m.Success; m = m.NextMatch())
        {
            var calls = ParseAt(text, m.Index, enabled);
            if (calls.Count > 0) return (m.Index, calls);
        }
        return (text.Length, []);
    }

    // Reads consecutive tool-call JSON values starting at `pos`; anything after them is ignored
    // (models often add a premature "Done!" after the call).
    private static List<LlmToolCall> ParseAt(string text, int pos, IReadOnlyList<IAgentTool> enabled)
    {
        var result = new List<LlmToolCall>();
        while (true)
        {
            pos = SkipWrappers(text, pos);
            if (pos >= text.Length || (text[pos] != '{' && text[pos] != '[')) break;

            var end = FindJsonEnd(text, pos);
            if (end < 0) break;

            var found = TryParse(text[pos..end], enabled);
            if (found.Count == 0) break;
            result.AddRange(found);
            pos = end;
        }
        return result;
    }

    private static int SkipWrappers(string text, int pos)
    {
        while (pos < text.Length)
        {
            var m = Wrapper.Match(text, pos);
            if (m.Success && m.Length > 0) { pos += m.Length; continue; }
            if (char.IsWhiteSpace(text[pos])) { pos++; continue; }
            break;
        }
        return pos;
    }

    // Index just past the JSON value starting at `start`, honoring strings and escapes.
    private static int FindJsonEnd(string text, int start)
    {
        var depth = 0;
        var inString = false;
        for (var i = start; i < text.Length; i++)
        {
            var c = text[i];
            if (inString)
            {
                if (c == '\\') i++;
                else if (c == '"') inString = false;
                continue;
            }
            switch (c)
            {
                case '"': inString = true; break;
                case '{' or '[': depth++; break;
                case '}' or ']':
                    if (--depth == 0) return i + 1;
                    break;
            }
        }
        return -1;
    }

    // Models often write Windows paths with single backslashes (C:\Users), which is invalid JSON
    private static readonly Regex BadEscape = new(@"\\(?![""\\/bfnrtu])", RegexOptions.Compiled);

    private static List<LlmToolCall> TryParse(string json, IReadOnlyList<IAgentTool> enabled)
    {
        var calls = TryParseExact(json, enabled);
        return calls.Count > 0 ? calls : TryParseExact(BadEscape.Replace(json, @"\\"), enabled);
    }

    private static List<LlmToolCall> TryParseExact(string json, IReadOnlyList<IAgentTool> enabled)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root  = doc.RootElement;
            var items = root.ValueKind == JsonValueKind.Array ? root.EnumerateArray().ToList() : [root];

            var found = new List<LlmToolCall>();
            foreach (var item in items)
            {
                if (item.ValueKind != JsonValueKind.Object) return [];

                // {"name":..,"arguments":..} or {"function":{"name":..,"arguments":..}}
                var fn = item.TryGetProperty("function", out var f) && f.ValueKind == JsonValueKind.Object ? f : item;
                if (!fn.TryGetProperty("name", out var nameEl) || nameEl.ValueKind != JsonValueKind.String) return [];

                var name = nameEl.GetString()!;
                var tool = enabled.FirstOrDefault(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (tool is null) return [];

                JsonElement args = default;
                var hasArgs = fn.TryGetProperty("arguments", out args) || fn.TryGetProperty("parameters", out args);
                if (hasArgs && args.ValueKind == JsonValueKind.String)
                {
                    using var inner = JsonDocument.Parse(args.GetString() ?? "{}");
                    args = inner.RootElement.Clone();
                }

                found.Add(new LlmToolCall
                {
                    Name      = tool.Name,
                    Arguments = hasArgs && args.ValueKind == JsonValueKind.Object
                        ? args.Clone()
                        : JsonDocument.Parse("{}").RootElement.Clone()
                });
            }
            return found;
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
