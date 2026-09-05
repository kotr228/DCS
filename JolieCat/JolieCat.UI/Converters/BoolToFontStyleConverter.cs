using System;
using System.Globalization;
using System.Windows.Data;

namespace JolieCat.UI.Converters
{
    /// <summary>Maps <see cref="ViewModels.ToolOptions.TextToolOptionsViewModel.IsItalic"/>
    /// to a WPF <see cref="System.Windows.FontStyle"/> for the text-edit overlay's live
    /// preview font.</summary>
    public sealed class BoolToFontStyleConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            value is true ? System.Windows.FontStyles.Italic : System.Windows.FontStyles.Normal;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
