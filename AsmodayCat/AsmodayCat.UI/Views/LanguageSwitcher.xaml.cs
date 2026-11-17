using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using AsmodayCat.UI.Localization;
using AsmodayCat.UI.Services;

namespace AsmodayCat.UI.Views;

public partial class LanguageSwitcher : UserControl
{
    public sealed record Option(string Code, string Short, string Name);

    private static readonly Option[] Options =
    [
        new("uk", "UA", "Українська"),
        new("en", "EN", "English"),
        new("ru", "RU", "Русский")
    ];

    private bool _syncing;

    public LanguageSwitcher()
    {
        InitializeComponent();
        Combo.ItemsSource = Options;
        Sync();
        // Another switcher (startup window / main window) may change the language
        Loc.LanguageChanged += Sync;
        Unloaded += (_, _) => Loc.LanguageChanged -= Sync;
        Loaded += (_, _) => { Loc.LanguageChanged -= Sync; Loc.LanguageChanged += Sync; Sync(); };
    }

    private void Sync()
    {
        _syncing = true;
        Combo.SelectedItem = Options.First(o => o.Code == Loc.Instance.Lang);
        _syncing = false;
    }

    private void Combo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || Combo.SelectedItem is not Option o || o.Code == Loc.Instance.Lang) return;
        App.Services.GetRequiredService<LanguageService>().Apply(o.Code);
    }
}
