using AsmodayCat.Agent.Memory;
using AsmodayCat.Core.Data;
using AsmodayCat.Core.Data.Repositories;
using AsmodayCat.Shared.Models;

namespace AsmodayCat.Tests;

// SQLite-backed memory and chat history, on a temporary database
public class StorageTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly AsmodayDatabase _db;

    public StorageTests()
    {
        _db = new AsmodayDatabase(_dir.File("test.db"));
        new DatabaseMigrator(_db).Initialize();
    }

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void Memory_search_matches_other_word_forms()
    {
        var repo = new MemoryRepository(_db);
        repo.Add(MemoryKind.Action, @"Wrote file C:\Users\me\Desktop\нотатки.txt", "Chat");
        repo.Add(MemoryKind.Fact, "Мене звати Костя", "Chat");

        // "нотатках" / "звуть" share the 5-letter prefix with the stored words
        Assert.Contains(repo.Search("що в нотатках?", 5), e => e.Text.Contains("нотатки.txt"));
        Assert.Contains(repo.Search("як мене звати", 5), e => e.Text.Contains("Костя"));
    }

    [Fact]
    public void Same_fact_is_not_stored_twice_but_actions_are()
    {
        var repo = new MemoryRepository(_db);
        var a = repo.Add(MemoryKind.Fact, "Люблю короткі відповіді");
        var b = repo.Add(MemoryKind.Fact, "Люблю короткі відповіді");
        Assert.Equal(a, b);

        repo.Add(MemoryKind.Action, "Ran command `git status`");
        repo.Add(MemoryKind.Action, "Ran command `git status`");
        Assert.Equal(3, repo.Count());
    }

    [Fact]
    public void Delete_and_clear_remove_entries()
    {
        var repo = new MemoryRepository(_db);
        var id = repo.Add(MemoryKind.Fact, "тимчасовий факт");
        Assert.True(repo.Delete(id));
        Assert.Equal(0, repo.Count());

        repo.Add(MemoryKind.Fact, "a1"); repo.Add(MemoryKind.Fact, "b2");
        repo.Clear();
        Assert.Equal(0, repo.Count());
    }

    [Fact]
    public void Match_query_skips_stop_words_and_uses_prefixes()
    {
        Assert.Null(MemoryRepository.BuildMatchQuery("що як це"));
        Assert.Equal("\"робоч\"* OR \"стол\"*", MemoryRepository.BuildMatchQuery("на робочому стол"));
    }

    [Fact]
    public void Refusals_are_not_remembered_and_context_lists_recent_actions()
    {
        var memory = new AssistantMemory(new MemoryRepository(_db));
        memory.RecordExchange("глянь робочий стіл", "Вибачте, але я не можу відкрити файли.");
        memory.RecordExchange("привіт", "Привіт! Чим допомогти?");
        memory.RecordAction("Chat", @"Wrote file D:\x\report.md (10 chars)");

        var ctx = memory.BuildContext("звіт");
        Assert.Contains("report.md", ctx);
        Assert.DoesNotContain("Вибачте", ctx);
        Assert.Equal(2, new MemoryRepository(_db).Count());   // refusal skipped
    }

    [Fact]
    public void Chat_sessions_keep_messages_and_can_be_deleted()
    {
        var chats = new ChatRepository(_db);
        var id = chats.CreateSession("Перша розмова");
        chats.AddMessage(id, new ChatMessageDto { Role = ChatRole.User, Content = "Привіт", AttachmentPaths = [@"C:\a.txt"] });
        chats.AddMessage(id, new ChatMessageDto { Role = ChatRole.Assistant, Content = "Вітаю!" });

        var messages = chats.GetMessagesBySession(id);
        Assert.Equal(["Привіт", "Вітаю!"], messages.Select(m => m.Content));
        Assert.Equal(@"C:\a.txt", messages[0].AttachmentPaths.Single());
        Assert.Contains(chats.GetSessions(), s => s.Id == id && s.Title == "Перша розмова");

        chats.DeleteSession(id);
        Assert.Empty(chats.GetMessagesBySession(id));
        Assert.DoesNotContain(chats.GetSessions(), s => s.Id == id);
    }

    [Fact]
    public void Truncate_removes_from_a_message_on_and_search_finds_any_word_case()
    {
        var chats = new ChatRepository(_db);
        var id = chats.CreateSession("Рецепти");
        foreach (var (role, text) in new[] { (ChatRole.User, "Як спекти Борщ?"), (ChatRole.Assistant, "Ось рецепт…"),
                                              (ChatRole.User, "А вареники?"),     (ChatRole.Assistant, "Тісто…") })
        {
            chats.AddMessage(id, new ChatMessageDto { Role = role, Content = text });
            Thread.Sleep(5);   // distinct timestamps
        }
        var other = chats.CreateSession("Інше");
        chats.AddMessage(other, new ChatMessageDto { Role = ChatRole.User, Content = "погода" });

        Assert.Equal(2, chats.TruncateSession(id, 2));                                  // drop the second exchange
        Assert.Equal(["Як спекти Борщ?", "Ось рецепт…"], chats.GetMessagesBySession(id).Select(m => m.Content));

        Assert.Equal([id], chats.SearchSessions("борщ").Select(s => s.Id));           // other case, Cyrillic
        Assert.Empty(chats.SearchSessions("вареники"));                                 // was truncated
        Assert.Equal([other], chats.SearchSessions("ІНШЕ").Select(s => s.Id));        // by title
    }
}
