using CommunityToolkit.Mvvm.ComponentModel;

namespace JolieCat.UI.ViewModels
{
    /// <summary>One draggable control point on the Curves graph - input (<see cref="X"/>)
    /// and output (<see cref="Y"/>), both 0-255.</summary>
    public sealed partial class CurvePointViewModel : ObservableObject
    {
        [ObservableProperty]
        private double x;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanvasTop))]
        private double y;

        /// <summary>WPF's Canvas has Y growing downward; curve data has Y growing
        /// upward (0 = black at the bottom, 255 = white at the top - the convention
        /// every curves tool uses) - this is the one place that flip happens, so
        /// <see cref="Views.CurvesDialog"/> can bind <c>Canvas.Top</c> straight to
        /// this without a value converter, while <see cref="X"/> (which does grow
        /// the same direction on both axes) needs none at all.</summary>
        public double CanvasTop => 255 - Y;

        public CurvePointViewModel(double x, double y)
        {
            this.x = x;
            this.y = y;
        }
    }
}
