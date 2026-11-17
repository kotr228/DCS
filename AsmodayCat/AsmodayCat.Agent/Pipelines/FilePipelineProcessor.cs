using AsmodayCat.Shared.Text;
using System.Text;
using AsmodayCat.Agent.Documents;
using AsmodayCat.Agent.Memory;
using AsmodayCat.Agent.Tools;
using AsmodayCat.Agent.Watchers;
using AsmodayCat.Core.Engine;
using AsmodayCat.Shared.Enums;
using AsmodayCat.Shared.Interfaces;
using AsmodayCat.Shared.Models;
using Microsoft.Extensions.Logging;

namespace AsmodayCat.Agent.Pipelines;

// Processes files from one watched folder, one at a time:
// read → LLM (with tools) → <name>.result.md in the output folder.
public class FilePipelineProcessor
{
    private const int MaxInputChars = 24000;

    private readonly AgentRunner      _runner;
    private readonly ModelSwitcher    _models;
    private readonly IApprovalService _approvals;
    private readonly IActivityLog     _activity;
    private readonly AssistantMemory  _memory;
    private readonly ImageDescriber   _describer;
    private readonly INotificationSink _notify;
    private readonly ILogger          _logger;

    public FilePipelineProcessor(
        AgentRunner runner, ModelSwitcher models, IApprovalService approvals,
        IActivityLog activity, AssistantMemory memory, INotificationSink notify, ILogger logger)
    {
        _notify    = notify;
        _describer = new ImageDescriber(runner, models);
        _memory    = memory;
        _runner    = runner;
        _models    = models;
        _approvals = approvals;
        _activity  = activity;
        _logger    = logger;
    }

    public async Task RunAsync(FolderObserver observer, CancellationToken ct)
    {
        try
        {
            await foreach (var filePath in observer.PendingFiles.ReadAllAsync(ct))
            {
                var name = Path.GetFileName(filePath);
                try
                {
                    await ProcessFileAsync(filePath, observer, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    _logger.LogInformation("Cancelled while processing {File}", name);
                    break;
                }
                catch (Exception ex)
                {
                    // NFR2: a failed file never stops the watcher — the error goes to a file next to the results
                    _logger.LogError("Failed to process {File}: {Error}", name, ex.Message);
                    _activity.LogActivity(name, "Error");
                    _notify.Notify(L.T("Агент: помилка", "Agent: error", "Агент: ошибка"), $"{name}: {ex.Message}", observer.OutputPath, isError: true);
                    await WriteErrorArtifactAsync(filePath, observer, ex);
                }
            }
        }
        catch (OperationCanceledException) { /* watcher stopped */ }
    }

    private async Task ProcessFileAsync(string filePath, FolderObserver observer, CancellationToken ct)
    {
        var config = observer.Config;
        var name   = Path.GetFileName(filePath);
        var outDir = observer.OutputPath;
        Directory.CreateDirectory(outDir);

        var outPath = Path.Combine(outDir, name + FolderObserver.ResultSuffix);

        _logger.LogInformation("Processing {File}", name);
        _activity.LogActivity(name, "Processing");

        var doc = await DocumentReader.ReadAsync(filePath, MaxInputChars, ct);

        // Images: the vision model describes the picture, then the main model does the task
        // (report, translation…) from that text — with tools and in the requested language
        if (doc.Kind == DocumentKind.Image)
        {
            var description = await _describer.DescribeAsync(filePath, ct);
            doc = new DocumentContent(DocumentKind.Text, ImageDescriber.AsPromptBlock(name, description), false);
        }

        var task  = config.Action == AgentAction.Refactor ? ModelTaskType.Coding : ModelTaskType.General;
        var model = await _models.ResolveModelAsync(task, ct);
        var context = new ToolContext
        {
            Source               = L.T($"Агент · {config.Path}", $"Agent · {config.Path}", $"Агент · {config.Path}"),
            Approvals            = _approvals,
            TrustedRoots         = [config.Path, outDir],
            ReadAnywhere         = false,
            AllowFileSystem      = true,
            AllowShell           = true,     // each command still needs approval
            AllowInternet        = config.AllowInternetAccess,
            AllowImages          = true,
            Memory               = _memory,
            WorkingDirectory     = config.Path,
            ImageOutputDirectory = outDir
        };
        List<LlmChatMessage> messages =
        [
            LlmChatMessage.System(BuildSystemPrompt(config, outPath, withTools: true)),
            LlmChatMessage.User(BuildUserPrompt(filePath, doc))
        ];

        var result = await _runner.RunAsync(model, messages, context, e =>
        {
            if (e.Kind == AgentEventKind.Notice) _logger.LogWarning("{Notice}", e.Text);
            return Task.CompletedTask;
        }, ct);

        if (string.IsNullOrWhiteSpace(result.Content))
            throw new InvalidOperationException(L.T("Модель повернула порожню відповідь.", "The model returned an empty answer.", "Модель вернула пустой ответ."));

        observer.Ignore(outPath);
        await File.WriteAllTextAsync(outPath, result.Content, Encoding.UTF8, ct);

        _logger.LogInformation("Saved {Result} ({Model}, {Tools} tool calls)", Path.GetFileName(outPath), model, result.ToolCalls);
        _memory.RecordAction($"Agent · {config.Path}", $"Processed {filePath} ({config.Action}) → {outPath}");
        _activity.LogActivity(name, "Done");
        _notify.Notify(L.T("Агент обробив файл", "Agent processed a file", "Агент обработал файл"), $"{name} → {Path.GetFileName(outPath)}", outPath);
    }

    public const string VisionInstruction =
        "Look at the image carefully. Write out ALL visible text exactly as written, then describe every object and shape " +
        "with its colour and position, and what the image is (photo, document, screenshot, diagram…).";

    // FR3.2 / FR-A2: the system prompt is Action + custom SystemPrompt + context about folders and tools
    private static string BuildSystemPrompt(AgentFolderConfig config, string outPath, bool withTools)
    {
        var action = config.Action switch
        {
            AgentAction.CreateReport => "Analyze the file and write a structured report about it.",
            AgentAction.Translate    => "Translate the file content to English, keeping its structure and formatting.",
            AgentAction.Refactor     => "Refactor the code for clarity and performance. Return the full improved code with a short summary of the changes.",
            _                        => "Process the file."
        };

        var sb = new StringBuilder();
        sb.AppendLine("You are AsmodayCat, an autonomous file-processing agent on the user's Windows PC.");
        sb.AppendLine("A new file appeared in a watched folder. " + action);
        sb.AppendLine("Unless the user's instructions below say otherwise, answer in the language of the file's content.");

        if (!string.IsNullOrWhiteSpace(config.SystemPrompt))
            sb.AppendLine().AppendLine("Instructions from the user for this folder (they take priority, including the language of your answer):")
              .AppendLine(config.SystemPrompt.Trim());

        sb.AppendLine();
        sb.AppendLine($"Your final answer is saved automatically to \"{outPath}\" as Markdown. " +
                      "Write the result itself, not a description of what you did.");

        if (withTools)
        {
            sb.AppendLine($"Working folder: {config.Path}. Output folder: {Path.GetDirectoryName(outPath)}.");
            sb.AppendLine("Use tools only when they help (reading related files, checking facts). " +
                          "Shell commands and anything outside these folders need the user's approval and may be denied; " +
                          "if an action is denied, finish the task without it.");
            if (config.AllowInternetAccess)
                sb.AppendLine("You may search the internet with web_search and read pages with web_fetch when current information is needed.");
        }

        return sb.ToString();
    }

    private static string BuildUserPrompt(string filePath, DocumentContent doc)
    {
        var name = Path.GetFileName(filePath);
        return doc.Kind switch
        {
            DocumentKind.Binary =>
                $"New file: {name}\nIt is a binary file, so only its metadata is available:\n{doc.Text}",
            _ =>
                $"New file: {name}{(doc.Truncated ? " (content truncated)" : "")}\n\n" +
                $"<file_content>\n{doc.Text}\n</file_content>"
        };
    }

    private async Task WriteErrorArtifactAsync(string filePath, FolderObserver observer, Exception ex)
    {
        try
        {
            Directory.CreateDirectory(observer.OutputPath);
            var errorPath = Path.Combine(observer.OutputPath, Path.GetFileName(filePath) + FolderObserver.ErrorSuffix);
            observer.Ignore(errorPath);
            await File.WriteAllTextAsync(errorPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\n{ex.Message}\n\n{ex}");
        }
        catch (Exception writeEx)
        {
            _logger.LogWarning("Could not write error file for {File}: {Error}", filePath, writeEx.Message);
        }
    }
}
