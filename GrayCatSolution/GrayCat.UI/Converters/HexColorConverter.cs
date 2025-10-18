namespace GrayCat.UI.Converters;

using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

[ValueConversion(typeof(string), typeof(Brush))]
public class HexColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string hexColor && !string.IsNullOrWhiteSpace(hexColor))
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(hexColor);
                return new SolidColorBrush(color);
            }
            catch
            {
                return new SolidColorBrush(Colors.White);
            }
        }

        return new SolidColorBrush(Colors.White);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is SolidColorBrush brush)
        {
            var color = brush.Color;
            return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        }

        return "#FFFFFF";
    }
}