using System.Text.RegularExpressions;
using AsmodayCat.Shared.Text;
using AsmodayCat.UI.Localization;
using AsmodayCat.UI.Voice;

namespace AsmodayCat.Tests;

public class LocalizationTests
{
    [Fact]
    public void Every_text_has_all_three_languages()
    {
        Assert.NotEmpty(Strings.Table);
        foreach (var (key, values) in Strings.Table)
        {
            Assert.Equal(3, values.Length);
            Assert.All(values, v => Assert.False(string.IsNullOrWhiteSpace(v), $"{key} has an empty translation"));
        }
    }

    [Fact]
    public void Placeholders_match_in_every_language()
    {
        foreach (var (key, values) in Strings.Table)
        {
            var sets = values.Select(v => string.Join(",", Regex.Matches(v, @"\{(\d)").Select(m => m.Groups[1].Value).Distinct().Order())).ToArray();
            Assert.True(sets[0] == sets[1] && sets[1] == sets[2], $"{key}: {string.Join(" | ", sets)}");
            // and the format strings are valid
            foreach (var v in values) _ = string.Format(v, "a", "b", "c");
        }
    }

    [Theory]
    [InlineData("uk", "Чат", "Пам’ять")]
    [InlineData("en", "Chat", "Memory")]
    [InlineData("ru", "Чат", "Память")]
    public void Switching_language_changes_texts_and_labels(string lang, string chat, string memory)
    {
        var before = Loc.Instance.Lang;
        try
        {
            Loc.Instance.Set(lang);
            Assert.Equal(lang, Loc.Instance.Lang);
            Assert.Equal(chat, Loc.T("nav.chat"));
            Assert.Equal(memory, Loc.T("nav.memory"));
            Assert.Equal(Loc.T("label.Ready"), Loc.Label("Ready"));
            Assert.Equal("SomethingUnknown", Loc.Label("SomethingUnknown"));
        }
        finally { Loc.Instance.Set(before); }
    }

    [Fact]
    public void Unknown_or_missing_language_falls_back()
    {
        Assert.Equal("uk", Loc.Normalize("uk"));
        Assert.Equal("ru", Loc.Normalize("ru"));
        Assert.Contains(Loc.Normalize("de"), Loc.Languages);
        Assert.Contains(Loc.Normalize(null), Loc.Languages);
    }

    [Fact]
    public void Service_messages_follow_the_language()
    {
        var before = L.Lang;
        try
        {
            L.Lang = "en"; Assert.Equal("b", L.T("a", "b", "c"));
            L.Lang = "ru"; Assert.Equal("c", L.T("a", "b", "c"));
            L.Lang = "xx"; Assert.Equal("a", L.T("a", "b", "c"));   // unknown → Ukrainian
        }
        finally { L.Lang = before; }
    }

    [Theory]
    [InlineData("Да, разрешаю", true)]
    [InlineData("подтверждаю", true)]
    [InlineData("Нет, отмена", false)]
    [InlineData("не разрешаю", false)]
    [InlineData("yes, approve", true)]
    public void Russian_and_English_voice_answers_are_understood(string heard, bool expected)
    {
        Assert.Equal(expected, VoiceIntent.Parse(heard));
    }
}
