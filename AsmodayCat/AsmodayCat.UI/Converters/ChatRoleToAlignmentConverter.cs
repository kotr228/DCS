using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace AsmodayCat.UI.Converters;

public class ChatRoleToAlignmentConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value?.ToString() is "User" or "user"
            ? HorizontalAlignment.Right
            : HorizontalAlignment.Left;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}
