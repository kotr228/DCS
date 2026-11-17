using AsmodayCat.Service.Approvals;
using AsmodayCat.Shared.Models;
using AsmodayCat.UI.Converters;
using AsmodayCat.UI.Services;
using AsmodayCat.UI.Voice;
using Microsoft.Extensions.Logging.Abstractions;

namespace AsmodayCat.Tests;

public class ApprovalBrokerTests
{
    [Fact]
    public async Task Without_an_open_UI_requests_are_denied_at_once()
    {
        var broker = new ApprovalBroker(NullLogger<ApprovalBroker>.Instance);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Assert.False(await broker.RequestAsync("Test", "run", "cmd"));
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task With_an_open_UI_the_users_answer_is_returned()
    {
        var broker = new ApprovalBroker(NullLogger<ApprovalBroker>.Instance);
        broker.GetPending();                                 // the UI polled: it is open

        var request = broker.RequestAsync("Test", "run", "dir");
        await Task.Delay(100);
        var pending = Assert.Single(broker.GetPending());
        Assert.True(broker.Resolve(pending.Id, approved: true));

        Assert.True(await request);
        Assert.Empty(broker.GetPending());
    }

    [Fact]
    public async Task DenyAll_answers_every_waiting_request_with_no()
    {
        var broker = new ApprovalBroker(NullLogger<ApprovalBroker>.Instance);
        broker.GetPending();
        var a = broker.RequestAsync("Test", "a", "1");
        var b = broker.RequestAsync("Test", "b", "2");
        await Task.Delay(100);

        broker.DenyAll();
        Assert.False(await a);
        Assert.False(await b);
    }
}

public class UiTextTests
{
    [Fact]
    public void Markdown_is_turned_into_speakable_text()
    {
        var text = SpeechOutput.ToSpeech("## Відповідь\nОсь **три** пункти:\n- Перший\n- [Посилання](https://x.com)\n```cs\nvar x = 1;\n```\nГотово! https://example.com");
        Assert.DoesNotContain("**", text);
        Assert.DoesNotContain("##", text);
        Assert.DoesNotContain("var x", text);
        Assert.DoesNotContain("https://", text);
        Assert.Contains("Посилання", text);
        Assert.Contains("Готово!", text);
    }

    [Fact]
    public void Very_long_answers_are_cut_at_a_sentence()
    {
        var text = SpeechOutput.ToSpeech(string.Join(" ", Enumerable.Repeat("Це речення.", 400)));
        Assert.True(text.Length < 1400);
        Assert.EndsWith("Далі — на екрані.", text);
    }

    [Theory]
    [InlineData("Так.", true)]
    [InlineData("Так, дозволяю", true)]
    [InlineData("Підтверджую!", true)]
    [InlineData("Ні", false)]
    [InlineData("Не дозволяю", false)]          // "дозволяю" inside, but it's a no
    [InlineData("Так, але ні", false)]          // mixed: deny wins
    [InlineData("Скасуй це", false)]
    [InlineData("Дякую за перегляд!", null)]    // typical Whisper noise
    [InlineData("Таксі", null)]                 // "так" inside another word
    [InlineData("", null)]
    public void Spoken_yes_no_is_understood(string heard, bool? expected) =>
        Assert.Equal(expected, VoiceIntent.Parse(heard));

    [Theory]
    [InlineData("NotInstalled", "Не встановлена")]
    [InlineData("CreateReport", "Звіт")]
    [InlineData("Auto", "Авто")]
    [InlineData("something-else", "something-else")]
    public void Internal_names_are_shown_in_ukrainian(string value, string expected) =>
        Assert.Equal(expected, UkLabelConverter.Translate(value));
}

public class IpcPipeTests
{
    [Fact]
    public void Pipe_name_is_per_user_stable_and_ascii()
    {
        var name = AsmodayCat.Shared.Ipc.IpcPipe.Name;
        Assert.Matches("^AsmodayCat\\.Service\\.[0-9A-F]{16}$", name);
        Assert.Equal(name, AsmodayCat.Shared.Ipc.IpcPipe.Name);
    }
}

public class ChatExportTests
{
    [Fact]
    public void Conversation_becomes_markdown_with_roles_attachments_and_text()
    {
        var time = new DateTime(2026, 11, 8, 19, 5, 0);
        var md = ChatExport.ToMarkdown("Кольори", [
            new ChatMessageDto { Role = ChatRole.User, Content = "Який колір?", Timestamp = time, AttachmentPaths = [@"C:\tmp\image.png"] },
            new ChatMessageDto { Role = ChatRole.Assistant, Content = "**Жовтий**\n", Timestamp = time }
        ]);

        Assert.StartsWith("# Кольори", md);
        Assert.Contains("### Ви · 08.11.2026 19:05", md);
        Assert.Contains("📎 image.png", md);
        Assert.Contains("### AsmodayCat", md);
        Assert.Contains("**Жовтий**", md);
    }

    [Fact]
    public void File_name_drops_forbidden_characters()
    {
        var name = ChatExport.SuggestFileName("Що таке: \"AI\"? / тест", new DateTime(2026, 11, 8));
        Assert.Equal("Що таке AI  тест 2026-11-08.md", name);
        Assert.DoesNotContain(name, c => Path.GetInvalidFileNameChars().Contains(c));
    }
}
