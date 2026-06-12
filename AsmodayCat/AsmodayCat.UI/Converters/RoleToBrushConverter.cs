using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace AsmodayCat.UI.Converters;

public class RoleToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value?.ToString() switch
        {
            "user"      => new SolidColorBrush(Color.FromRgb(0x1A, 0x23, 0x3A)),
            "assistant" => new SolidColorBrush(Color.FromRgb(0x1E, 0x1A, 0x2E)),
            "tool"      => new SolidColorBrush(Color.FromRgb(0x1A, 0x2E, 0x1A)),
            _           => new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A))
        };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}
