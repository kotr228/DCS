using System;
using System.Globalization;
using System.Windows.Data;

namespace JolieCat.UI.Converters
{
    /// <summary>
    /// Multiplies a frame-space value (a clip's start/length, a keyframe's frame, the
    /// playhead's current frame) by pixels-per-frame to get the on-screen pixel value
    /// used for the timeline's Canvas.Left/Width bindings.
    /// </summary>
    public sealed class FramesToPixelsConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
        {
            if (values.Length < 2 || values[0] is not double frames || values[1] is not double pixelsPerFrame)
                return 0.0;

            return frames * pixelsPerFrame;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException("Timeline pixel values are display-only.");
    }
}
