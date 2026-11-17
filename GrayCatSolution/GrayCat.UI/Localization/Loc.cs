namespace GrayCat.UI.Localization;

using System.ComponentModel;
using System.Globalization;

/// <summary>
/// Interface language (Ukrainian or English), switchable at runtime.
/// Keys are the English texts, so anything without a translation simply shows in English.
/// XAML binds to the indexer ({l:Tr 'Text'}), so every text updates as soon as the language changes.
/// </summary>
public sealed class Loc : INotifyPropertyChanged
{
    public static readonly string[] Languages = { "uk", "en" };

    public static Loc Instance { get; } = new();

    private Loc() { }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised after a switch, for text built in code (properties panel, status line).</summary>
    public static event Action? LanguageChanged;

    public string Lang { get; private set; } = "en";

    public CultureInfo Culture { get; private set; } = CultureInfo.GetCultureInfo("en-US");

    public string this[string key] => Get(key);

    /// <summary>Translates a fixed text.</summary>
    public static string T(string key) => Instance.Get(key);

    /// <summary>Translates a format text ("Saved at {0}") and fills it in.</summary>
    public static string F(string key, params object?[] args) =>
        string.Format(Instance.Culture, Instance.Get(key), args);

    /// <summary>Display name of a block type ("Hero" → "Банер"); the type itself in English.</summary>
    public static string Block(string? type) => Instance.Lookup("block:", type);

    /// <summary>Display text of an option value ("center" → "по центру"); the value itself in English.</summary>
    public static string Value(string? value) => Instance.Lookup("value:", value);

    private string Lookup(string prefix, string? key)
    {
        if (string.IsNullOrEmpty(key)) return string.Empty;
        return Lang != "en" && Strings.Uk.TryGetValue(prefix + key, out var text) ? text : key;
    }

    private string Get(string key) =>
        Lang != "en" && Strings.Uk.TryGetValue(key, out var text) ? text : key;

    /// <summary>First run: Ukrainian or Russian Windows → Ukrainian, otherwise English.</summary>
    public static string DefaultForSystem() => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName switch
    {
        "uk" or "ru" => "uk",
        _ => "en"
    };

    public static string Normalize(string? lang) =>
        lang != null && Languages.Contains(lang) ? lang : DefaultForSystem();

    public void Set(string lang)
    {
        lang = Normalize(lang);
        Culture = CultureInfo.GetCultureInfo(lang == "uk" ? "uk-UA" : "en-US");
        CultureInfo.DefaultThreadCurrentUICulture = Culture;
        CultureInfo.CurrentUICulture = Culture;

        var changed = lang != Lang;
        Lang = lang;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Lang)));
        if (changed) LanguageChanged?.Invoke();
    }
}
