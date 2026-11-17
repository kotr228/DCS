using System.ComponentModel;
using System.Globalization;

namespace AsmodayCat.UI.Localization;

// Interface language: Ukrainian, English or Russian, switchable at runtime.
// XAML binds to the indexer ({l:Tr key}), so every text updates as soon as Lang changes.
public sealed class Loc : INotifyPropertyChanged
{
    public static readonly string[] Languages = ["uk", "en", "ru"];

    public static Loc Instance { get; } = new();

    private Loc() { }

    public event PropertyChangedEventHandler? PropertyChanged;

    // Raised after a switch, for text built in code (tray menu, status lines, voice)
    public static event Action? LanguageChanged;

    public string Lang { get; private set; } = "uk";

    public CultureInfo Culture { get; private set; } = CultureInfo.GetCultureInfo("uk-UA");

    private int Index => Array.IndexOf(Languages, Lang);

    public string this[string key] => Get(key);

    public static string T(string key) => Instance.Get(key);

    public static string F(string key, params object?[] args) =>
        string.Format(Instance.Culture, Instance.Get(key), args);

    // Enum values, statuses and model tasks shown as words; unknown values pass through
    public static string Label(object? value)
    {
        var s = value?.ToString();
        if (string.IsNullOrEmpty(s)) return string.Empty;
        return Strings.Table.ContainsKey("label." + s) ? T("label." + s) : s;
    }

    private string Get(string key) =>
        Strings.Table.TryGetValue(key, out var v) ? v[Index] : key;

    public static string Normalize(string? lang) =>
        lang is not null && Languages.Contains(lang) ? lang : DefaultForSystem();

    // First run: follow Windows (Ukrainian or Russian Windows → that language, otherwise English)
    public static string DefaultForSystem() => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName switch
    {
        "uk" => "uk",
        "ru" => "ru",
        _    => "en"
    };

    public void Set(string lang)
    {
        lang = Normalize(lang);
        Culture = CultureInfo.GetCultureInfo(lang switch { "en" => "en-US", "ru" => "ru-RU", _ => "uk-UA" });
        CultureInfo.DefaultThreadCurrentCulture = Culture;
        CultureInfo.DefaultThreadCurrentUICulture = Culture;
        CultureInfo.CurrentCulture = Culture;
        CultureInfo.CurrentUICulture = Culture;

        var changed = lang != Lang;
        Lang = lang;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Lang)));
        if (changed) LanguageChanged?.Invoke();
    }
}
