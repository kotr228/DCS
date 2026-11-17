using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace AsmodayCat.Core.Data.Repositories;

public static class MemoryKind
{
    public const string Fact         = "fact";          // something the user asked to remember
    public const string Action       = "action";        // a file written, a command run…
    public const string Conversation = "conversation";  // short digest of one chat exchange
}

public sealed record MemoryEntry(long Id, string Kind, string Text, string Source, DateTime CreatedAt);

// Long-term memory of the assistant in an SQLite FTS5 table.
// Search is keyword based (prefix-matched, bm25-ranked): no embedding model or LLM call needed.
public class MemoryRepository
{
    private const int MaxActions       = 1000;
    private const int MaxConversations = 500;

    private static readonly Regex Word = new(@"[\p{L}\p{N}][\p{L}\p{N}_'’-]*", RegexOptions.Compiled);

    // Words that say nothing about the topic
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the","and","for","with","that","this","what","from","have","you","are","was","can","please",
        "що","як","який","яка","яке","які","мені","тобі","мене","тебе","для","про","або","але","чи","це",
        "цей","ця","ці","там","тут","так","ще","вже","було","буде","можеш","зроби","будь","ласка","раніше",
        "что","как","это","мне","для","или","уже","еще","есть"
    };

    private readonly AsmodayDatabase _db;
    private int _insertsSincePrune;

    public MemoryRepository(AsmodayDatabase db) => _db = db;

    public long Add(string kind, string text, string source = "")
    {
        text = text.Trim();
        if (text.Length == 0) return 0;

        using var conn = Open();

        // Skip exact repeats of the latest entries (e.g. the same file written twice)
        using (var dup = conn.CreateCommand())
        {
            dup.CommandText = "SELECT rowid FROM Memories WHERE Kind = @k AND Text = @t ORDER BY rowid DESC LIMIT 1;";
            dup.Parameters.AddWithValue("@k", kind);
            dup.Parameters.AddWithValue("@t", text);
            if (dup.ExecuteScalar() is long existing && kind != MemoryKind.Action) return existing;
        }

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO Memories (Text, Kind, Source, CreatedAt) VALUES (@t, @k, @s, @c); SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("@t", text);
        cmd.Parameters.AddWithValue("@k", kind);
        cmd.Parameters.AddWithValue("@s", source);
        cmd.Parameters.AddWithValue("@c", DateTime.Now.ToString("O"));
        var id = (long)cmd.ExecuteScalar()!;

        if (Interlocked.Increment(ref _insertsSincePrune) >= 50)
        {
            _insertsSincePrune = 0;
            Prune(conn);
        }
        return id;
    }

    // Most relevant entries for free text, best first.
    public List<MemoryEntry> Search(string text, int limit, string? kind = null)
    {
        var query = BuildMatchQuery(text);
        if (query is null) return [];

        using var conn = Open();
        using var cmd  = conn.CreateCommand();
        cmd.CommandText = $@"
            SELECT rowid, Kind, Text, Source, CreatedAt FROM Memories
            WHERE Memories MATCH @q {(kind is null ? "" : "AND Kind = @k")}
            ORDER BY bm25(Memories), rowid DESC
            LIMIT @n;";
        cmd.Parameters.AddWithValue("@q", query);
        cmd.Parameters.AddWithValue("@n", limit);
        if (kind is not null) cmd.Parameters.AddWithValue("@k", kind);
        return Read(cmd);
    }

    public List<MemoryEntry> Recent(int limit, string? kind = null)
    {
        using var conn = Open();
        using var cmd  = conn.CreateCommand();
        cmd.CommandText = $@"
            SELECT rowid, Kind, Text, Source, CreatedAt FROM Memories
            {(kind is null ? "" : "WHERE Kind = @k")}
            ORDER BY rowid DESC LIMIT @n;";
        cmd.Parameters.AddWithValue("@n", limit);
        if (kind is not null) cmd.Parameters.AddWithValue("@k", kind);
        return Read(cmd);
    }

    public bool Delete(long id)
    {
        using var conn = Open();
        using var cmd  = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM Memories WHERE rowid = @id;";
        cmd.Parameters.AddWithValue("@id", id);
        return cmd.ExecuteNonQuery() > 0;
    }

    public int Clear()
    {
        using var conn = Open();
        using var cmd  = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM Memories;";
        return cmd.ExecuteNonQuery();
    }

    public int Count()
    {
        using var conn = Open();
        using var cmd  = conn.CreateCommand();
        cmd.CommandText = "SELECT count(*) FROM Memories;";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    // "текстовий файл на робочому столі" → "текст"* OR "файл"* OR "робоч"* OR "столі"*
    // A 5-letter prefix is a crude stem that covers most word endings in Ukrainian and English.
    public static string? BuildMatchQuery(string text)
    {
        var terms = Word.Matches(text.ToLowerInvariant())
            .Select(m => m.Value.Trim('\'', '’', '-', '_'))
            .Where(w => w.Length >= 3 && !StopWords.Contains(w))
            .Select(w => w.Length > 5 ? w[..5] : w)
            .Distinct()
            .Take(12)
            .Select(w => $"\"{w.Replace("\"", "")}\"*")
            .ToList();

        return terms.Count == 0 ? null : string.Join(" OR ", terms);
    }

    private void Prune(SqliteConnection conn)
    {
        foreach (var (kind, keep) in new[] { (MemoryKind.Action, MaxActions), (MemoryKind.Conversation, MaxConversations) })
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                DELETE FROM Memories WHERE Kind = @k AND rowid NOT IN
                    (SELECT rowid FROM Memories WHERE Kind = @k ORDER BY rowid DESC LIMIT @keep);";
            cmd.Parameters.AddWithValue("@k", kind);
            cmd.Parameters.AddWithValue("@keep", keep);
            cmd.ExecuteNonQuery();
        }
    }

    private SqliteConnection Open()
    {
        var conn = _db.CreateConnection();
        conn.Open();
        return conn;
    }

    private static List<MemoryEntry> Read(SqliteCommand cmd)
    {
        var list = new List<MemoryEntry>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            DateTime.TryParse(r.GetString(4), out var created);
            list.Add(new MemoryEntry(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3), created));
        }
        return list;
    }
}
