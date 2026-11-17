using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using JolieCat3D.Service.Animation;
using JolieCat3D.Service.Commands;

namespace JolieCat3D.UI
{
    /// <summary>
    /// Blender-style NLA Editor: animation layers (tracks, bottom to top) holding action
    /// strips over time, with the active action as the topmost row. Drag a strip to move
    /// it in time (snapped to frames), click the ruler to move the playhead, wheel zooms,
    /// middle mouse pans. Every edit is one undo step.
    /// </summary>
    public sealed class NlaEditorWindow : Window
    {
        private const double RulerHeight = 22;
        private const double RowHeight = 30;
        private const double HeaderWidth = 150;

        private static readonly Brush Background1 = Freeze(new SolidColorBrush(Color.FromRgb(0x1E, 0x1B, 0x1A)));
        private static readonly Brush PanelBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x26, 0x22, 0x21)));
        private static readonly Brush RowBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x2B, 0x27, 0x25)));
        private static readonly Brush GridBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x34, 0x30, 0x2E)));
        private static readonly Brush TextBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xC8, 0xC0, 0xB8)));
        private static readonly Brush DimTextBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x80, 0x78, 0x70)));
        private static readonly Brush PlayheadBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x4C, 0x9A, 0xF0)));
        private static readonly Brush StripBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x8A, 0x6E, 0x3C)));
        private static readonly Brush AddStripBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x4E, 0x6E, 0x8E)));
        private static readonly Brush MutedStripBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x48, 0x44, 0x42)));
        private static readonly Brush SelectedBorder = Freeze(new SolidColorBrush(Color.FromRgb(0xF0, 0xC0, 0x60)));
        private static readonly Brush ActionRowBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x3A, 0x30, 0x22)));
        private static readonly Brush HoldBrush = Freeze(new SolidColorBrush(Color.FromArgb(0x50, 0x8A, 0x6E, 0x3C)));

        private readonly Func<AnimationTimeline> _timeline;
        private readonly Action<IUndoableCommand> _record;
        private readonly Action<double> _setTime;
        private readonly Action _changed;

        private readonly Canvas _canvas = new() { ClipToBounds = true, Background = Background1 };
        private readonly TextBlock _status = new() { Foreground = TextBrush, Margin = new Thickness(8, 4, 8, 4), VerticalAlignment = VerticalAlignment.Center };
        private readonly StackPanel _props = new() { Margin = new Thickness(8), Width = 210 };

        private double _frameMin = -5, _frameMax = 125;
        private int _selTrack = -1, _selStrip = -1;   // indices survive undo snapshots
        private int _selectedTrackRow = -1;           // a track picked without a strip

        // drag state
        private bool _dragging, _isPanning, _isScrubbing;
        private double _dragOffsetFrames;
        private List<NlaTrack>? _dragBefore;
        private Point _panStart;
        private (double Min, double Max) _panView;
        private bool _updatingProps;

        public NlaEditorWindow(Func<AnimationTimeline> timeline, Action<IUndoableCommand> record, Action<double> setTime, Action changed)
        {
            _timeline = timeline;
            _record = record;
            _setTime = setTime;
            _changed = changed;

            Title = "NLA Editor - JolieCat3D";
            Width = 1000;
            Height = 380;
            Background = PanelBrush;
            Foreground = TextBrush;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            var toolbar = new WrapPanel { Margin = new Thickness(4) };
            toolbar.Children.Add(MakeButton("Push Down", (_, _) => PushDown(), "Move the active action into a new NLA track and start a fresh action"));
            toolbar.Children.Add(MakeButton("Add Track", (_, _) => AddTrack()));
            toolbar.Children.Add(MakeButton("Add Strip", (_, _) => AddStrip(), "Add the active action as a strip at the playhead (on the selected track)"));
            toolbar.Children.Add(MakeButton("Delete (X)", (_, _) => DeleteSelection()));
            toolbar.Children.Add(MakeButton("Mute Track", (_, _) => ToggleMuteTrack()));
            toolbar.Children.Add(MakeButton("Track Up", (_, _) => MoveTrack(+1)));
            toolbar.Children.Add(MakeButton("Track Down", (_, _) => MoveTrack(-1)));
            toolbar.Children.Add(MakeButton("Bake to Action", (_, _) => Bake(), "Evaluate all layers into one new action (for game export)"));
            toolbar.Children.Add(MakeButton("Frame All (F)", (_, _) => FrameAll()));
            toolbar.Children.Add(_status);

            var root = new DockPanel();
            DockPanel.SetDock(toolbar, Dock.Top);
            root.Children.Add(toolbar);
            var propsScroll = new ScrollViewer { Content = _props, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            DockPanel.SetDock(propsScroll, Dock.Right);
            root.Children.Add(propsScroll);
            root.Children.Add(_canvas);
            Content = root;

            _canvas.SizeChanged += (_, _) => Redraw();
            _canvas.MouseLeftButtonDown += OnMouseLeftDown;
            _canvas.MouseMove += OnMouseMove;
            _canvas.MouseLeftButtonUp += OnMouseLeftUp;
            _canvas.MouseDown += (_, e) =>
            {
                if (e.ChangedButton != MouseButton.Middle) return;
                _isPanning = true;
                _panStart = e.GetPosition(_canvas);
                _panView = (_frameMin, _frameMax);
                _canvas.CaptureMouse();
            };
            _canvas.MouseUp += (_, e) => { if (e.ChangedButton == MouseButton.Middle) { _isPanning = false; _canvas.ReleaseMouseCapture(); } };
            _canvas.MouseWheel += OnWheel;
            PreviewKeyDown += OnKey;
            Loaded += (_, _) => { FrameAll(); Focus(); };
        }

        private static Brush Freeze(Brush b) { b.Freeze(); return b; }

        private static Button MakeButton(string text, RoutedEventHandler click, string? tip = null)
        {
            var b = new Button { Content = text, Margin = new Thickness(0, 0, 4, 4), Padding = new Thickness(8, 2, 8, 2), ToolTip = tip };
            b.Click += click;
            return b;
        }

        // ---------------- geometry ----------------

        private double FrameRate => Math.Max(1, _timeline().FrameRate);
        private double TimelineWidth => Math.Max(1, _canvas.ActualWidth - HeaderWidth);
        private double FrameToX(double frame) => HeaderWidth + (frame - _frameMin) / (_frameMax - _frameMin) * TimelineWidth;
        private double XToFrame(double x) => _frameMin + (x - HeaderWidth) / TimelineWidth * (_frameMax - _frameMin);

        /// <summary>Row 0 is the active action at the top; then tracks from top (last) to bottom (first).</summary>
        private double RowY(int row) => RulerHeight + row * RowHeight;
        private int TrackIndexForRow(int row) => _timeline().NlaTracks.Count - row;   // row 1 = top track
        private int RowForTrack(int index) => _timeline().NlaTracks.Count - index;

        private NlaStrip? SelectedStrip =>
            _selTrack >= 0 && _selTrack < _timeline().NlaTracks.Count && _selStrip >= 0 && _selStrip < _timeline().NlaTracks[_selTrack].Strips.Count
                ? _timeline().NlaTracks[_selTrack].Strips[_selStrip] : null;

        private int SelectedTrackIndex => SelectedStrip is not null ? _selTrack : _selectedTrackRow;

        // ---------------- drawing ----------------

        public void Redraw()
        {
            _canvas.Children.Clear();
            var t = _timeline();
            var w = _canvas.ActualWidth;
            var h = _canvas.ActualHeight;
            if (w <= HeaderWidth || h <= RulerHeight) return;
            var fps = FrameRate;

            // grid + ruler
            var step = NiceStep((_frameMax - _frameMin) / Math.Max(1, TimelineWidth / 70));
            for (var f = Math.Ceiling(_frameMin / step) * step; f <= _frameMax; f += step)
            {
                var x = FrameToX(f);
                _canvas.Children.Add(new Line { X1 = x, X2 = x, Y1 = RulerHeight, Y2 = h, Stroke = GridBrush });
            }

            var rows = t.NlaTracks.Count + 1;
            for (var row = 0; row < rows; row++)
            {
                var y = RowY(row);
                var isAction = row == 0;
                var trackIndex = TrackIndexForRow(row);
                var track = isAction ? null : t.NlaTracks[trackIndex];
                var band = new Rectangle { Width = w - HeaderWidth, Height = RowHeight - 2, Fill = isAction ? ActionRowBrush : RowBrush, Opacity = 0.55 };
                Canvas.SetLeft(band, HeaderWidth); Canvas.SetTop(band, y + 1);
                _canvas.Children.Add(band);

                var header = new Border
                {
                    Width = HeaderWidth - 2,
                    Height = RowHeight - 2,
                    Background = isAction ? ActionRowBrush : RowBrush,
                    BorderBrush = !isAction && SelectedTrackIndex == trackIndex ? SelectedBorder : null,
                    BorderThickness = new Thickness(1),
                    Child = new TextBlock
                    {
                        Text = isAction ? "▶ " + t.ActiveAction.Name : (track!.Muted ? "◌ " : "● ") + track.Name,
                        Foreground = track?.Muted == true ? DimTextBrush : TextBrush,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(6, 0, 4, 0),
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        ToolTip = isAction ? "Active action (plays on top of the NLA; keyframes go here)" : null,
                    },
                };
                Canvas.SetLeft(header, 0); Canvas.SetTop(header, y + 1);
                _canvas.Children.Add(header);

                if (isAction)
                {
                    var probe = new NlaStrip(t.ActiveAction);
                    var (first, last) = probe.ActionRange;
                    if (last > first || t.ActiveAction.Tracks.Any(tr => !tr.IsEmpty))
                        DrawBlock(first * fps, Math.Max(last * fps, first * fps + 1), y, t.ActiveAction.Name, ActionRowBrush, SelectedBorder, 0.5, false);
                    continue;
                }

                for (var si = 0; si < track!.Strips.Count; si++)
                {
                    var strip = track.Strips[si];
                    var selected = trackIndex == _selTrack && si == _selStrip;
                    var fill = strip.Muted || track.Muted ? MutedStripBrush : strip.BlendMode == NlaBlendMode.Add ? AddStripBrush : StripBrush;
                    if (strip.Extrapolation != NlaExtrapolation.Nothing && !strip.Muted)
                    {
                        // Faint band where the strip holds its pose.
                        var next = track.Strips.Where(o => o != strip && o.Start >= strip.End).Select(o => o.Start).DefaultIfEmpty(_frameMax / fps).Min();
                        DrawHold(strip.End * fps, next * fps, y);
                        if (strip.Extrapolation == NlaExtrapolation.Hold) DrawHold(_frameMin, strip.Start * fps, y);
                    }
                    var label = $"{strip.Action.Name}" + (strip.Repeat != 1 ? $" ×{strip.Repeat:0.##}" : "") + (strip.Influence < 1 ? $" {strip.Influence * 100:0}%" : "");
                    DrawBlock(strip.Start * fps, Math.Max(strip.End * fps, strip.Start * fps + 1), y, label, fill, selected ? SelectedBorder : null, 1, strip.Muted);
                }
            }

            // ruler on top
            var ruler = new Rectangle { Width = w, Height = RulerHeight, Fill = PanelBrush };
            _canvas.Children.Add(ruler);
            for (var f = Math.Ceiling(_frameMin / step) * step; f <= _frameMax; f += step)
            {
                var label = new TextBlock { Text = Math.Abs(f) < 1e-9 ? "0" : f.ToString("0", CultureInfo.InvariantCulture), Foreground = TextBrush, FontSize = 11 };
                Canvas.SetLeft(label, FrameToX(f) + 2); Canvas.SetTop(label, 3);
                _canvas.Children.Add(label);
            }
            var cover = new Rectangle { Width = HeaderWidth, Height = RulerHeight, Fill = PanelBrush };
            _canvas.Children.Add(cover);

            var playX = FrameToX(t.CurrentFrame);
            if (playX >= HeaderWidth)
                _canvas.Children.Add(new Line { X1 = playX, X2 = playX, Y1 = 0, Y2 = h, Stroke = PlayheadBrush, StrokeThickness = 2 });

            _status.Text = $"{t.NlaTracks.Count} track(s)  frame {t.CurrentFrame:0}";
            RefreshProperties();
        }

        private void DrawBlock(double f0, double f1, double y, string label, Brush fill, Brush? border, double opacity, bool muted)
        {
            var x0 = Math.Max(HeaderWidth, FrameToX(f0));
            var x1 = Math.Min(_canvas.ActualWidth, FrameToX(f1));
            if (x1 <= x0) return;
            var block = new Border
            {
                Width = x1 - x0,
                Height = RowHeight - 8,
                Background = fill,
                Opacity = opacity,
                CornerRadius = new CornerRadius(3),
                BorderBrush = border,
                BorderThickness = new Thickness(border is null ? 0 : 2),
                Child = new TextBlock
                {
                    Text = label,
                    Foreground = muted ? DimTextBrush : Brushes.White,
                    Margin = new Thickness(5, 0, 3, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                },
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(block, x0); Canvas.SetTop(block, y + 4);
            _canvas.Children.Add(block);
        }

        private void DrawHold(double f0, double f1, double y)
        {
            var x0 = Math.Max(HeaderWidth, FrameToX(f0));
            var x1 = Math.Min(_canvas.ActualWidth, FrameToX(f1));
            if (x1 <= x0) return;
            var r = new Rectangle { Width = x1 - x0, Height = RowHeight - 14, Fill = HoldBrush, IsHitTestVisible = false };
            Canvas.SetLeft(r, x0); Canvas.SetTop(r, y + 7);
            _canvas.Children.Add(r);
        }

        private static double NiceStep(double raw)
        {
            if (raw <= 1) return 1;
            var pow = Math.Pow(10, Math.Floor(Math.Log10(raw)));
            foreach (var m in new[] { 1, 2, 5, 10 }) if (m * pow >= raw) return m * pow;
            return 10 * pow;
        }

        // ---------------- properties panel ----------------

        private void RefreshProperties()
        {
            _updatingProps = true;
            try
            {
                _props.Children.Clear();
                var t = _timeline();
                if (SelectedStrip is not { } strip)
                {
                    _props.Children.Add(new TextBlock
                    {
                        Text = "Select a strip to edit it.\n\nPush Down turns the active action into a layer; keys you add afterwards go into a new action that plays on top.",
                        Foreground = DimTextBrush,
                        TextWrapping = TextWrapping.Wrap,
                    });
                    return;
                }

                var fps = FrameRate;
                AddHeader("STRIP");
                var actionBox = new ComboBox { Margin = new Thickness(0, 0, 0, 6) };
                foreach (var a in t.Actions) actionBox.Items.Add(a);
                actionBox.DisplayMemberPath = nameof(AnimationAction.Name);
                actionBox.SelectedItem = strip.Action;
                actionBox.SelectionChanged += (_, _) =>
                {
                    if (_updatingProps || actionBox.SelectedItem is not AnimationAction a || a == strip.Action) return;
                    EditStrip("Strip Action", s => s.Action = a);
                };
                AddRow("Action", actionBox);
                AddNumber("Start (frame)", strip.Start * fps, v => EditStrip("Strip Start", s => s.Start = v / fps));
                AddNumber("Speed", strip.Speed, v => EditStrip("Strip Speed", s => s.Speed = v));
                AddNumber("Repeat", strip.Repeat, v => EditStrip("Strip Repeat", s => s.Repeat = v));
                AddNumber("Influence", strip.Influence, v => EditStrip("Strip Influence", s => s.Influence = (float)v));
                AddNumber("Blend In (frames)", strip.BlendIn * fps, v => EditStrip("Strip Blend In", s => s.BlendIn = Math.Max(0, v / fps)));
                AddNumber("Blend Out (frames)", strip.BlendOut * fps, v => EditStrip("Strip Blend Out", s => s.BlendOut = Math.Max(0, v / fps)));

                var blend = new ComboBox { Margin = new Thickness(0, 0, 0, 6), ItemsSource = Enum.GetValues<NlaBlendMode>(), SelectedItem = strip.BlendMode };
                blend.SelectionChanged += (_, _) => { if (!_updatingProps && blend.SelectedItem is NlaBlendMode m) EditStrip("Strip Blending", s => s.BlendMode = m); };
                AddRow("Blending", blend);
                var extrapolation = new ComboBox { Margin = new Thickness(0, 0, 0, 6), ItemsSource = Enum.GetValues<NlaExtrapolation>(), SelectedItem = strip.Extrapolation };
                extrapolation.SelectionChanged += (_, _) => { if (!_updatingProps && extrapolation.SelectedItem is NlaExtrapolation x) EditStrip("Strip Extrapolation", s => s.Extrapolation = x); };
                AddRow("Extrapolation", extrapolation);
                var mute = new CheckBox { Content = "Muted", IsChecked = strip.Muted, Foreground = TextBrush, Margin = new Thickness(0, 4, 0, 4) };
                mute.Click += (_, _) => EditStrip("Mute Strip", s => s.Muted = mute.IsChecked == true);
                _props.Children.Add(mute);
                _props.Children.Add(new TextBlock
                {
                    Text = $"Length {strip.Length * fps:0.#} frames ({strip.Start * fps:0.#} – {strip.End * fps:0.#})",
                    Foreground = DimTextBrush,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 4, 0, 0),
                });
            }
            finally
            {
                _updatingProps = false;
            }
        }

        private void AddHeader(string text) =>
            _props.Children.Add(new TextBlock { Text = text, Foreground = DimTextBrush, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });

        private void AddRow(string label, FrameworkElement editor)
        {
            _props.Children.Add(new TextBlock { Text = label, Foreground = TextBrush, Margin = new Thickness(0, 2, 0, 1) });
            _props.Children.Add(editor);
        }

        private void AddNumber(string label, double value, Action<double> apply)
        {
            var box = new TextBox { Text = value.ToString("0.###", CultureInfo.InvariantCulture), Margin = new Thickness(0, 0, 0, 6) };
            void Commit()
            {
                if (_updatingProps) return;
                if (!double.TryParse(box.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) || !double.IsFinite(v))
                {
                    box.Text = value.ToString("0.###", CultureInfo.InvariantCulture);
                    return;
                }
                if (Math.Abs(v - value) < 1e-9) return;
                apply(v);
            }
            box.LostKeyboardFocus += (_, _) => Commit();
            box.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Commit(); e.Handled = true; } };
            AddRow(label, box);
        }

        // ---------------- edits (each one undo step) ----------------

        private bool Commit(string description, Func<AnimationTimeline, bool> edit)
        {
            var command = NlaStateCommand.Capture(_timeline, description, edit, AfterUndoRedo);
            if (command is null) return false;
            _record(command);
            _changed();
            Redraw();
            return true;
        }

        private void AfterUndoRedo()
        {
            _changed();
            Redraw();
        }

        private void EditStrip(string description, Action<NlaStrip> edit)
        {
            int ti = _selTrack, si = _selStrip;
            Commit(description, t =>
            {
                if (ti < 0 || ti >= t.NlaTracks.Count || si < 0 || si >= t.NlaTracks[ti].Strips.Count) return false;
                edit(t.NlaTracks[ti].Strips[si]);
                return true;
            });
        }

        private void PushDown()
        {
            var ok = Commit("Push Down Action", t => t.PushDownActiveAction() is not null);
            if (!ok) { _status.Text = "The active action has no keys to push down."; return; }
            _selTrack = _timeline().NlaTracks.Count - 1;
            _selStrip = 0;
            Redraw();
        }

        private void AddTrack()
        {
            var above = SelectedTrackIndex;
            Commit("Add NLA Track", t =>
            {
                var index = above >= 0 ? above + 1 : t.NlaTracks.Count;
                t.NlaTracks.Insert(index, new NlaTrack(t.UniqueNlaTrackName()));
                _selectedTrackRow = index;
                _selTrack = _selStrip = -1;
                return true;
            });
        }

        private void AddStrip()
        {
            var t0 = _timeline();
            if (t0.ActiveAction.Tracks.All(tr => tr.IsEmpty)) { _status.Text = "The active action has no keys."; return; }
            var trackIndex = SelectedTrackIndex;
            Commit("Add Action Strip", t =>
            {
                var strip = new NlaStrip(t.ActiveAction) { Start = t.CurrentTime };
                NlaTrack track;
                if (trackIndex >= 0 && trackIndex < t.NlaTracks.Count && !t.NlaTracks[trackIndex].Strips.Any(s => s.Start < strip.Start + strip.Length && strip.Start < s.End))
                    track = t.NlaTracks[trackIndex];
                else
                {
                    // Occupied (or nothing chosen): a new track on top, like Blender does.
                    track = new NlaTrack(t.UniqueNlaTrackName());
                    t.NlaTracks.Add(track);
                }
                track.Strips.Add(strip);
                _selTrack = t.NlaTracks.IndexOf(track);
                _selStrip = track.Strips.Count - 1;
                return true;
            });
        }

        private void DeleteSelection()
        {
            if (SelectedStrip is not null)
            {
                int ti = _selTrack, si = _selStrip;
                Commit("Delete Strip", t => { t.NlaTracks[ti].Strips.RemoveAt(si); return true; });
                _selStrip = -1;
                _selectedTrackRow = ti;
                _selTrack = -1;
            }
            else if (_selectedTrackRow >= 0 && _selectedTrackRow < _timeline().NlaTracks.Count)
            {
                var ti = _selectedTrackRow;
                Commit("Delete NLA Track", t => { t.NlaTracks.RemoveAt(ti); return true; });
                _selectedTrackRow = -1;
            }
            Redraw();
        }

        private void ToggleMuteTrack()
        {
            var ti = SelectedTrackIndex;
            if (ti < 0 || ti >= _timeline().NlaTracks.Count) { _status.Text = "Select a track first."; return; }
            Commit("Mute NLA Track", t => { t.NlaTracks[ti].Muted = !t.NlaTracks[ti].Muted; return true; });
        }

        private void MoveTrack(int delta)
        {
            var ti = SelectedTrackIndex;
            var t0 = _timeline();
            var target = ti + delta;
            if (ti < 0 || ti >= t0.NlaTracks.Count || target < 0 || target >= t0.NlaTracks.Count) return;
            var hadStrip = SelectedStrip is not null;
            Commit("Move NLA Track", t =>
            {
                (t.NlaTracks[ti], t.NlaTracks[target]) = (t.NlaTracks[target], t.NlaTracks[ti]);
                return true;
            });
            if (hadStrip) _selTrack = target; else _selectedTrackRow = target;
            Redraw();
        }

        private void Bake()
        {
            var t0 = _timeline();
            if (!t0.HasNla) { _status.Text = "Nothing to bake - the NLA is empty or muted."; return; }
            Commit("Bake NLA", t =>
            {
                var baked = t.BakeNla(t.UniqueActionName("NLA Bake"));
                t.RestoreActions(t.Actions.Append(baked), t.ActiveAction);
                return true;
            });
            _status.Text = "Baked into a new action (pick it in the Action list; mute the NLA tracks to preview it alone).";
        }

        private void FrameAll()
        {
            var t = _timeline();
            double min = 0, max = t.TotalFrames;
            foreach (var strip in t.NlaTracks.SelectMany(tr => tr.Strips))
            {
                min = Math.Min(min, strip.Start * FrameRate);
                max = Math.Max(max, strip.End * FrameRate);
            }
            var pad = Math.Max(2, (max - min) * 0.04);
            _frameMin = min - pad;
            _frameMax = max + pad;
            Redraw();
        }

        // ---------------- mouse & keys ----------------

        private (int Track, int Strip) HitTest(Point p)
        {
            var t = _timeline();
            var row = (int)Math.Floor((p.Y - RulerHeight) / RowHeight);
            if (row < 1 || row > t.NlaTracks.Count) return (-1, -1);
            var ti = TrackIndexForRow(row);
            if (p.X < HeaderWidth) return (ti, -1);
            var frame = XToFrame(p.X);
            var track = t.NlaTracks[ti];
            for (var si = track.Strips.Count - 1; si >= 0; si--)
            {
                var s = track.Strips[si];
                var f0 = s.Start * FrameRate;
                var f1 = Math.Max(s.End * FrameRate, f0 + 1);
                if (frame >= f0 && frame <= f1) return (ti, si);
            }
            return (ti, -1);
        }

        private void OnMouseLeftDown(object sender, MouseButtonEventArgs e)
        {
            Focus();
            var p = e.GetPosition(_canvas);
            if (p.Y < RulerHeight && p.X >= HeaderWidth)
            {
                _isScrubbing = true;
                _canvas.CaptureMouse();
                _setTime(Math.Max(0, Math.Round(XToFrame(p.X))) / FrameRate);
                Redraw();
                return;
            }

            var (ti, si) = HitTest(p);
            if (si >= 0)
            {
                _selTrack = ti;
                _selStrip = si;
                _selectedTrackRow = -1;
                var strip = SelectedStrip!;
                _dragOffsetFrames = XToFrame(p.X) - strip.Start * FrameRate;
                _dragBefore = _timeline().CloneNla();
                _dragging = true;
                _canvas.CaptureMouse();
            }
            else
            {
                _selTrack = _selStrip = -1;
                _selectedTrackRow = ti;
            }
            Redraw();
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            var p = e.GetPosition(_canvas);
            if (_isPanning)
            {
                var span = _panView.Max - _panView.Min;
                var df = (p.X - _panStart.X) / TimelineWidth * span;
                _frameMin = _panView.Min - df;
                _frameMax = _panView.Max - df;
                Redraw();
                return;
            }
            if (_isScrubbing)
            {
                _setTime(Math.Max(0, Math.Round(XToFrame(p.X))) / FrameRate);
                Redraw();
                return;
            }
            if (_dragging && SelectedStrip is { } strip)
            {
                // Live preview; the undo step is made on release.
                strip.Start = Math.Round(XToFrame(p.X) - _dragOffsetFrames) / FrameRate;
                _changed();
                Redraw();
            }
        }

        private void OnMouseLeftUp(object sender, MouseButtonEventArgs e)
        {
            _canvas.ReleaseMouseCapture();
            _isScrubbing = false;
            if (!_dragging) return;
            _dragging = false;
            if (_dragBefore is null || SelectedStrip is not { } strip) return;
            var finalStart = strip.Start;
            int ti = _selTrack, si = _selStrip;
            _timeline().RestoreNla(_dragBefore);
            _dragBefore = null;
            Commit("Move Strip", t =>
            {
                var s = t.NlaTracks[ti].Strips[si];
                if (Math.Abs(s.Start - finalStart) < 1e-9) return false;
                s.Start = finalStart;
                return true;
            });
            _changed();
            Redraw();
        }

        private void OnWheel(object sender, MouseWheelEventArgs e)
        {
            var p = e.GetPosition(_canvas);
            var pivot = XToFrame(Math.Max(HeaderWidth, p.X));
            var factor = e.Delta > 0 ? 0.85 : 1 / 0.85;
            _frameMin = pivot - (pivot - _frameMin) * factor;
            _frameMax = pivot + (_frameMax - pivot) * factor;
            if (_frameMax - _frameMin < 5) _frameMax = _frameMin + 5;
            Redraw();
            e.Handled = true;
        }

        private void OnKey(object sender, KeyEventArgs e)
        {
            if (e.OriginalSource is TextBox) return;
            if (Keyboard.Modifiers == ModifierKeys.Control && (e.Key == Key.Z || e.Key == Key.Y) && Owner is IInputElement owner)
            {
                // Undo lives in the main window's history.
                (e.Key == Key.Z ? ApplicationCommands.Undo : ApplicationCommands.Redo).Execute(null, owner);
                Redraw();
                e.Handled = true;
                return;
            }
            switch (e.Key)
            {
                case Key.F: FrameAll(); e.Handled = true; break;
                case Key.X:
                case Key.Delete: DeleteSelection(); e.Handled = true; break;
            }
        }
    }
}
