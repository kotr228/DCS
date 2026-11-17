using AsmodayCat.Agent.Pipelines;
using AsmodayCat.Core.Engine;
using AsmodayCat.Shared.Enums;
using AsmodayCat.Shared.Models;

namespace AsmodayCat.Agent.Documents;

// Turns an image into a detailed text description with the vision model (llava).
// The main model then answers from that text: it writes better Ukrainian and can use tools,
// which the small vision models can't.
public sealed class ImageDescriber
{
    private readonly AgentRunner   _runner;
    private readonly ModelSwitcher _models;

    public ImageDescriber(AgentRunner runner, ModelSwitcher models)
    {
        _runner = runner;
        _models = models;
    }

    public async Task<string> DescribeAsync(string imagePath, CancellationToken ct)
    {
        var model = await _models.ResolveModelAsync(ModelTaskType.Vision, ct);
        var bytes = await File.ReadAllBytesAsync(imagePath, ct);

        var messages = new List<LlmChatMessage>
        {
            LlmChatMessage.System("You describe images precisely and completely, in English. Never guess what you cannot see."),
            new() { Role = "user", Content = FilePipelineProcessor.VisionInstruction, Images = [Convert.ToBase64String(bytes)] }
        };

        var result = await _runner.RunAsync(model, messages, context: null, onEvent: null, ct);
        return result.Content.Trim();
    }

    // Text block that replaces the picture in a prompt
    public static string AsPromptBlock(string fileName, string description) =>
        $"<image name=\"{fileName}\">\n(The user attached an image. A vision model described it as follows.)\n{description}\n</image>";
}
