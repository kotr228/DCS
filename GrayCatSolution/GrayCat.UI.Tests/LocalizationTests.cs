namespace GrayCat.UI.Tests;

using System.Text.RegularExpressions;
using GrayCat.UI.Localization;

public class LocalizationTests
{
    private static readonly Regex Placeholder = new(@"(?<!\{)\{(\d+)(?::[^}]*)?\}(?!\})");

    [Fact]
    public void Translations_KeepTheSamePlaceholders()
    {
        // A translation that drops or adds {0}/{1} would make string.Format throw at runtime.
        var broken = Strings.Uk
            .Where(kv => !kv.Key.StartsWith("block:") && !kv.Key.StartsWith("value:"))
            .Where(kv => Indices(kv.Key) != Indices(kv.Value))
            .Select(kv => $"{kv.Key} → {kv.Value}")
            .ToList();
        Assert.Empty(broken);
    }

    [Fact]
    public void EveryTranslation_FormatsWithoutErrors()
    {
        var args = Enumerable.Range(0, 5).Select(i => (object)("x" + i)).ToArray();
        foreach (var (key, value) in Strings.Uk)
        {
            if (key.Contains('{'))
                _ = string.Format(value, args); // throws on malformed templates
        }
    }

    [Fact]
    public void XamlTexts_WithCommas_AreShownWhole()
    {
        // {l:Tr '…'} once used an indexer binding path, which WPF splits at commas: such texts came out empty.
        const string key = "Click to add, drag to place";
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                Loc.Instance.Set("uk");
                var text = new System.Windows.Controls.TextBlock();
                var binding = (System.Windows.Data.BindingBase)new TrExtension(key).ProvideValue(null!);
                System.Windows.Data.BindingOperations.SetBinding(text, System.Windows.Controls.TextBlock.TextProperty, binding);
                Assert.Equal(Strings.Uk[key], text.Text);

                Loc.Instance.Set("en"); // follows language changes
                Assert.Equal(key, text.Text);

                // Other tests switch the language from their own threads; this thread's control must not hear it.
                System.Windows.Data.BindingOperations.ClearBinding(text, System.Windows.Controls.TextBlock.TextProperty);
            }
            catch (Exception ex) { failure = ex; }
            finally
            {
                Loc.Instance.Set("en");
                System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure != null) throw new Exception("Binding failed", failure);
    }

    [Fact]
    public void Switching_ChangesTextsAndFallsBackToEnglish()
    {
        try
        {
            Loc.Instance.Set("uk");
            Assert.Equal("Готово", Loc.T("Ready"));
            Assert.Equal("Блоків: 3", Loc.F("Blocks: {0}", 3));
            Assert.Equal("Банер", Loc.Block("Hero"));
            Assert.Equal("по центру", Loc.Value("center"));
            Assert.Equal("Untranslated text", Loc.T("Untranslated text"));
            Assert.Equal("Georgia", Loc.Value("Georgia")); // data values without a translation stay as they are

            Loc.Instance.Set("en");
            Assert.Equal("Ready", Loc.T("Ready"));
            Assert.Equal("Hero", Loc.Block("Hero"));
        }
        finally
        {
            Loc.Instance.Set("en");
        }
    }

    private static string Indices(string text) =>
        string.Join(",", Placeholder.Matches(text).Select(m => m.Groups[1].Value).Distinct().OrderBy(x => x));
}
