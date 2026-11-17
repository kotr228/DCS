using System;
using System.Collections;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Data;

namespace JolieCat.UI.Converters
{
    /// <summary>
    /// For one group's list of tools: the active tool if it belongs to this group, else
    /// nothing - so only the group that holds the active tool shows it selected. Picking a
    /// tool passes it back; a group losing its selection passes nothing back.
    /// Values: [0] the active tool, [1] the group's tools.
    /// </summary>
    public sealed class ActiveInGroupConverter : IMultiValueConverter
    {
        public object? Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length < 2 || values[0] is null || values[1] is not IEnumerable items) return null;
            return items.Cast<object>().Contains(values[0]) ? values[0] : null;
        }

        public object[] ConvertBack(object? value, Type[] targetTypes, object parameter, CultureInfo culture) =>
            value is null
                ? new object[] { Binding.DoNothing, Binding.DoNothing }
                : new object[] { value, Binding.DoNothing };
    }
}
