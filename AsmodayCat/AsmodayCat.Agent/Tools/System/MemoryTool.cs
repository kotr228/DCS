using System.Text.Json;

namespace AsmodayCat.Agent.Tools.System;

// Lets the model save facts the user wants kept, look up the past, and forget entries.
public class MemoryTool : IAgentTool
{
    public string Name => "memory";

    public string Description =>
        "Long-term memory. 'save' a fact the user asks you to remember, 'search' earlier conversations, " +
        "files you created and saved facts, or 'forget' an entry by id.";

    public object Parameters => new
    {
        type = "object",
        properties = new
        {
            action = new { type = "string", @enum = new[] { "save", "search", "forget" } },
            text   = new { type = "string",  description = "Fact to save, or search query" },
            id     = new { type = "integer", description = "Entry id to forget" }
        },
        required = new[] { "action" }
    };

    public bool IsEnabled(ToolContext context) => context.Memory is not null;

    public Task<string> ExecuteAsync(JsonElement args, ToolContext ctx, CancellationToken ct)
    {
        var memory = ctx.Memory!;
        var text   = args.Str("text")?.Trim() ?? string.Empty;

        string result = args.Str("action")?.ToLowerInvariant() switch
        {
            "save" when text.Length > 0 =>
                ToolJson.Serialize(new { saved = true, id = memory.Remember(text, ctx.Source) }),

            "search" when text.Length > 0 =>
                ToolJson.Serialize(new
                {
                    results = memory.Search(text).Select(e => new
                    {
                        id   = e.Id,
                        kind = e.Kind,
                        date = e.CreatedAt.ToString("yyyy-MM-dd HH:mm"),
                        text = e.Text
                    })
                }),

            "forget" =>
                ToolJson.Serialize(new { forgotten = memory.Forget(args.Int("id", -1)) }),

            _ => ToolRegistry.Error("Use action save (with text), search (with text) or forget (with id).")
        };
        return Task.FromResult(result);
    }
}
