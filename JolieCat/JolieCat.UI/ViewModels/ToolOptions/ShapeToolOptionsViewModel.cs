using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;

namespace JolieCat.UI.ViewModels.ToolOptions
{
    public enum ShapeKind
    {
        Rectangle,
        RoundedRectangle,
        Ellipse,
        Line,
    }

    /// <summary>Options for the Shape tool: drag on the canvas to draw the shape in
    /// the primary color (Shift keeps it square/circular or the line at 45° steps).</summary>
    public partial class ShapeToolOptionsViewModel : ObservableObject
    {
        public static IReadOnlyList<ShapeKind> AllKinds { get; } = Enum.GetValues<ShapeKind>();

        [ObservableProperty]
        private ShapeKind kind = ShapeKind.Rectangle;

        [ObservableProperty]
        private double strokeWidth = 2;

        [ObservableProperty]
        private double cornerRadius = 12;

        [ObservableProperty]
        private bool fillEnabled = true;

        [ObservableProperty]
        private bool antiAlias = true;
    }
}
