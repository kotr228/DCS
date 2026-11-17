namespace GrayCat.UI.Converters;

using System.Globalization;
using System.IO;
using System.Windows.Data;
using GrayCat.Core.Generation;

/// <summary>"C:\Sites\Shop.graycat" → "Shop"; old "C:\Sites\Shop\graycat.project.json" → "Shop".</summary>
public class ProjectNameConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string path || path.Length == 0)
            return string.Empty;
        return Path.GetFileName(path).Equals(ProjectScaffolder.ProjectFileName, StringComparison.OrdinalIgnoreCase)
            ? Path.GetFileName(Path.GetDirectoryName(path)) ?? path
            : Path.GetFileNameWithoutExtension(path);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
