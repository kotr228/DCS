namespace AsmodayCat.Shared.Text;

// Language of messages the service shows to the user (errors, notifications, approval requests).
// Set by the app (IPC SetLanguage) and kept in the service database as "Ui:Language".
public static class L
{
    public const string SettingKey = "Ui:Language";

    private static volatile string _lang = "uk";

    public static string Lang
    {
        get => _lang;
        set => _lang = value is "en" or "ru" ? value : "uk";
    }

    public static string T(string uk, string en, string ru) => _lang switch
    {
        "en" => en,
        "ru" => ru,
        _    => uk
    };
}
