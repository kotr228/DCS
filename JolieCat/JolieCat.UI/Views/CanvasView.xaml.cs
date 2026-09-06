using System;
using System.ComponentModel;
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
    /// Hosts the software-rendered Skia surface (<c>SKElement</c>, not the GPU/OpenGL-
    /// backed <c>SKGLElement</c>) for the document canvas - deliberately: SKGLElement
    /// hosts its GL framebuffer via the third-party GLWpfControl (which has to flip the
    /// framebuffer to reconcile OpenGL's bottom-left-origin convention with WPF's own
    /// top-left one to composite it at all), an extra layer with its own coordinate
    /// handling this app's actual pixel data never otherwise passes through - every
    /// layer bitmap, the whole render pipeline, and the .jolie serializer are all plain
    /// top-left-origin CPU raster surfaces already. SKElement removes that layer
    /// entirely rather than trying to out-guess it, at the cost of GPU acceleration this
    /// 2D raster editor doesn't need at these resolutions anyway. Rendering itself is
    /// delegated to <see cref="CanvasRenderer"/>, and all pointer interaction is
    /// delegated to <see cref="CanvasViewModel"/> - this code-behind only translates WPF
    /// input events (DIP coordinates, DPI-scaled to the surface's actual device pixels)
    /// into the view model's calls, and repaints when it asks to.
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
            {
                oldViewModel.InvalidateRequested -= OnInvalidateRequested;
                oldViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            }

            if (e.NewValue is CanvasViewModel newViewModel)
            {
                newViewModel.InvalidateRequested += OnInvalidateRequested;
                newViewModel.PropertyChanged += OnViewModelPropertyChanged;
            }

            // Forces an immediate repaint against whichever CanvasViewModel just
            // became current - needed now that this same CanvasView is reused across
            // document tabs (MainWindow's DataContext="{Binding Canvas}" just points
            // at a different DocumentViewModel's own CanvasViewModel on a tab switch).
            // Without this, switching tabs would keep showing the previous document's
            // last-rendered frame until something unrelated happened to trigger a
            // repaint (a paint stroke, a zoom) on the newly active one.
            Surface.InvalidateVisual();
        }

        private void OnInvalidateRequested(object? sender, EventArgs e) => Surface.InvalidateVisual();

        /// <summary>Drives the text-edit overlay purely from view model state changes -
        /// the same event-driven pattern as <see cref="OnInvalidateRequested"/> - rather
        /// than XAML bindings, since positioning it correctly needs the DPI scale
        /// (<see cref="_dpiScaleX"/>/<see cref="_dpiScaleY"/>), which only this
        /// code-behind knows.</summary>
        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (ViewModel is not { } viewModel) return;

            switch (e.PropertyName)
            {
                case nameof(CanvasViewModel.IsTextEditing):
                    if (viewModel.IsTextEditing)
                    {
                        PositionTextEditOverlay(viewModel);
                        TextEditBox.Visibility = Visibility.Visible;
                        TextEditBox.Focus();
                    }
                    else
                    {
                        TextEditBox.Visibility = Visibility.Collapsed;
                    }
                    break;

                case nameof(CanvasViewModel.TextEditScreenX):
                case nameof(CanvasViewModel.TextEditScreenY):
                    if (viewModel.IsTextEditing)
                        PositionTextEditOverlay(viewModel);
                    break;
            }
        }

        /// <summary>Converts the view model's screen-space (canvas surface device-pixel)
        /// anchor back to DIPs - the inverse of <see cref="ToDevicePixels"/> - so the
        /// overlay lines up with the document point that was actually clicked.</summary>
        private void PositionTextEditOverlay(CanvasViewModel viewModel)
        {
            var left = viewModel.TextEditScreenX / _dpiScaleX;
            var top = viewModel.TextEditScreenY / _dpiScaleY;
            TextEditBox.Margin = new Thickness(left, top, 0, 0);
        }

        private void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
        {
            // The SKElement's PaintSurface reports the surface's actual device-pixel
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
            // ClickCount == 2 is Polygonal Lasso's "close the polygon" gesture.
            viewModel.OnPointerPressed(ToDevicePixels(e.GetPosition(Surface)), e.ClickCount == 2);
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

        /// <summary>Enter (without Shift) commits the text-edit overlay - drawing it onto
        /// the active layer; Shift+Enter inserts a literal newline instead (multi-line
        /// text), since AcceptsReturn would otherwise treat every Enter the same way.
        /// Escape discards the edit without drawing anything.</summary>
        private void TextEditBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (ViewModel is not { } viewModel) return;

            if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
            {
                viewModel.CommitTextEdit();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                viewModel.CancelTextEdit();
                e.Handled = true;
            }
        }

        /// <summary>Shows a copy cursor only while the thing being dragged over the
        /// canvas is actually a file drop - anything else (dragged text, a dragged
        /// color swatch from elsewhere in the app, etc.) gets no drop affordance here.</summary>
        private void Surface_DragEnterOrOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        /// <summary>Importing dropped files is exactly file-dialog import (see
        /// <c>MainViewModel.ImportImageCommand</c>) with the paths coming from the OS's
        /// drag-and-drop payload instead of an Open dialog - both funnel into
        /// <see cref="CanvasViewModel.ImportImageFiles"/>.</summary>
        private void Surface_Drop(object sender, DragEventArgs e)
        {
            if (ViewModel is not { } viewModel) return;
            if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths) return;

            viewModel.ImportImageFiles(paths);
        }
    }
}
