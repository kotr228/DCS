using System.Globalization;
using System.Windows.Data;
using AsmodayCat.UI.Localization;

namespace AsmodayCat.UI.Converters;

// Shows internal names (enum values, statuses, tasks) in the interface language; unknown values pass through.
// In XAML prefer {l:TrLabel Path=...}: it also refreshes when the language changes.
public sealed class UkLabelConverter : IValueConverter
{
    public static string Translate(string? value) => Loc.Label(value);

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Translate(value?.ToString());

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
