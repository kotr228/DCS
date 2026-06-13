using System.Text;
using AsmodayCat.Agent.Watchers;
using AsmodayCat.Shared.Enums;
using AsmodayCat.Shared.Interfaces;
using AsmodayCat.Shared.Models;

namespace AsmodayCat.Agent.Pipelines;

public class FilePipelineProcessor
{
    private readonly ILLMEngine _llm;

    public FilePipelineProcessor(ILLMEngine llm)
    {
        _llm = llm;
    }

    public async Task RunAsync(FolderObserver observer, CancellationToken cancellationToken)
    {
        await foreach (var filePath in observer.PendingFiles.ReadAllAsync(cancellationToken))
        {
            try
            {
                await ProcessFileAsync(filePath, observer.Config, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // NFR2: падіння генерації не крашить сервіс — логуємо помилку в артефакт
                await WriteErrorArtifact(filePath, ex);
            }
        }
    }

    private async Task ProcessFileAsync(
        string filePath, AgentFolderConfig config, CancellationToken cancellationToken)
    {
        var content = await File.ReadAllTextAsync(filePath, cancellationToken);

        var request = new LlmRequest
        {
            Context = BuildSystemPrompt(config),
            Prompt = content
        };

        var response = await _llm.GenerateAsync(request, cancellationToken);

        var outputPath = BuildOutputPath(filePath, config);
        await File.WriteAllTextAsync(outputPath, response.Content, cancellationToken);
    }

    // FR3.2 / FR-A2: системний промпт формується з Action + SystemPrompt + internet access hint
    private static string BuildSystemPrompt(AgentFolderConfig config)
    {
        var actionInstruction = config.Action switch
        {
            AgentAction.CreateReport => "Analyze the following content and create a structured report.",
            AgentAction.Translate    => "Translate the following content to English.",
            AgentAction.Refactor     => "Refactor the following code for clarity and performance.",
            _                        => "Process the following content."
        };

        var sb = new StringBuilder(actionInstruction);

        if (!string.IsNullOrWhiteSpace(config.SystemPrompt))
            sb.Append($"\n\n{config.SystemPrompt}");

        if (config.AllowInternetAccess)
            sb.Append("\n\nYou have access to WebSearchTool. Use it to retrieve current information from the internet when needed.");

        return sb.ToString();
    }

    private static string BuildOutputPath(string inputPath, AgentFolderConfig config)
    {
        var outDir = string.IsNullOrWhiteSpace(config.OutputPath)
            ? Path.GetDirectoryName(inputPath) ?? "."
            : config.OutputPath;

        Directory.CreateDirectory(outDir);

        var nameWithoutExt = Path.GetFileNameWithoutExtension(inputPath);
        return Path.Combine(outDir, $"{nameWithoutExt}.result.txt");
    }

    private static async Task WriteErrorArtifact(string filePath, Exception ex)
    {
        var errorPath = Path.ChangeExtension(filePath, ".error.txt");
        await File.WriteAllTextAsync(errorPath, ex.ToString());
    }
}
