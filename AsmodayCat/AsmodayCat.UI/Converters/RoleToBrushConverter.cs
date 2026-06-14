using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace AsmodayCat.UI.Converters;

public class RoleToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value?.ToString()?.ToLowerInvariant() switch
        {
            "user"      => new SolidColorBrush(Color.FromRgb(0xBC, 0x8F, 0x3C)),  // #BC8F3C gold
            "assistant" => new SolidColorBrush(Color.FromRgb(0xE4, 0xD3, 0xBD)),  // #E4D3BD beige
            "tool"      => new SolidColorBrush(Color.FromRgb(0x79, 0x64, 0x4D)),  // #79644D nav brown
            "system"    => new SolidColorBrush(Color.FromRgb(0xED, 0xCA, 0x80)),  // #EDCA80 background
            _           => new SolidColorBrush(Color.FromRgb(0xE4, 0xD3, 0xBD))
        };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}
