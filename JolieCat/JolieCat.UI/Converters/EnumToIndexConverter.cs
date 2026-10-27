using System;
using System.Globalization;
using System.Windows.Data;

namespace JolieCat.UI.Converters
{
    /// <summary>Binds an enum property to a ComboBox's SelectedIndex, so the items can
    /// carry friendly text ("Foreground → Background") instead of enum member names.
    /// The items must be listed in the enum's declaration order.</summary>
    public sealed class EnumToIndexConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is Enum e ? System.Convert.ToInt32(e, CultureInfo.InvariantCulture) : -1;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is int index && index >= 0 ? Enum.ToObject(targetType, index) : Binding.DoNothing;
    }
}
