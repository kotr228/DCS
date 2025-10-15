using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace GrayCat.UI
{
    [ValueConversion(typeof(bool), typeof(Visibility))]
    public class BooleanToVisibilityConverter : IValueConverter
    {
        public bool Invert { get; set; }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool boolValue = (bool)value;
            if (Invert)
            {
                boolValue = !boolValue;
            }

            return boolValue ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}