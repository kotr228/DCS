namespace AsmodayCat.Core.Prompts;

public class PromptBuilder
{
    private readonly List<string> _contextChunks = [];
    private string _systemPrompt = string.Empty;

    public PromptBuilder WithSystem(string systemPrompt)
    {
        _systemPrompt = systemPrompt;
        return this;
    }

    public PromptBuilder AddContext(string context)
    {
        if (!string.IsNullOrWhiteSpace(context))
            _contextChunks.Add(context);
        return this;
    }

    public string BuildPrompt(string userInput)
    {
        if (_contextChunks.Count == 0)
            return userInput;

        var contextBlock = string.Join("\n\n", _contextChunks);
        return $"### Context\n{contextBlock}\n\n### Task\n{userInput}";
    }

    public string SystemPrompt => _systemPrompt;

    public void Reset()
    {
        _contextChunks.Clear();
        _systemPrompt = string.Empty;
    }
}
