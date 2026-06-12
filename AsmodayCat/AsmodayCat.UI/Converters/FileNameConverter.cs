using System.Globalization;
using System.IO;
using System.Windows.Data;

namespace AsmodayCat.UI.Converters;

public class FileNameConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is string path ? Path.GetFileName(path) : value?.ToString() ?? string.Empty;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}
