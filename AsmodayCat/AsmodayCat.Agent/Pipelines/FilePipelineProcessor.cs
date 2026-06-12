using AsmodayCat.Agent.Watchers;
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
            Context = config.SystemPrompt,
            Prompt = content
        };

        var response = await _llm.GenerateAsync(request, cancellationToken);

        var outputPath = BuildOutputPath(filePath);
        await File.WriteAllTextAsync(outputPath, response.Content, cancellationToken);
    }

    private static string BuildOutputPath(string inputPath)
    {
        var dir = Path.GetDirectoryName(inputPath) ?? ".";
        var nameWithoutExt = Path.GetFileNameWithoutExtension(inputPath);
        return Path.Combine(dir, $"{nameWithoutExt}.result.txt");
    }

    private static async Task WriteErrorArtifact(string filePath, Exception ex)
    {
        var outputPath = BuildOutputPath(filePath) + ".error";
        await File.WriteAllTextAsync(outputPath, ex.ToString());
    }
}
