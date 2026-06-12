using System.Text.Json;
using AsmodayCat.Shared.Interfaces;

namespace AsmodayCat.Agent.Tools;

public class ToolManager
{
    private readonly Dictionary<string, IAgentTool> _tools = new(StringComparer.OrdinalIgnoreCase);

    public void Register(IAgentTool tool) =>
        _tools[tool.GetName()] = tool;

    public IReadOnlyList<IAgentTool> GetAll() =>
        _tools.Values.ToList().AsReadOnly();

    // Повертає JSON-схему інструментів для передачі в LLM як function definitions
    public string GetToolsSchema()
    {
        var tools = _tools.Values.Select(t => new
        {
            name        = t.GetName(),
            description = t.GetDescription()
        });
        return JsonSerializer.Serialize(tools);
    }

    public async Task<string> DispatchAsync(
        string toolName, string jsonArgs, CancellationToken cancellationToken = default)
    {
        if (!_tools.TryGetValue(toolName, out var tool))
            return $"{{\"error\": \"Tool '{toolName}' not found.\"}}";

        try
        {
            return await tool.ExecuteAsync(jsonArgs, cancellationToken);
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = ex.Message });
        }
    }
}
