using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace JolieCat3D.UI.Converters
{
    /// <summary>A plain <see cref="Color"/> -&gt; <see cref="SolidColorBrush"/> - what the
    /// Properties Inspector's material-color swatch <see cref="System.Windows.Controls.Border.Background"/>
    /// needs, since <see cref="NodeViewModel.DiffuseColor"/> is a Color (matching how
    /// every other WPF color-related API in this app already speaks Color), not a Brush.</summary>
    public sealed class ColorToBrushConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            value is Color color ? new SolidColorBrush(color) : Brushes.Transparent;

        public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
