using System;
using System.Globalization;
using System.Windows.Data;

namespace JolieCat.UI.Converters
{
    /// <summary>Maps <see cref="ViewModels.ToolOptions.TextToolOptionsViewModel.IsBold"/>
    /// to a WPF <see cref="System.Windows.FontWeight"/> for the text-edit overlay's live
    /// preview font.</summary>
    public sealed class BoolToFontWeightConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            value is true ? System.Windows.FontWeights.Bold : System.Windows.FontWeights.Normal;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
