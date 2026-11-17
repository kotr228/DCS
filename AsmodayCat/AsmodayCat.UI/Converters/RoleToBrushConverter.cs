using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace AsmodayCat.UI.Converters;

public class RoleToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value?.ToString()?.ToLowerInvariant() switch
        {
            "user"      => new SolidColorBrush(Color.FromRgb(0xF3, 0xE9, 0xCC)),  // soft amber
            "assistant" => new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF)),  // white card
            "tool"      => new SolidColorBrush(Color.FromRgb(0xF2, 0xEB, 0xD8)),  // light cream
            "system"    => new SolidColorBrush(Color.FromRgb(0xF2, 0xEB, 0xD8)),  // light cream
            _           => new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF))
        };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}
