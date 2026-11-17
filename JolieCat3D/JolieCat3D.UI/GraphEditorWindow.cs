using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using JolieCat3D.Core.Scene;
using JolieCat3D.Service.Animation;

namespace JolieCat3D.UI
{
    /// <summary>
    /// Blender-style Graph Editor: the active object's transform F-curves (X/Y/Z location,
    /// rotation and scale) over time. Keys can be dragged in time (snapped to frames) and
    /// value; each channel has its own keys and Bézier handles (drag the round handle ends
    /// of the selected key). Each drag is one undo step. Wheel zooms time (Ctrl+wheel: values), middle
    /// mouse pans, F frames everything, clicking the ruler moves the playhead.
    /// </summary>
    public sealed class GraphEditorWindow : Window
    {
        private const double RulerHeight = 22;
        private const double KeySize = 7;

        private static readonly Brush Background1 = Freeze(new SolidColorBrush(Color.FromRgb(0x1E, 0x1B, 0x1A)));
        private static readonly Brush PanelBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x26, 0x22, 0x21)));
        private static readonly Brush GridBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x30, 0x2C, 0x2A)));
        private static readonly Brush TextBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xC8, 0xC0, 0xB8)));
        private static readonly Brush PlayheadBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x4C, 0x9A, 0xF0)));
        private static readonly Brush KeyBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8)));
        private static readonly Brush SelectedKeyBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xF0, 0xA0, 0x30)));
        private static readonly Brush[] AxisBrushes =
        {
            Freeze(new SolidColorBrush(Color.FromRgb(0xE0, 0x4A, 0x55))),
            Freeze(new SolidColorBrush(Color.FromRgb(0x72, 0xC0, 0x4C))),
            Freeze(new SolidColorBrush(Color.FromRgb(0x4C, 0x80, 0xE8))),
        };

        private readonly Func<AnimationTimeline> _timeline;
        private readonly Func<Node?> _activeNode;
        private readonly Action<Node, AnimationTrack?, string> _commit;
        private readonly Action _preview;
        private readonly Action<double> _setTime;

        private readonly Canvas _canvas = new() { ClipToBounds = true, Background = Background1 };
        private readonly StackPanel _channelPanel = new() { Margin = new Thickness(6) };
        private readonly Dictionary<AnimationChannel, CheckBox> _channelBoxes = new();
        private readonly TextBlock _status = new() { Foreground = TextBrush, Margin = new Thickness(8, 4, 8, 4), VerticalAlignment = VerticalAlignment.Center };

        // view (frames horizontally, channel value vertically)
        private double _frameMin = 0, _frameMax = 120, _valueMin = -2, _valueMax = 2;
        private bool _hasFramed;

        // selection & drag state
        private FCurveKey? _selectedKey;
        private AnimationChannel? _selectedChannel;
        private AnimationTrack? _dragBefore;
        private enum DragPart { None, Key, LeftHandle, RightHandle }
        private DragPart _dragPart;
        private Point _panStart;
        private (double FMin, double FMax, double VMin, double VMax) _panView;
        private bool _isPanning, _isScrubbing;

        public GraphEditorWindow(Func<AnimationTimeline> timeline, Func<Node?> activeNode,
            Action<Node, AnimationTrack?, string> commit, Action preview, Action<double> setTime)
        {
            _timeline = timeline;
            _activeNode = activeNode;
            _commit = commit;
            _preview = preview;
            _setTime = setTime;

            Title = "Graph Editor - JolieCat3D";
            Width = 900;
            Height = 420;
            Background = PanelBrush;
            Foreground = TextBrush;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            foreach (var channel in AnimationChannels.All)
            {
                var box = new CheckBox
                {
                    Content = AnimationChannels.DisplayName(channel),
                    IsChecked = channel <= AnimationChannel.LocationZ,
                    Foreground = AxisBrushes[AnimationChannels.Axis(channel)],
                    Margin = new Thickness(0, 2, 0, 2),
                };
                box.Checked += (_, _) => Redraw();
                box.Unchecked += (_, _) => Redraw();
                _channelBoxes[channel] = box;
                if (AnimationChannels.Axis(channel) == 0 && channel != AnimationChannel.LocationX)
                    _channelPanel.Children.Add(new Separator { Margin = new Thickness(0, 4, 0, 4) });
                _channelPanel.Children.Add(box);
            }

            var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4) };
            toolbar.Children.Add(MakeButton("Frame All (F)", (_, _) => FrameAll()));
            toolbar.Children.Add(MakeButton("Linear", (_, _) => SetInterpolation(InterpolationMode.Linear)));
            toolbar.Children.Add(MakeButton("Bezier", (_, _) => SetInterpolation(InterpolationMode.Bezier)));
            toolbar.Children.Add(MakeButton("Constant", (_, _) => SetInterpolation(InterpolationMode.Constant)));
            toolbar.Children.Add(MakeButton("Auto Handles", (_, _) => ResetHandles()));
            toolbar.Children.Add(MakeButton("Delete Key (X)", (_, _) => DeleteSelectedKey()));
            toolbar.Children.Add(_status);

            var root = new DockPanel();
            DockPanel.SetDock(toolbar, Dock.Top);
            root.Children.Add(toolbar);
            var channels = new ScrollViewer { Content = _channelPanel, Width = 130, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            DockPanel.SetDock(channels, Dock.Left);
            root.Children.Add(channels);
            root.Children.Add(_canvas);
            Content = root;

            _canvas.SizeChanged += (_, _) => Redraw();
            _canvas.MouseLeftButtonDown += OnMouseLeftDown;
            _canvas.MouseMove += OnMouseMove;
            _canvas.MouseLeftButtonUp += OnMouseLeftUp;
            _canvas.MouseDown += OnMouseDown;
            _canvas.MouseUp += (_, e) => { if (e.ChangedButton == MouseButton.Middle) { _isPanning = false; _canvas.ReleaseMouseCapture(); } };
            _canvas.MouseWheel += OnWheel;
            PreviewKeyDown += OnKey;
            Loaded += (_, _) => { FrameAll(); Focus(); };
        }

        private static Brush Freeze(Brush b) { b.Freeze(); return b; }

        private static Button MakeButton(string text, RoutedEventHandler click)
        {
            var b = new Button { Content = text, Margin = new Thickness(0, 0, 4, 0), Padding = new Thickness(8, 2, 8, 2) };
            b.Click += click;
            return b;
        }

        // ---------------- data helpers ----------------

        private double FrameRate => Math.Max(1, _timeline().FrameRate);

        private AnimationTrack? ActiveTrack() =>
            _activeNode() is { } node && _timeline().TryGetTrack(node, out var track) ? track : null;

        private IEnumerable<AnimationChannel> VisibleChannels() =>
            AnimationChannels.All.Where(c => _channelBoxes[c].IsChecked == true);

        // ---------------- coordinates ----------------

        private double PlotHeight => Math.Max(1, _canvas.ActualHeight - RulerHeight);

        private double FrameToX(double frame) => (frame - _frameMin) / (_frameMax - _frameMin) * Math.Max(1, _canvas.ActualWidth);
        private double XToFrame(double x) => _frameMin + x / Math.Max(1, _canvas.ActualWidth) * (_frameMax - _frameMin);
        private double ValueToY(double v) => RulerHeight + (1 - (v - _valueMin) / (_valueMax - _valueMin)) * PlotHeight;
        private double YToValue(double y) => _valueMin + (1 - (y - RulerHeight) / PlotHeight) * (_valueMax - _valueMin);

        public void FrameAll()
        {
            var track = ActiveTrack();
            var channels = VisibleChannels().ToList();
            var keys = track is null ? new List<FCurveKey>() : channels.SelectMany(c => track.Curve(c).Keys).ToList();
            if (keys.Count == 0)
            {
                _frameMin = 0; _frameMax = Math.Max(10, _timeline().TotalFrames);
                _valueMin = -2; _valueMax = 2;
            }
            else
            {
                var f0 = keys.Min(k => k.Time) * FrameRate; var f1 = keys.Max(k => k.Time) * FrameRate;
                var v0 = keys.Min(k => k.Value); var v1 = keys.Max(k => k.Value);
                var fPad = Math.Max(5, (f1 - f0) * 0.1);
                var vPad = Math.Max(0.5, (v1 - v0) * 0.15);
                _frameMin = f0 - fPad; _frameMax = f1 + fPad;
                _valueMin = v0 - vPad; _valueMax = v1 + vPad;
            }
            _hasFramed = true;
            Redraw();
        }

        // ---------------- drawing ----------------

        /// <summary>Rebuilds the picture - call when the selection, keys or current frame change.</summary>
        public void Redraw()
        {
            if (!IsLoaded) return;
            if (!_hasFramed) { FrameAll(); return; }
            _canvas.Children.Clear();
            var w = _canvas.ActualWidth;
            var h = _canvas.ActualHeight;
            if (w < 2 || h < 2) return;

            DrawGrid(w, h);

            var node = _activeNode();
            var track = ActiveTrack();
            _status.Text = node is null ? "No active object" : track is null ? $"{node.Name}: no keyframes (press I in the viewport)"
                : _selectedKey is { } sk && _selectedChannel is { } sc ? $"{node.Name} · {AnimationChannels.DisplayName(sc)} · frame {sk.Time * FrameRate:0.#} = {sk.Value:0.###} ({sk.Interpolation})"
                : $"{node.Name}: {track.Keyframes.Count} keyframe time(s)";

            if (track is not null)
            {
                foreach (var channel in VisibleChannels())
                {
                    var brush = AxisBrushes[AnimationChannels.Axis(channel)];
                    var curve = new Polyline { Stroke = brush, StrokeThickness = channel >= AnimationChannel.ScaleX ? 1.2 : channel >= AnimationChannel.RotationX ? 1.4 : 1.8, Opacity = 0.9 };
                    if (channel >= AnimationChannel.RotationX && channel < AnimationChannel.ScaleX) curve.StrokeDashArray = new DoubleCollection { 6, 3 };
                    if (channel >= AnimationChannel.ScaleX) curve.StrokeDashArray = new DoubleCollection { 2, 3 };
                    const int samples = 240;
                    for (var i = 0; i <= samples; i++)
                    {
                        var frame = _frameMin + (_frameMax - _frameMin) * i / samples;
                        if (AnimationChannels.Evaluate(track, channel, frame / FrameRate) is { } v)
                            curve.Points.Add(new Point(FrameToX(frame), ValueToY(v)));
                    }
                    _canvas.Children.Add(curve);

                    foreach (var key in track.Curve(channel).Keys)
                    {
                        var selected = ReferenceEquals(key, _selectedKey);
                        var center = KeyPoint(key);
                        if (selected && key.Interpolation == InterpolationMode.Bezier)
                        {
                            foreach (var hp in new[] { HandlePoint(key, left: true), HandlePoint(key, left: false) })
                            {
                                _canvas.Children.Add(new Line { X1 = center.X, Y1 = center.Y, X2 = hp.X, Y2 = hp.Y, Stroke = SelectedKeyBrush, StrokeThickness = 1 });
                                var dot = new Ellipse { Width = 7, Height = 7, Fill = Background1, Stroke = SelectedKeyBrush, StrokeThickness = 1.5 };
                                Canvas.SetLeft(dot, hp.X - 3.5);
                                Canvas.SetTop(dot, hp.Y - 3.5);
                                _canvas.Children.Add(dot);
                            }
                        }
                        var marker = new Rectangle
                        {
                            Width = KeySize, Height = KeySize,
                            Fill = selected ? SelectedKeyBrush : KeyBrush,
                            Stroke = Brushes.Black, StrokeThickness = 1,
                            RenderTransform = new RotateTransform(45, KeySize / 2, KeySize / 2),
                            ToolTip = $"{AnimationChannels.DisplayName(channel)}  frame {key.Time * FrameRate:0.#}  value {key.Value:0.###}  ({key.Interpolation})",
                        };
                        Canvas.SetLeft(marker, center.X - KeySize / 2);
                        Canvas.SetTop(marker, center.Y - KeySize / 2);
                        _canvas.Children.Add(marker);
                    }
                }
            }

            // Playhead
            var playX = FrameToX(_timeline().CurrentFrame);
            _canvas.Children.Add(new Line { X1 = playX, X2 = playX, Y1 = 0, Y2 = h, Stroke = PlayheadBrush, StrokeThickness = 1.5 });
            var label = new Border { Background = PlayheadBrush, CornerRadius = new CornerRadius(3), Padding = new Thickness(4, 0, 4, 0), Child = new TextBlock { Text = $"{_timeline().CurrentFrame:0}", Foreground = Brushes.White, FontSize = 11 } };
            Canvas.SetLeft(label, playX - 12);
            Canvas.SetTop(label, 2);
            _canvas.Children.Add(label);
        }

        private void DrawGrid(double w, double h)
        {
            _canvas.Children.Add(new Rectangle { Width = w, Height = RulerHeight, Fill = PanelBrush });

            var frameStep = NiceStep((_frameMax - _frameMin) / Math.Max(1, w / 70));
            for (var f = Math.Ceiling(_frameMin / frameStep) * frameStep; f <= _frameMax; f += frameStep)
            {
                var x = FrameToX(f);
                _canvas.Children.Add(new Line { X1 = x, X2 = x, Y1 = RulerHeight, Y2 = h, Stroke = GridBrush });
                var t = new TextBlock { Text = $"{f:0.##}", Foreground = TextBrush, FontSize = 10 };
                Canvas.SetLeft(t, x + 2);
                Canvas.SetTop(t, 4);
                _canvas.Children.Add(t);
            }

            var valueStep = NiceStep((_valueMax - _valueMin) / Math.Max(1, PlotHeight / 40));
            for (var v = Math.Ceiling(_valueMin / valueStep) * valueStep; v <= _valueMax; v += valueStep)
            {
                var y = ValueToY(v);
                _canvas.Children.Add(new Line { X1 = 0, X2 = w, Y1 = y, Y2 = y, Stroke = Math.Abs(v) < valueStep * 1e-3 ? TextBrush : GridBrush, StrokeThickness = Math.Abs(v) < valueStep * 1e-3 ? 0.6 : 1 });
                var t = new TextBlock { Text = $"{v:0.###}", Foreground = TextBrush, FontSize = 10, Opacity = 0.7 };
                Canvas.SetLeft(t, 4);
                Canvas.SetTop(t, y - 13);
                _canvas.Children.Add(t);
            }
        }

        private static double NiceStep(double raw)
        {
            if (raw <= 0 || double.IsNaN(raw)) return 1;
            var magnitude = Math.Pow(10, Math.Floor(Math.Log10(raw)));
            var n = raw / magnitude;
            return (n < 1.5 ? 1 : n < 3.5 ? 2 : n < 7.5 ? 5 : 10) * magnitude;
        }

        // ---------------- interaction ----------------

        private Point KeyPoint(FCurveKey key) => new(FrameToX(key.Time * FrameRate), ValueToY(key.Value));

        private Point HandlePoint(FCurveKey key, bool left)
        {
            var h = left ? key.HandleLeft : key.HandleRight;
            return new Point(FrameToX((key.Time + h.X) * FrameRate), ValueToY(key.Value + h.Y));
        }

        private (FCurveKey Key, AnimationChannel Channel, DragPart Part)? HitTest(Point p)
        {
            if (ActiveTrack() is not { } track) return null;
            // Handles of the selected key first.
            if (_selectedKey is { Interpolation: InterpolationMode.Bezier } sel && _selectedChannel is { } selChannel && track.Curve(selChannel).Keys.Contains(sel))
            {
                if ((HandlePoint(sel, true) - p).Length < 7) return (sel, selChannel, DragPart.LeftHandle);
                if ((HandlePoint(sel, false) - p).Length < 7) return (sel, selChannel, DragPart.RightHandle);
            }
            (FCurveKey, AnimationChannel, DragPart)? best = null;
            var bestDistance = 8.0;
            foreach (var channel in VisibleChannels())
                foreach (var key in track.Curve(channel).Keys)
                {
                    var d = (KeyPoint(key) - p).Length;
                    if (d < bestDistance) { bestDistance = d; best = (key, channel, DragPart.Key); }
                }
            return best;
        }

        private void OnMouseLeftDown(object sender, MouseButtonEventArgs e)
        {
            var p = e.GetPosition(_canvas);
            _canvas.Focus();
            if (p.Y < RulerHeight)
            {
                _isScrubbing = true;
                _canvas.CaptureMouse();
                _setTime(Math.Round(XToFrame(p.X)) / FrameRate);
                Redraw();
                return;
            }

            if (HitTest(p) is { } hit && ActiveTrack() is { } track)
            {
                _selectedKey = hit.Key;
                _selectedChannel = hit.Channel;
                _dragPart = hit.Part;
                _dragBefore = track.Clone();
                _canvas.CaptureMouse();
            }
            else
            {
                _selectedKey = null;
                _selectedChannel = null;
            }
            Redraw();
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            var p = e.GetPosition(_canvas);
            if (_isPanning)
            {
                var dxFrames = (p.X - _panStart.X) / Math.Max(1, _canvas.ActualWidth) * (_panView.FMax - _panView.FMin);
                var dyValues = (p.Y - _panStart.Y) / PlotHeight * (_panView.VMax - _panView.VMin);
                _frameMin = _panView.FMin - dxFrames; _frameMax = _panView.FMax - dxFrames;
                _valueMin = _panView.VMin + dyValues; _valueMax = _panView.VMax + dyValues;
                Redraw();
                return;
            }
            if (_isScrubbing)
            {
                _setTime(Math.Max(0, Math.Round(XToFrame(p.X))) / FrameRate);
                Redraw();
                return;
            }
            if (_dragBefore is null || _selectedKey is not { } key || _selectedChannel is not { } channel || ActiveTrack() is not { } track) return;
            var curve = track.Curve(channel);

            switch (_dragPart)
            {
                case DragPart.Key:
                    curve.Retime(key, Math.Max(0, Math.Round(XToFrame(p.X))) / FrameRate); // refused onto another key
                    key.Value = (float)YToValue(p.Y);
                    curve.Changed();
                    break;
                case DragPart.LeftHandle:
                case DragPart.RightHandle:
                {
                    var left = _dragPart == DragPart.LeftHandle;
                    var dt = (float)(XToFrame(p.X) / FrameRate - key.Time);
                    var dv = (float)(YToValue(p.Y) - key.Value);
                    if (left) dt = Math.Min(dt, -1e-4f); else dt = Math.Max(dt, 1e-4f);
                    var moved = new System.Numerics.Vector2(dt, dv);
                    var other = left ? key.HandleRight : key.HandleLeft;
                    // "Aligned" handles: the opposite one keeps its length but follows the direction.
                    var mirrored = -System.Numerics.Vector2.Normalize(moved) * other.Length();
                    key.AutoHandles = false;
                    if (left) { key.HandleLeft = moved; key.HandleRight = mirrored; }
                    else { key.HandleRight = moved; key.HandleLeft = mirrored; }
                    break;
                }
            }
            _preview();
            Redraw();
        }

        private void OnMouseLeftUp(object sender, MouseButtonEventArgs e)
        {
            _canvas.ReleaseMouseCapture();
            _isScrubbing = false;
            if (_dragBefore is { } before && _activeNode() is { } node && ActiveTrack() is { } track && !track.ContentEquals(before))
                _commit(node, before, _dragPart == DragPart.Key ? "Move Keyframe" : "Edit Handle");
            _dragBefore = null;
            _dragPart = DragPart.None;
        }

        private void OnMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Middle) return;
            _isPanning = true;
            _panStart = e.GetPosition(_canvas);
            _panView = (_frameMin, _frameMax, _valueMin, _valueMax);
            _canvas.CaptureMouse();
        }

        private void OnWheel(object sender, MouseWheelEventArgs e)
        {
            var p = e.GetPosition(_canvas);
            var factor = Math.Pow(0.85, e.Delta / 120.0);
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                var v = YToValue(p.Y);
                _valueMin = v + (_valueMin - v) * factor;
                _valueMax = v + (_valueMax - v) * factor;
            }
            else
            {
                var f = XToFrame(p.X);
                _frameMin = f + (_frameMin - f) * factor;
                _frameMax = f + (_frameMax - f) * factor;
            }
            Redraw();
            e.Handled = true;
        }

        private void OnKey(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.F: FrameAll(); e.Handled = true; break;
                case Key.X or Key.Delete: DeleteSelectedKey(); e.Handled = true; break;
                case Key.T: SetInterpolation(InterpolationMode.Bezier); e.Handled = true; break;
            }
        }

        /// <summary>Applies an edit to the selected key of the selected channel as one undo step.</summary>
        private void EditSelected(string description, Action<FCurve, FCurveKey> edit)
        {
            if (_selectedKey is not { } key || _selectedChannel is not { } channel || _activeNode() is not { } node || ActiveTrack() is not { } track) return;
            var before = track.Clone();
            var curve = track.Curve(channel);
            edit(curve, key);
            curve.Changed();
            if (track.IsEmpty) _timeline().RemoveTrack(node);
            _commit(node, before, description);
            _preview();
            Redraw();
        }

        private void SetInterpolation(InterpolationMode mode) =>
            EditSelected("Keyframe Interpolation", (_, k) => k.Interpolation = mode);

        private void ResetHandles() =>
            EditSelected("Auto Handles", (_, k) => k.AutoHandles = true);

        private void DeleteSelectedKey()
        {
            EditSelected("Delete Keyframe", (curve, k) => curve.Remove(k));
            _selectedKey = null;
            _selectedChannel = null;
            Redraw();
        }
    }
}
