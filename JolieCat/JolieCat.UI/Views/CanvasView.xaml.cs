using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using JolieCat.UI.Rendering;
using JolieCat.UI.ViewModels;
using SkiaSharp;
using SkiaSharp.Views.Desktop;

namespace JolieCat.UI.Views
{
    /// <summary>
    /// Hosts the hardware-accelerated Skia render surface for the document canvas.
    /// Rendering itself is delegated to <see cref="CanvasRenderer"/>, and all pointer
    /// interaction is delegated to <see cref="CanvasViewModel"/> - this code-behind only
    /// translates WPF input events (DIP coordinates, DPI-scaled to the surface's actual
    /// device pixels) into the view model's calls, and repaints when it asks to.
    /// </summary>
    public partial class CanvasView : UserControl
    {
        private double _dpiScaleX = 1.0;
        private double _dpiScaleY = 1.0;

        private CanvasViewModel? ViewModel => DataContext as CanvasViewModel;

        public CanvasView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is CanvasViewModel oldViewModel)
                oldViewModel.InvalidateRequested -= OnInvalidateRequested;

            if (e.NewValue is CanvasViewModel newViewModel)
                newViewModel.InvalidateRequested += OnInvalidateRequested;
        }

        private void OnInvalidateRequested(object? sender, EventArgs e) => Surface.InvalidateVisual();

        private void OnPaintSurface(object? sender, SKPaintGLSurfaceEventArgs e)
        {
            // The SKGLElement's PaintSurface reports the surface's actual device-pixel
            // size (e.Info), which differs from Surface.ActualWidth/Height (DIPs) on any
            // display that isn't exactly 96 DPI - cache the ratio so pointer events (only
            // ever given in DIPs by WPF) can be converted to the same device-pixel space
            // CanvasViewModel's pan/zoom math works in.
            if (Surface.ActualWidth > 0) _dpiScaleX = e.Info.Width / Surface.ActualWidth;
            if (Surface.ActualHeight > 0) _dpiScaleY = e.Info.Height / Surface.ActualHeight;

            if (ViewModel is { } viewModel)
                CanvasRenderer.Render(e.Surface.Canvas, e.Info, viewModel);
        }

        private void Surface_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (ViewModel is not { } viewModel) return;

            Surface.Focus();
            Surface.CaptureMouse();
            viewModel.OnPointerPressed(ToDevicePixels(e.GetPosition(Surface)));
        }

        private void Surface_MouseMove(object sender, MouseEventArgs e)
        {
            ViewModel?.OnPointerMoved(ToDevicePixels(e.GetPosition(Surface)));
        }

        private void Surface_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (ViewModel is not { } viewModel) return;

            viewModel.OnPointerReleased(ToDevicePixels(e.GetPosition(Surface)));
            Surface.ReleaseMouseCapture();
        }

        private void Surface_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            ViewModel?.OnMouseWheel(ToDevicePixels(e.GetPosition(Surface)), e.Delta);
            e.Handled = true;
        }

        /// <summary>
        /// Capture can be lost without a matching MouseLeftButtonUp - e.g. a dialog or
        /// another window steals it mid-drag. Without this, a gesture flag could get
        /// stuck true, making later mouse moves keep painting/panning with no button held.
        /// </summary>
        private void Surface_LostMouseCapture(object sender, MouseEventArgs e)
        {
            ViewModel?.CancelInteraction();
        }

        private SKPoint ToDevicePixels(Point point) =>
            new((float)(point.X * _dpiScaleX), (float)(point.Y * _dpiScaleY));
    }
}
