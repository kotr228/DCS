using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Input;
using JolieCat.Shared.Enums;

namespace JolieCat.UI.Converters
{
    /// <summary>
    /// Maps the active <see cref="ToolType"/> to the cursor the canvas should show while
    /// that tool is selected - Hand pans, the selection tools mark a precise crosshair,
    /// the paint-like tools show a pen, everything else falls back to the plain arrow.
    /// </summary>
    public sealed class ToolTypeToCursorConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is not ToolType toolType)
                return Cursors.Arrow;

            return toolType switch
            {
                ToolType.Hand => Cursors.Hand,

                ToolType.Move => Cursors.SizeAll,

                ToolType.RectangularMarquee or ToolType.EllipticalMarquee or ToolType.Lasso or
                    ToolType.PolygonalLasso or ToolType.MagneticLasso or ToolType.QuickSelection or
                    ToolType.MagicWand or ToolType.Eyedropper or ToolType.Shape or ToolType.Zoom or
                    ToolType.ColorSampler or ToolType.Ruler or ToolType.ObjectSelection or ToolType.PuppetWarp or ToolType.PatternStamp or ToolType.MixerBrush => Cursors.Cross,

                // A small crosshair: the canvas itself draws the brush-size outline around it.
                ToolType.Brush or ToolType.Pencil or ToolType.Eraser or ToolType.CloneStamp or
                    ToolType.HealingBrush or ToolType.SpotHealing or ToolType.Blur or ToolType.Sharpen or ToolType.Sponge or
                    ToolType.Dodge or ToolType.Burn or ToolType.Smudge or ToolType.HistoryBrush or
                    ToolType.BackgroundEraser or ToolType.MagicEraser or ToolType.RedEye => Cursors.Cross,

                _ => Cursors.Arrow,
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
