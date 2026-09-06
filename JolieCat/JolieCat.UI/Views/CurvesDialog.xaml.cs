using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using JolieCat.UI.ViewModels;

namespace JolieCat.UI.Views
{
    /// <summary>
    /// The Curves adjustment's interactive spline graph - see
    /// <see cref="CurvesViewModel"/> for the control points and the smooth line
    /// through them; this only translates clicks/drags on the 256x256 graph
    /// (<see cref="GraphCanvas"/>) into calls on it. Reports back via
    /// <see cref="Window.DialogResult"/> only, same as every other adjustment dialog.
    /// </summary>
    public partial class CurvesDialog : Window
    {
        public CurvesDialog()
        {
            InitializeComponent();
        }

        private CurvesViewModel? ViewModel => DataContext as CurvesViewModel;

        /// <summary>Adds a new control point wherever the graph's empty background
        /// was clicked - a click that actually landed on an existing point's own
        /// Thumb never reaches here as a graph-background click, since the Thumb is
        /// on top and handles it as the start of a drag instead.</summary>
        private void GraphCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (ViewModel is not { } viewModel) return;

            var position = e.GetPosition(GraphCanvas);
            viewModel.AddPoint(position.X, 256 - position.Y);
        }

        /// <summary>A point's Thumb reports movement as a relative delta (DIPs, which
        /// this graph's fixed 256x256 sizing makes equal to data units) - X follows
        /// the screen directly, Y is inverted (see CurvePointViewModel.CanvasTop's
        /// own remarks for why) so dragging down actually lowers the curve's output
        /// value instead of raising it.</summary>
        private void PointThumb_DragDelta(object sender, DragDeltaEventArgs e)
        {
            if (sender is not FrameworkElement { DataContext: CurvePointViewModel point }) return;

            point.X = Math.Clamp(point.X + e.HorizontalChange, 0, 255);
            point.Y = Math.Clamp(point.Y - e.VerticalChange, 0, 255);
        }

        private void PointThumb_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (ViewModel is not { } viewModel) return;
            if (sender is not FrameworkElement { DataContext: CurvePointViewModel point }) return;

            viewModel.RemovePoint(point);
            e.Handled = true;
        }

        private void Apply_Click(object sender, RoutedEventArgs e) => DialogResult = true;

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
