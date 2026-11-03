using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using JolieCat.UI.ViewModels.ToolOptions;
using JolieCat.UI.Views;

namespace JolieCat.UI.Controls
{
    /// <summary>
    /// The gradient bar with draggable color stops (Photoshop's Gradient Editor strip),
    /// bound to a <see cref="GradientToolOptionsViewModel"/> DataContext:
    /// drag a stop to move it, click the strip below the bar to add one, double-click a
    /// stop to pick its color, drag a stop down off the strip to remove it.
    /// </summary>
    public sealed class GradientStopsBar : Canvas
    {
        private const double BarHeight = 18;
        private const double MarkerSize = 12;
        private const double RemoveDistance = 30;

        private GradientToolOptionsViewModel? _options;
        private GradientStopViewModel? _dragging;
        private bool _draggedOff;

        public GradientStopsBar()
        {
            Height = BarHeight + MarkerSize + 4;
            Background = Brushes.Transparent; // hit-testable everywhere
            ClipToBounds = false;
            DataContextChanged += (_, _) => Attach(DataContext as GradientToolOptionsViewModel);
            SizeChanged += (_, _) => Rebuild();
        }

        private void Attach(GradientToolOptionsViewModel? options)
        {
            if (_options is not null)
            {
                _options.Stops.CollectionChanged -= OnStopsChanged;
                _options.PropertyChanged -= OnOptionsChanged;
            }
            _options = options;
            if (_options is not null)
            {
                _options.Stops.CollectionChanged += OnStopsChanged;
                _options.PropertyChanged += OnOptionsChanged;
            }
            Rebuild();
        }

        private void OnStopsChanged(object? sender, NotifyCollectionChangedEventArgs e) => Rebuild();

        // PreviewBrush changes whenever any stop's color/location/opacity changes.
        private void OnOptionsChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(GradientToolOptionsViewModel.PreviewBrush)) Rebuild();
        }

        private double Usable => Math.Max(1, ActualWidth - MarkerSize);

        private double XOf(double location) => MarkerSize / 2 + location / 100 * Usable;

        private double LocationAt(double x) => Math.Clamp((x - MarkerSize / 2) / Usable * 100, 0, 100);

        private void Rebuild()
        {
            Children.Clear();
            if (_options is null || ActualWidth <= 0) return;

            var checker = new DrawingBrush(new DrawingGroup
            {
                Children =
                {
                    new GeometryDrawing(new SolidColorBrush(Color.FromRgb(0x5A, 0x55, 0x55)), null, Geometry.Parse("M0,0 H2 V2 H0Z")),
                    new GeometryDrawing(new SolidColorBrush(Color.FromRgb(0x6E, 0x69, 0x69)), null, Geometry.Parse("M0,0 H1 V1 H0Z M1,1 H2 V2 H1Z")),
                },
            }) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 8, 8), ViewportUnits = BrushMappingMode.Absolute };

            var bar = new Border
            {
                Width = Usable,
                Height = BarHeight,
                Background = checker,
                BorderBrush = (Brush)FindResource("BorderBrush"),
                BorderThickness = new Thickness(1),
                Child = new Border { Background = _options.PreviewBrush },
                ToolTip = "Click below the bar to add a color stop",
            };
            SetLeft(bar, MarkerSize / 2);
            SetTop(bar, 0);
            Children.Add(bar);

            foreach (var stop in _options.Stops)
            {
                var c = stop.Color;
                var marker = new Path
                {
                    // A little house shape pointing up at the bar.
                    Data = Geometry.Parse($"M{MarkerSize / 2},0 L{MarkerSize},{MarkerSize / 2} L{MarkerSize},{MarkerSize} L0,{MarkerSize} L0,{MarkerSize / 2} Z"),
                    Fill = new SolidColorBrush(Color.FromRgb(c.Red, c.Green, c.Blue)),
                    Stroke = ReferenceEquals(stop, _dragging) ? (Brush)FindResource("AccentBrush") : Brushes.White,
                    StrokeThickness = 1.2,
                    Cursor = Cursors.SizeWE,
                    Tag = stop,
                    ToolTip = $"{stop.Hex} at {stop.Location:0}% - drag to move, double-click to change the color, drag down to remove",
                };
                SetLeft(marker, XOf(stop.Location) - MarkerSize / 2);
                SetTop(marker, BarHeight + 2 + (ReferenceEquals(stop, _dragging) && _draggedOff ? RemoveDistance / 2 : 0));
                marker.Opacity = ReferenceEquals(stop, _dragging) && _draggedOff ? 0.4 : 1;
                Children.Add(marker);
            }
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            if (_options is null) return;
            var p = e.GetPosition(this);

            if (e.OriginalSource is Path { Tag: GradientStopViewModel stop })
            {
                if (e.ClickCount == 2)
                {
                    if (ColorPickerDialog.Pick(stop.Hex, Window.GetWindow(this)) is { } hex) stop.Hex = hex;
                    e.Handled = true;
                    return;
                }
                _dragging = stop;
                _draggedOff = false;
                CaptureMouse();
                e.Handled = true;
                return;
            }

            // Below the bar: a new stop, colored like the gradient there.
            if (p.Y > BarHeight)
            {
                var location = Math.Round(LocationAt(p.X));
                var color = _options.ColorAt(location / 100);
                var added = new GradientStopViewModel($"#{color.Red:X2}{color.Green:X2}{color.Blue:X2}", location, Math.Round(color.Alpha / 2.55));
                _options.Stops.Add(added);
                _dragging = added;
                CaptureMouse();
                e.Handled = true;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_dragging is null || _options is null) return;
            var p = e.GetPosition(this);
            _draggedOff = p.Y > Height + RemoveDistance && _options.Stops.Count > 2;
            _dragging.Location = Math.Round(LocationAt(p.X));
            Rebuild();
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);
            if (_dragging is null || _options is null) return;
            if (_draggedOff) _options.RemoveStopCommand.Execute(_dragging);
            _dragging = null;
            _draggedOff = false;
            ReleaseMouseCapture();
            Rebuild();
        }
    }
}
