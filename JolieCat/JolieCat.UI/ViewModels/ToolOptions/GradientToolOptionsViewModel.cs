using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;

namespace JolieCat.UI.ViewModels.ToolOptions
{
    public enum GradientKind
    {
        Linear,
        Radial,
        Angle,
        Reflected,
    }

    public enum GradientColors
    {
        ForegroundToBackground,
        ForegroundToTransparent,
    }

    /// <summary>Gradient tool settings. The gradient itself is defined by dragging on
    /// the canvas: the drag's start is the first color, its end the second.</summary>
    public partial class GradientToolOptionsViewModel : ObservableObject
    {
        public static IReadOnlyList<GradientKind> AllKinds { get; } = Enum.GetValues<GradientKind>();

        public static IReadOnlyList<GradientColors> AllColorModes { get; } = Enum.GetValues<GradientColors>();

        [ObservableProperty]
        private GradientKind kind = GradientKind.Linear;

        [ObservableProperty]
        private GradientColors colors = GradientColors.ForegroundToBackground;

        /// <summary>0-100%.</summary>
        [ObservableProperty]
        private double opacity = 100;

        [ObservableProperty]
        private bool reverse;
    }
}
