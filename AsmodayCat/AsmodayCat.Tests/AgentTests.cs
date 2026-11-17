using System.Runtime.CompilerServices;
using System.Text.Json;
using AsmodayCat.Agent;
using AsmodayCat.Agent.Tools;
using AsmodayCat.Agent.Tools.System;
using AsmodayCat.Agent.Tools.Web;
using AsmodayCat.Agent.Watchers;
using AsmodayCat.Shared.Interfaces;
using AsmodayCat.Shared.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace AsmodayCat.Tests;

public class FileSystemToolTests : IDisposable
{
    private readonly TempDir _dir = new();
    public void Dispose() => _dir.Dispose();

    private static JsonElement Args(object o) => JsonSerializer.SerializeToElement(o);

    [Fact]
    public async Task Append_to_a_missing_file_is_an_error_not_a_new_file()
    {
        var result = await new FileSystemTool().ExecuteAsync(
            Args(new { action = "append", path = "missing.txt", content = "x" }), Ctx.For(_dir.Path, new FakeApprovals(true)), default);

        Assert.Contains("File not found", result);
        Assert.False(File.Exists(_dir.File("missing.txt")));
    }

    [Fact]
    public async Task Append_starts_on_a_new_line()
    {
        File.WriteAllText(_dir.File("notes.txt"), "перший рядок");
        await new FileSystemTool().ExecuteAsync(
            Args(new { action = "append", path = "notes.txt", content = "другий рядок" }), Ctx.For(_dir.Path, new FakeApprovals(true)), default);

        Assert.Equal(["перший рядок", "другий рядок"], File.ReadAllLines(_dir.File("notes.txt")));
    }

    [Fact]
    public async Task Writing_inside_the_trusted_folder_needs_no_approval()
    {
        var approvals = new FakeApprovals(false);
        await new FileSystemTool().ExecuteAsync(
            Args(new { action = "write", path = "a.md", content = "ok" }), Ctx.For(_dir.Path, approvals), default);

        Assert.Empty(approvals.Asked);
        Assert.Equal("ok", File.ReadAllText(_dir.File("a.md")));
    }

    [Fact]
    public async Task Writing_outside_asks_and_respects_a_denial()
    {
        using var other = new TempDir();
        var approvals = new FakeApprovals(false);
        var result = await new FileSystemTool().ExecuteAsync(
            Args(new { action = "write", path = other.File("x.txt"), content = "no" }), Ctx.For(_dir.Path, approvals), default);

        Assert.Single(approvals.Asked);
        Assert.Contains("denied", result);
        Assert.False(File.Exists(other.File("x.txt")));
    }

    [Fact]
    public async Task Listing_hides_hidden_and_office_temp_files()
    {
        File.WriteAllText(_dir.File("visible.txt"), "");
        File.WriteAllText(_dir.File("~$draft.docx"), "");
        File.WriteAllText(_dir.File("hidden.ini"), "");
        File.SetAttributes(_dir.File("hidden.ini"), FileAttributes.Hidden);

        var result = await new FileSystemTool().ExecuteAsync(
            Args(new { action = "list", path = "." }), Ctx.For(_dir.Path, new FakeApprovals(true)), default);

        Assert.Contains("visible.txt", result);
        Assert.DoesNotContain("~$draft", result);
        Assert.DoesNotContain("hidden.ini", result);
    }

    [Theory]
    [InlineData(@"C:\Work\a.txt", @"C:\Work", true)]
    [InlineData(@"C:\Work", @"C:\Work\", true)]
    [InlineData(@"C:\Workshop\a.txt", @"C:\Work", false)]      // same prefix, different folder
    [InlineData(@"C:\Work\..\Other\a.txt", @"C:\Work", false)] // path traversal
    public void IsUnder_compares_whole_folders(string path, string root, bool expected) =>
        Assert.Equal(expected, ToolContext.IsUnder(path, root));
}

public class FolderObserverTests : IDisposable
{
    private readonly TempDir _dir = new();
    public void Dispose() => _dir.Dispose();

    private FolderObserver Start(params string[] extensions)
    {
        var config = new AgentFolderConfig { Path = _dir.Path, FileExtensionsAllowed = extensions.ToList() };
        var observer = new FolderObserver(config, Path.Combine(_dir.Path, "AsmodayCat_Output"), NullLogger.Instance);
        observer.Start();
        return observer;
    }

    private static async Task<List<string>> CollectAsync(FolderObserver observer, TimeSpan time)
    {
        var found = new List<string>();
        using var cts = new CancellationTokenSource(time);
        try
        {
            await foreach (var f in observer.PendingFiles.ReadAllAsync(cts.Token)) found.Add(Path.GetFileName(f));
        }
        catch (OperationCanceledException) { }
        return found;
    }

    [Fact]
    public async Task New_file_is_emitted_once_and_own_results_temp_files_are_ignored()
    {
        using var observer = Start("*.*");
        File.WriteAllText(_dir.File("task.txt"), "1");
        File.AppendAllText(_dir.File("task.txt"), "2");               // several change events
        File.WriteAllText(_dir.File("task.txt.result.md"), "out");    // agent output
        File.WriteAllText(_dir.File("download.tmp"), "tmp");          // temp file
        File.WriteAllText(_dir.File("~$doc.docx"), "lock");            // Office lock file

        var found = await CollectAsync(observer, TimeSpan.FromSeconds(4));
        Assert.Equal(["task.txt"], found);
    }

    [Fact]
    public async Task Existing_files_without_a_fresh_result_are_queued_and_force_queues_all()
    {
        var outDir = Directory.CreateDirectory(Path.Combine(_dir.Path, "AsmodayCat_Output")).FullName;
        File.WriteAllText(_dir.File("new.txt"), "a");
        File.WriteAllText(_dir.File("done.txt"), "b");
        File.WriteAllText(Path.Combine(outDir, "done.txt.result.md"), "already processed");
        File.SetLastWriteTime(Path.Combine(outDir, "done.txt.result.md"), DateTime.Now.AddMinutes(1));

        var config = new AgentFolderConfig { Path = _dir.Path };
        using var observer = new FolderObserver(config, outDir, NullLogger.Instance);

        Assert.Equal(1, observer.EnqueueExisting(null, force: false));   // only new.txt
        Assert.Equal(0, observer.EnqueueExisting(DateTime.Now.AddHours(1), force: false));   // nothing changed since then
        Assert.Equal(2, observer.EnqueueExisting(null, force: true));    // both

        var found = await CollectAsync(observer, TimeSpan.FromMilliseconds(300));
        Assert.Equal(["done.txt", "new.txt", "new.txt"], found.Order());   // folder order isn't guaranteed
    }

    [Fact]
    public async Task Extension_filter_and_ignore_list_apply()
    {
        using var observer = Start(".md");
        observer.Ignore(_dir.File("skip.md"));
        File.WriteAllText(_dir.File("keep.md"), "a");
        File.WriteAllText(_dir.File("skip.md"), "b");
        File.WriteAllText(_dir.File("other.txt"), "c");

        var found = await CollectAsync(observer, TimeSpan.FromSeconds(4));
        Assert.Equal(["keep.md"], found);
    }
}

public class WebParsingTests
{
    [Fact]
    public void Search_results_are_parsed_with_redirects_decoded_and_ads_skipped()
    {
        const string html = """
            <div class="result"><a rel="nofollow" class="result__a" href="//duckduckgo.com/l/?uddg=https%3A%2F%2Fexample.com%2Fnews&amp;rut=1">Новини <b>дня</b></a>
              <a class="result__snippet" href="x">Короткий <b>опис</b></a></div>
            <div class="result"><a rel="nofollow" class="result__a" href="https://duckduckgo.com/y.js?ad=1">Реклама</a></div>
            <div class="result"><a rel="nofollow" class="result__a" href="https://site.ua/a">Друга</a></div>
            """;
        var results = WebSearchTool.Parse(html);

        Assert.Equal(2, results.Count);
        Assert.Equal("Новини дня", results[0].Title);
        Assert.Equal("https://example.com/news", results[0].Url);
        Assert.Equal("Короткий опис", results[0].Snippet);
        Assert.Equal("https://site.ua/a", results[1].Url);
    }

    [Fact]
    public void Html_to_text_drops_scripts_and_keeps_paragraphs()
    {
        var text = WebHttp.HtmlToText("<html><head><title>t</title></head><body><script>var x=1;</script>" +
                                      "<p>Перший&nbsp;абзац</p><p>Другий &amp; останній</p></body></html>");
        Assert.DoesNotContain("var x", text);
        Assert.Contains("Перший абзац", text);
        Assert.Contains("Другий & останній", text);
    }

    [Theory]
    [InlineData("http://127.0.0.1:11434/api/tags")]
    [InlineData("http://192.168.1.1/")]
    [InlineData("http://10.0.0.5/")]
    [InlineData("file:///C:/Windows/win.ini")]
    public async Task Private_and_non_http_addresses_are_refused(string url) =>
        Assert.NotNull(await WebHttp.CheckPublicUrlAsync(new Uri(url), default));
}

// The tool loop with a scripted model instead of Ollama
public class AgentRunnerTests : IDisposable
{
    private readonly TempDir _dir = new();
    public void Dispose() => _dir.Dispose();

    private sealed class ScriptedEngine(params LlmChatChunk[][] replies) : ILLMEngine
    {
        private int _turn;
        public List<int> ToolsOffered { get; } = [];
        public bool RejectTools { get; init; }

        public async IAsyncEnumerable<LlmChatChunk> ChatStreamAsync(LlmChatRequest request, [EnumeratorCancellation] CancellationToken ct = default)
        {
            if (RejectTools && request.Tools is { Count: > 0 }) throw new ToolsNotSupportedException(request.Model);
            ToolsOffered.Add(request.Tools?.Count ?? 0);
            foreach (var c in replies[Math.Min(_turn, replies.Length - 1)]) { await Task.Yield(); yield return c; }
            _turn++;
        }

        public Task LoadModelAsync(string modelPath, CancellationToken ct = default) => Task.CompletedTask;
        public Task<LlmResponse> GenerateAsync(LlmRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UnloadAsync(CancellationToken ct = default) => Task.CompletedTask;
        public IAsyncEnumerable<string> GenerateStreamAsync(LlmRequest request, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private static LlmChatChunk Text(string s, bool done = false) => new() { Content = s, Done = done };

    private AgentRunner Runner(ILLMEngine engine) =>
        new(engine, new ToolRegistry([new FileSystemTool()]), NullLogger<AgentRunner>.Instance);

    [Fact]
    public async Task Structured_tool_call_runs_and_the_model_then_answers()
    {
        var call = new LlmToolCall
        {
            Name = "file_system",
            Arguments = JsonSerializer.SerializeToElement(new { action = "write", path = "hi.txt", content = "привіт" })
        };
        var engine = new ScriptedEngine(
            [new LlmChatChunk { ToolCalls = [call], Done = true }],
            [Text("Файл створено."), Text("", done: true)]);

        var messages = new List<LlmChatMessage> { LlmChatMessage.User("створи файл") };
        var result = await Runner(engine).RunAsync("m", messages, Ctx.For(_dir.Path, new FakeApprovals(true)), null, default);

        Assert.Equal("Файл створено.", result.Content);
        Assert.Equal(1, result.ToolCalls);
        Assert.Equal("привіт", File.ReadAllText(_dir.File("hi.txt")));
        Assert.Contains(messages, m => m.Role == "tool");
    }

    [Fact]
    public async Task Tool_call_written_as_text_is_recovered_and_not_shown()
    {
        var engine = new ScriptedEngine(
            [Text("Створюю файл.\n"), Text("{\"name\": \"file_system\", \"arguments\": {\"action\": \"write\", \"path\": \"t.txt\", \"content\": \"x\"}}", done: true)],
            [Text("Готово.", done: true)]);

        var shown = new System.Text.StringBuilder();
        var result = await Runner(engine).RunAsync("m", [LlmChatMessage.User("зроби")], Ctx.For(_dir.Path, new FakeApprovals(true)),
            e => { if (e.Kind == AgentEventKind.Token) shown.Append(e.Text); return Task.CompletedTask; }, default);

        Assert.True(File.Exists(_dir.File("t.txt")));
        Assert.DoesNotContain("\"name\"", shown.ToString());
        Assert.Equal("Готово.", result.Content);
    }

    [Fact]
    public async Task Model_without_tool_support_still_answers()
    {
        var engine = new ScriptedEngine([Text("Відповідь без інструментів.", done: true)]) { RejectTools = true };
        var result = await Runner(engine).RunAsync("m", [LlmChatMessage.User("питання")], Ctx.For(_dir.Path, new FakeApprovals(true)), null, default);
        Assert.Equal("Відповідь без інструментів.", result.Content);
    }

    [Fact]
    public async Task Upfront_call_runs_before_the_model_is_asked()
    {
        File.WriteAllText(_dir.File("a.txt"), "");
        var engine = new ScriptedEngine([Text("Бачу a.txt.", done: true)]);
        var messages = new List<LlmChatMessage> { LlmChatMessage.User("що в теці?") };
        var listCall = new LlmToolCall { Name = "file_system", Arguments = JsonSerializer.SerializeToElement(new { action = "list", path = "." }) };

        await Runner(engine).RunAsync("m", messages, Ctx.For(_dir.Path, new FakeApprovals(true)), null, default, [listCall]);

        Assert.Contains(messages, m => m.Role == "tool" && m.Content.Contains("a.txt"));
    }

    [Fact]
    public void Token_estimate_grows_with_text() =>
        Assert.True(AgentRunner.EstimateTokens([LlmChatMessage.User(new string('я', 20000))], null) >
                    AgentRunner.EstimateTokens([LlmChatMessage.User("привіт")], null) + 9000);
}
