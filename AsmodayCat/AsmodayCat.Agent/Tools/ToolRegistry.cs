using System.Text.Json;
using AsmodayCat.Shared.Models;

namespace AsmodayCat.Agent.Tools;

public sealed class ToolRegistry
{
    private const int MaxResultChars = 12000;

    private readonly IReadOnlyList<IAgentTool> _tools;

    public ToolRegistry(IEnumerable<IAgentTool> tools) => _tools = tools.ToList();

    public async Task<IReadOnlyList<IAgentTool>> GetEnabledAsync(ToolContext context, CancellationToken ct)
    {
        var result = new List<IAgentTool>();
        foreach (var tool in _tools)
        {
            if (!tool.IsEnabled(context)) continue;
            if (!await tool.IsAvailableAsync(ct)) continue;
            result.Add(tool);
        }
        return result;
    }

    public static List<LlmToolDefinition> ToDefinitions(IEnumerable<IAgentTool> tools) =>
        tools.Select(t => new LlmToolDefinition
        {
            Name        = t.Name,
            Description = t.Description,
            Parameters  = t.Parameters
        }).ToList();

    public static async Task<string> ExecuteAsync(
        LlmToolCall call, IReadOnlyList<IAgentTool> enabled, ToolContext context, CancellationToken ct)
    {
        var tool = enabled.FirstOrDefault(t => t.Name.Equals(call.Name, StringComparison.OrdinalIgnoreCase));
        if (tool is null)
            return Error($"Tool '{call.Name}' is not available. Available: {string.Join(", ", enabled.Select(t => t.Name))}");

        try
        {
            var result = await tool.ExecuteAsync(call.Arguments, context, ct);
            return result.Length > MaxResultChars ? result[..MaxResultChars] + "\n... [truncated]" : result;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Error(ex.Message);
        }
    }

    public static string Error(string message) => ToolJson.Serialize(new { error = message });

    public static string Denied(string what) =>
        Error($"The user denied this action: {what}. Don't repeat it unless the user asks again; continue without it or explain what you need.");
}

internal static class JsonArgs
{
    public static string? Str(this JsonElement args, string name) =>
        args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out var v)
            ? v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString()
            : null;

    public static int Int(this JsonElement args, string name, int fallback)
    {
        if (args.ValueKind != JsonValueKind.Object || !args.TryGetProperty(name, out var v)) return fallback;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i)) return i;
        return int.TryParse(v.ToString(), out var j) ? j : fallback;
    }
}

// Tool results go to the model as text: keep Cyrillic and other letters as-is
// (the default escapes them as \uXXXX, which is unreadable and ~6x longer).
public static class ToolJson
{
    private static readonly global::System.Text.Json.JsonSerializerOptions Options = new()
    {
        Encoder = global::System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static string Serialize<T>(T value) => global::System.Text.Json.JsonSerializer.Serialize(value, Options);
}
