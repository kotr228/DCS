using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using JolieCat.Core.Documents;
using SkiaSharp;

namespace JolieCat.UI.ViewModels.ToolOptions
{
    public enum ShapeKind
    {
        Rectangle,
        RoundedRectangle,
        Ellipse,
        Line,
        Polygon,
        Star,
    }

    public enum ShapeMode
    {
        /// <summary>Each shape goes on its own editable shape layer.</summary>
        Shape,

        /// <summary>The shape is painted onto the active layer's pixels.</summary>
        Pixels,
    }

    /// <summary>Options for the Shape tool: drag on the canvas to draw the shape (Shift
    /// keeps it square/circular or the line at 45° steps). With a shape layer selected,
    /// changing these restyles it at once.</summary>
    public partial class ShapeToolOptionsViewModel : ObservableObject
    {
        public static IReadOnlyList<ShapeKind> AllKinds { get; } = Enum.GetValues<ShapeKind>();

        public static IReadOnlyList<ShapeMode> AllModes { get; } = Enum.GetValues<ShapeMode>();

        [ObservableProperty]
        private ShapeMode mode = ShapeMode.Shape;

        [ObservableProperty]
        private ShapeKind kind = ShapeKind.Rectangle;

        [ObservableProperty]
        private double strokeWidth = 2;

        [ObservableProperty]
        private double cornerRadius = 12;

        /// <summary>Polygon sides / star points.</summary>
        [ObservableProperty]
        private double sides = 5;

        [ObservableProperty]
        private bool fillEnabled = true;

        [ObservableProperty]
        private string fillColor = "#3D8BFD";

        [ObservableProperty]
        private string strokeColor = "#000000";

        [ObservableProperty]
        private bool antiAlias = true;

        /// <summary>The shape these settings describe, between two corners.</summary>
        public ShapeContent ToShape(SKPoint start, SKPoint end) => new(
            Enum.Parse<VectorShapeKind>(Kind.ToString()),
            start, end,
            FillEnabled ? LayerStyleViewModel.Parse(FillColor, SKColors.Gray) : SKColors.Transparent,
            LayerStyleViewModel.Parse(StrokeColor, SKColors.Black),
            (float)StrokeWidth,
            (float)CornerRadius,
            (int)Math.Round(Sides),
            AntiAlias);

        /// <summary>These settings applied to an existing shape: its look changes, its
        /// kind and position stay (like Photoshop's shape options).</summary>
        public ShapeContent ApplyTo(ShapeContent shape) => ToShape(shape.Start, shape.End) with { Kind = shape.Kind };

        public void LoadFrom(ShapeContent shape)
        {
            Kind = Enum.Parse<ShapeKind>(shape.Kind.ToString());
            StrokeWidth = shape.StrokeWidth;
            CornerRadius = shape.CornerRadius;
            Sides = shape.Sides;
            FillEnabled = shape.Fill.Alpha > 0;
            if (shape.Fill.Alpha > 0) FillColor = LayerStyleViewModel.Hex(shape.Fill);
            StrokeColor = LayerStyleViewModel.Hex(shape.Stroke);
            AntiAlias = shape.AntiAlias;
        }
    }
}
