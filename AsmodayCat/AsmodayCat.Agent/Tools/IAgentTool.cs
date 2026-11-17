using System.Text.Json;

namespace AsmodayCat.Agent.Tools;

public interface IAgentTool
{
    string Name        { get; }
    string Description { get; }

    // JSON schema for the arguments, sent to the model as the function definition
    object Parameters  { get; }

    // Whether this run's permissions allow the tool at all
    bool IsEnabled(ToolContext context);

    // Whether the backing service is reachable (e.g. Stable Diffusion)
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken) => Task.FromResult(true);

    Task<string> ExecuteAsync(JsonElement args, ToolContext context, CancellationToken cancellationToken);
}
