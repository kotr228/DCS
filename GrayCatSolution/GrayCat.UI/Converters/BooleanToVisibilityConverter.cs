namespace GrayCat.UI.Converters;

using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

[ValueConversion(typeof(bool), typeof(Visibility))]
public class BooleanToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not bool boolValue)
            return Visibility.Collapsed;

        // Check for parameter override
        if (parameter?.ToString()?.ToLower() == "invert")
        {
            boolValue = !boolValue;
        }
        else if (Invert)
        {
            boolValue = !boolValue;
        }

        return boolValue ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is Visibility visibility)
        {
            return visibility == Visibility.Visible;
        }
        return false;
    }
}