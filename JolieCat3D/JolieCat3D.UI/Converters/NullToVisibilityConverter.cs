using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace JolieCat3D.UI.Converters
{
    /// <summary>
    /// Null -&gt; <see cref="Visibility.Collapsed"/>, non-null -&gt; <see cref="Visibility.Visible"/> -
    /// what the Properties Inspector's "no object selected" placeholder and its actual
    /// field grid both bind against (the same <c>SelectedNode</c> value, in opposite
    /// senses via <see cref="ConverterParameter"/>="Invert"). WPF ships a built-in
    /// <see cref="System.Windows.Controls.BooleanToVisibilityConverter"/> for bool but
    /// nothing for a plain null check, hence this one.
    /// </summary>
    public sealed class NullToVisibilityConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            var isNull = value is null;
            if (string.Equals(parameter as string, "Invert", StringComparison.OrdinalIgnoreCase)) isNull = !isNull;

            return isNull ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
