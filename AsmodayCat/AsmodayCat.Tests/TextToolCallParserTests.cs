using AsmodayCat.Agent.Tools;
using AsmodayCat.Agent.Tools.System;
using AsmodayCat.Agent.Tools.Web;

namespace AsmodayCat.Tests;

public class TextToolCallParserTests
{
    private static readonly IReadOnlyList<IAgentTool> Tools = [new FileSystemTool(), new WebSearchTool()];

    private static (string Visible, List<string> Calls) Run(string text)
    {
        var start = TextToolCallParser.FindPossibleStart(text, 0);
        if (start < 0) return (text, []);
        var (prefixEnd, calls) = TextToolCallParser.ExtractFrom(text, start, Tools);
        return (text[..prefixEnd].Trim(), calls.Select(c => $"{c.Name}:{c.Arguments}").ToList());
    }

    [Fact]
    public void Prose_then_json_with_single_backslash_path_becomes_a_call()
    {
        // The exact shape seen in the app: a sentence, then JSON with a Windows path
        var text = "Я створю файл на робочому столі.\n\n{\"name\": \"file_system\", \"arguments\": " +
                   "{\"action\": \"write\", \"content\": \"Текст\", \"path\": \"C:\\Users\\me\\Desktop\\a.txt\"}}";
        var (visible, calls) = Run(text);

        Assert.Equal("Я створю файл на робочому столі.", visible);
        Assert.Single(calls);
        Assert.Contains("file_system", calls[0]);
        Assert.Contains(@"C:\\Users\\me\\Desktop\\a.txt", calls[0]);
    }

    [Fact]
    public void Json_in_the_middle_of_a_sentence_is_found_and_trailing_text_dropped()
    {
        var (visible, calls) = Run("Ось: {\"name\": \"web_search\", \"arguments\": {\"query\": \"новини\"}} Готово!");
        Assert.Equal("Ось:", visible);
        Assert.Single(calls);
    }

    [Fact]
    public void Fenced_json_block_is_a_call()
    {
        var (_, calls) = Run("```json\n{\"name\": \"web_search\", \"arguments\": {\"query\": \"x\"}}\n```");
        Assert.Single(calls);
    }

    [Fact]
    public void Several_calls_in_an_array()
    {
        var (_, calls) = Run("[{\"name\":\"web_search\",\"arguments\":{\"query\":\"a\"}},{\"name\":\"web_search\",\"arguments\":{\"query\":\"b\"}}]");
        Assert.Equal(2, calls.Count);
    }

    [Theory]
    [InlineData("Ось приклад коду:\n```csharp\nif (x) {\n  y();\n}\n```")]
    [InlineData("Звичайна відповідь з [посиланням](https://x.com).")]
    [InlineData("{\"name\": \"unknown_tool\", \"arguments\": {}}")]
    [InlineData("{\"answer\": 42}")]
    public void Ordinary_text_code_and_unknown_tools_are_not_calls(string text)
    {
        var (_, calls) = Run(text);
        Assert.Empty(calls);
    }

    [Fact]
    public void Arguments_given_as_a_json_string_are_parsed()
    {
        var (_, calls) = Run("{\"function\": {\"name\": \"web_search\", \"arguments\": \"{\\\"query\\\": \\\"погода\\\"}\"}}");
        Assert.Single(calls);
        Assert.Contains("погода", calls[0]);
    }
}
