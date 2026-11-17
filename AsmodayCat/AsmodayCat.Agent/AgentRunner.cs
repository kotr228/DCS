using AsmodayCat.Shared.Text;
using System.Text;
using AsmodayCat.Agent.Tools;
using AsmodayCat.Shared.Interfaces;
using AsmodayCat.Shared.Models;
using Microsoft.Extensions.Logging;

namespace AsmodayCat.Agent;

public enum AgentEventKind { Token, ToolCall, ToolResult, Notice }

public sealed record AgentEvent(AgentEventKind Kind, string Text, string? ToolName = null);

public sealed record AgentRunResult(string Content, double TokensPerSecond, int ToolCalls);

// The function-calling loop: ask the model, run the tools it calls, feed results back,
// repeat until it answers without tool calls.
public sealed class AgentRunner
{
    private const int MaxSteps = 8;

    private readonly ILLMEngine           _llm;
    private readonly ToolRegistry         _tools;
    private readonly ILogger<AgentRunner> _logger;

    public AgentRunner(ILLMEngine llm, ToolRegistry tools, ILogger<AgentRunner> logger)
    {
        _llm    = llm;
        _tools  = tools;
        _logger = logger;
    }

    // `messages` is extended in place with the assistant and tool messages of this run.
    public async Task<AgentRunResult> RunAsync(
        string                   model,
        List<LlmChatMessage>     messages,
        ToolContext?             context,
        Func<AgentEvent, Task>?  onEvent,
        CancellationToken        ct,
        IReadOnlyList<LlmToolCall>? initialCalls = null)
    {
        onEvent ??= _ => Task.CompletedTask;

        var enabled = context is null
            ? (IReadOnlyList<IAgentTool>)[]
            : await _tools.GetEnabledAsync(context, ct);
        var definitions = ToolRegistry.ToDefinitions(enabled);

        var toolsSupported = definitions.Count > 0;
        var toolCallCount  = 0;
        double tps         = 0;

        // Calls the app makes up front (e.g. a web search for "find the news…"), so a weak
        // model answers from real data instead of guessing.
        var upfront = initialCalls?
            .Where(c => enabled.Any(t => t.Name.Equals(c.Name, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        if (context is not null && upfront is { Count: > 0 })
        {
            messages.Add(new LlmChatMessage { Role = "assistant", Content = string.Empty, ToolCalls = upfront });
            foreach (var call in upfront)
            {
                toolCallCount++;
                await onEvent(new AgentEvent(AgentEventKind.ToolCall, call.Arguments.ToString(), call.Name));
                var result = await ToolRegistry.ExecuteAsync(call, enabled, context, ct);
                await onEvent(new AgentEvent(AgentEventKind.ToolResult, result, call.Name));
                messages.Add(new LlmChatMessage { Role = "tool", ToolName = call.Name, Content = result });
            }
        }

        for (var step = 0; step <= MaxSteps; step++)
        {
            // Last step: force a plain answer
            var offerTools = toolsSupported && step < MaxSteps;

            var request = new LlmChatRequest
            {
                Model         = model,
                Messages      = messages,
                Tools         = offerTools ? definitions : null,
                ContextTokens = EstimateTokens(messages, offerTools ? definitions : null)
            };

            var content = new StringBuilder();
            var calls   = new List<LlmToolCall>();

            // Some models write a tool call as JSON text (often after a sentence of prose)
            // instead of using tool_calls. From the first point that may start such JSON,
            // text is held back; at the end it becomes a tool call or is shown as usual.
            var streamed  = 0;    // chars of `content` already sent to the user
            var holdFrom  = -1;   // where held-back text starts, or -1

            try
            {
                await foreach (var chunk in _llm.ChatStreamAsync(request, ct))
                {
                    if (chunk.Content.Length > 0)
                    {
                        content.Append(chunk.Content);

                        if (holdFrom < 0)
                        {
                            var text = content.ToString();
                            if (offerTools)
                            {
                                var trigger = TextToolCallParser.FindPossibleStart(text, streamed);
                                if (trigger >= 0) holdFrom = trigger;
                            }

                            // Keep a few chars back so a trigger split across chunks is still caught
                            var safeEnd = holdFrom >= 0 ? holdFrom
                                        : offerTools ? Math.Max(streamed, text.Length - TextToolCallParser.LookBehind)
                                        : text.Length;
                            if (safeEnd > streamed)
                            {
                                await onEvent(new AgentEvent(AgentEventKind.Token, text[streamed..safeEnd]));
                                streamed = safeEnd;
                            }
                        }
                    }
                    if (chunk.ToolCalls is not null) calls.AddRange(chunk.ToolCalls);
                    if (chunk.Done && chunk.TokensPerSecond > 0) tps = chunk.TokensPerSecond;
                }

                var full = content.ToString();
                if (holdFrom >= 0 && calls.Count == 0)
                {
                    var (prefixEnd, textCalls) = TextToolCallParser.ExtractFrom(full, holdFrom, enabled);
                    if (textCalls.Count > 0)
                    {
                        calls.AddRange(textCalls);
                        if (prefixEnd > streamed)
                            await onEvent(new AgentEvent(AgentEventKind.Token, full[streamed..prefixEnd]));
                        content.Clear().Append(full[..prefixEnd].TrimEnd());
                        streamed = full.Length;
                    }
                }

                if (streamed < full.Length && content.Length == full.Length)
                    await onEvent(new AgentEvent(AgentEventKind.Token, full[streamed..]));
            }
            catch (ToolsNotSupportedException) when (offerTools)
            {
                _logger.LogWarning("Model {Model} does not support tools; continuing without them", model);
                await onEvent(new AgentEvent(AgentEventKind.Notice,
                    L.T($"Модель {model} не вміє користуватися інструментами — відповідаю без них.", $"Model {model} can't use tools — answering without them.", $"Модель {model} не умеет пользоваться инструментами — отвечаю без них.")));
                toolsSupported = false;
                step--;
                continue;
            }

            messages.Add(new LlmChatMessage
            {
                Role      = "assistant",
                Content   = content.ToString(),
                ToolCalls = calls.Count > 0 ? calls : null
            });

            if (calls.Count == 0 || context is null)
                return new AgentRunResult(content.ToString(), tps, toolCallCount);

            foreach (var call in calls)
            {
                toolCallCount++;
                var argsText = call.Arguments.ToString();
                _logger.LogInformation("[{Source}] tool {Tool} {Args}", context.Source, call.Name, Shorten(argsText, 200));
                await onEvent(new AgentEvent(AgentEventKind.ToolCall, argsText, call.Name));

                var result = await ToolRegistry.ExecuteAsync(call, enabled, context, ct);

                await onEvent(new AgentEvent(AgentEventKind.ToolResult, result, call.Name));
                messages.Add(new LlmChatMessage { Role = "tool", ToolName = call.Name, Content = result });
            }
        }

        // Unreachable: the last step never offers tools, so it always returns above.
        return new AgentRunResult(string.Empty, tps, toolCallCount);
    }

    // Rough count: ~2 chars per token (Cyrillic tokenizes worse than English), plus room for the answer.
    private const int AnswerReserveTokens = 1536;

    public static int EstimateTokens(IEnumerable<LlmChatMessage> messages, List<LlmToolDefinition>? tools)
    {
        var chars = messages.Sum(m => m.Content.Length + 16
                                      + (m.ToolCalls?.Sum(c => c.Name.Length + c.Arguments.ToString().Length) ?? 0));
        if (tools is not null)
            chars += tools.Sum(t => t.Name.Length + t.Description.Length + System.Text.Json.JsonSerializer.Serialize(t.Parameters).Length);
        var imageTokens = messages.Sum(m => (m.Images?.Count ?? 0) * 768);
        return (int)(chars / 2.0) + imageTokens + AnswerReserveTokens;
    }

    private static string Shorten(string s, int max) => s.Length > max ? s[..max] + "…" : s;
}
