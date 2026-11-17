using System.IO;
using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Path = System.Windows.Shapes.Path;
using JolieCat3D.Core.Geometry;
using JolieCat3D.Core.Scene;
using Point = System.Windows.Point;

namespace JolieCat3D.UI
{
    /// <summary>
    /// Blender-style UV Editor for the active mesh. V grows downwards, exactly as the DirectX
    /// renderer samples textures, so the layout lines up with the image underneath.
    /// Click / box select (Shift adds, Ctrl removes), A / Alt+A, L = islands, G / R / S
    /// move / rotate / scale with the mouse (X / Y constrain), Pack Islands, Unwrap.
    /// Every gesture is one undo step.
    /// </summary>
    public sealed class UVEditorWindow : Window
    {
        private static readonly Brush EdgeBrush = Freeze(new SolidColorBrush(Color.FromArgb(0xE0, 0x20, 0x20, 0x20)));
        private static readonly Brush SelectedBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xF0, 0xA0, 0x30)));
        private static readonly Brush FaceFillBrush = Freeze(new SolidColorBrush(Color.FromArgb(0x50, 0xF0, 0xA0, 0x30)));
        private static readonly Brush TextBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xC8, 0xC0, 0xB8)));

        private readonly Func<Node?> _target;
        private readonly Func<IEnumerable<int>> _sceneSelection;
        private readonly Action<Node, Mesh, string> _commit;
        private readonly Action _refresh;

        private readonly Canvas _canvas = new() { ClipToBounds = true, Background = Freeze(new SolidColorBrush(Color.FromRgb(0x1E, 0x1B, 0x1A))) };
        private readonly TextBlock _status = new() { Foreground = TextBrush, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        private readonly HashSet<int> _selection = new();

        // view: UV (0,0) is drawn at _origin, one UV unit is _scale pixels
        private Point _origin = new(40, 40);
        private double _scale = 400;
        private bool _viewInitialised;

        // gestures
        private enum Modal { None, Grab, Rotate, Scale }
        private Modal _modal;
        private int _axis = -1; // -1 free, 0 = U, 1 = V
        private Point _modalStart;
        private Dictionary<int, Vector2>? _originalUvs;
        private Mesh? _before;
        private Point? _boxStart;
        private bool _isPanning;
        private Point _panStart;
        private Point _panOrigin;
        private readonly Rectangle _box = new() { Stroke = Brushes.White, StrokeDashArray = new DoubleCollection { 4, 3 }, Fill = Freeze(new SolidColorBrush(Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF))), IsHitTestVisible = false };

        public UVEditorWindow(Func<Node?> target, Func<IEnumerable<int>> sceneSelection, Action<Node, Mesh, string> commit, Action refresh)
        {
            _target = target;
            _sceneSelection = sceneSelection;
            _commit = commit;
            _refresh = refresh;

            Title = "UV Editor - JolieCat3D";
            Width = 760;
            Height = 760;
            Background = Freeze(new SolidColorBrush(Color.FromRgb(0x26, 0x22, 0x21)));
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;

            var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4) };
            toolbar.Children.Add(MakeButton("Unwrap", (_, _) => Unwrap()));
            toolbar.Children.Add(MakeButton("Smart UV Project", (_, _) => SmartProject()));
            toolbar.Children.Add(MakeButton("Pack Islands", (_, _) => Pack()));
            toolbar.Children.Add(MakeButton("From 3D Selection", (_, _) => { SyncFromScene(); Redraw(); }));
            toolbar.Children.Add(MakeButton("Frame (Home)", (_, _) => { FitView(); Redraw(); }));
            toolbar.Children.Add(_status);

            var root = new DockPanel();
            DockPanel.SetDock(toolbar, Dock.Top);
            root.Children.Add(toolbar);
            root.Children.Add(_canvas);
            Content = root;

            _canvas.SizeChanged += (_, _) => { if (!_viewInitialised) FitView(); Redraw(); };
            _canvas.MouseLeftButtonDown += OnLeftDown;
            _canvas.MouseMove += OnMove;
            _canvas.MouseLeftButtonUp += OnLeftUp;
            _canvas.MouseDown += OnOtherDown;
            _canvas.MouseUp += (_, e) => { if (e.ChangedButton == MouseButton.Middle) { _isPanning = false; _canvas.ReleaseMouseCapture(); } };
            _canvas.MouseWheel += OnWheel;
            PreviewKeyDown += OnKey;
            Loaded += (_, _) => { SyncFromScene(); Redraw(); Focus(); };
        }

        private static Brush Freeze(Brush b) { b.Freeze(); return b; }

        private static Button MakeButton(string text, RoutedEventHandler click)
        {
            var b = new Button { Content = text, Margin = new Thickness(0, 0, 4, 0), Padding = new Thickness(8, 2, 8, 2) };
            b.Click += click;
            return b;
        }

        private Mesh? Mesh => _target()?.Mesh;

        // ---------------- coordinates ----------------

        private Point ToScreen(Vector2 uv) => new(_origin.X + uv.X * _scale, _origin.Y + uv.Y * _scale);
        private Vector2 ToUv(Point p) => new((float)((p.X - _origin.X) / _scale), (float)((p.Y - _origin.Y) / _scale));

        private void FitView()
        {
            var size = Math.Max(50, Math.Min(_canvas.ActualWidth, _canvas.ActualHeight) - 40);
            _scale = size;
            _origin = new Point((_canvas.ActualWidth - size) / 2, (_canvas.ActualHeight - size) / 2);
            _viewInitialised = _canvas.ActualWidth > 0;
        }

        // ---------------- drawing ----------------

        /// <summary>Redraws everything; call after the mesh or selection changed elsewhere.</summary>
        public void Redraw()
        {
            if (!IsLoaded) return;
            _canvas.Children.Clear();
            var node = _target();
            var mesh = node?.Mesh;
            var p0 = ToScreen(Vector2.Zero);
            var tile = new Rectangle { Width = _scale, Height = _scale, Fill = TileBrush(mesh) };
            Canvas.SetLeft(tile, p0.X);
            Canvas.SetTop(tile, p0.Y);
            _canvas.Children.Add(tile);
            _canvas.Children.Add(new Rectangle { Width = _scale, Height = _scale, Stroke = TextBrush, StrokeThickness = 1, Opacity = 0.5, RenderTransform = new TranslateTransform(p0.X, p0.Y) });

            if (mesh is null)
            {
                _status.Text = "Select a mesh object";
                return;
            }
            _selection.RemoveWhere(i => i >= mesh.Vertices.Count);

            var faces = mesh.GetRenderFaces().ToList();
            var geometry = new StreamGeometry();
            var selectedGeometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            using (var sel = selectedGeometry.Open())
            {
                foreach (var f in faces)
                {
                    var a = ToScreen(mesh.Vertices[f.A].UV);
                    var b = ToScreen(mesh.Vertices[f.B].UV);
                    var c = ToScreen(mesh.Vertices[f.C].UV);
                    ctx.BeginFigure(a, false, true);
                    ctx.LineTo(b, true, false);
                    ctx.LineTo(c, true, false);
                    if (_selection.Contains(f.A) && _selection.Contains(f.B) && _selection.Contains(f.C))
                    {
                        sel.BeginFigure(a, true, true);
                        sel.LineTo(b, false, false);
                        sel.LineTo(c, false, false);
                    }
                }
            }
            geometry.Freeze();
            selectedGeometry.FillRule = FillRule.Nonzero; // overlapping faces must not cancel out
            selectedGeometry.Freeze();
            _canvas.Children.Add(new Path { Data = selectedGeometry, Fill = FaceFillBrush });
            _canvas.Children.Add(new Path { Data = geometry, Stroke = EdgeBrush, StrokeThickness = 1 });

            var dots = new StreamGeometry();
            using (var ctx = dots.Open())
            {
                foreach (var i in _selection)
                {
                    var p = ToScreen(mesh.Vertices[i].UV);
                    ctx.BeginFigure(new Point(p.X - 2.5, p.Y - 2.5), true, true);
                    ctx.LineTo(new Point(p.X + 2.5, p.Y - 2.5), false, false);
                    ctx.LineTo(new Point(p.X + 2.5, p.Y + 2.5), false, false);
                    ctx.LineTo(new Point(p.X - 2.5, p.Y + 2.5), false, false);
                }
            }
            dots.Freeze();
            _canvas.Children.Add(new Path { Data = dots, Fill = SelectedBrush });

            if (_boxStart is not null) _canvas.Children.Add(_box);
            _status.Text = _modal != Modal.None
                ? $"{_modal}{(_axis == 0 ? " (U)" : _axis == 1 ? " (V)" : "")} - click/Enter to confirm, Esc/right click to cancel"
                : $"{node!.Name}: {faces.Count} triangles, {_selection.Count} UV vertices selected";
        }

        /// <summary>The material's diffuse texture, or a grey checker when there is none.</summary>
        private static Brush TileBrush(Mesh? mesh)
        {
            var path = mesh?.Material?.DiffuseTexturePath;
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                try
                {
                    var image = new BitmapImage();
                    image.BeginInit();
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.UriSource = new Uri(path);
                    image.EndInit();
                    image.Freeze();
                    return new ImageBrush(image) { Stretch = Stretch.Fill };
                }
                catch (Exception) { /* fall back to the checker */ }
            }

            var dark = Freeze(new SolidColorBrush(Color.FromRgb(0x46, 0x42, 0x40)));
            var light = Freeze(new SolidColorBrush(Color.FromRgb(0x5A, 0x55, 0x52)));
            var drawing = new DrawingGroup();
            drawing.Children.Add(new GeometryDrawing(dark, null, new RectangleGeometry(new Rect(0, 0, 2, 2))));
            drawing.Children.Add(new GeometryDrawing(light, null, new GeometryGroup { Children = { new RectangleGeometry(new Rect(0, 0, 1, 1)), new RectangleGeometry(new Rect(1, 1, 1, 1)) } }));
            var checker = new DrawingBrush(drawing) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 0.125, 0.125), ViewportUnits = BrushMappingMode.RelativeToBoundingBox };
            checker.Freeze();
            return checker;
        }

        // ---------------- selection ----------------

        private void SyncFromScene()
        {
            _selection.Clear();
            foreach (var i in _sceneSelection()) _selection.Add(i);
            if (_selection.Count == 0 && Mesh is { } mesh) foreach (var i in Enumerable.Range(0, mesh.Vertices.Count)) _selection.Add(i);
        }

        private int? VertexAt(Point p)
        {
            if (Mesh is not { } mesh) return null;
            int? best = null;
            var bestDistance = 8.0;
            for (var i = 0; i < mesh.Vertices.Count; i++)
            {
                var d = (ToScreen(mesh.Vertices[i].UV) - p).Length;
                if (d < bestDistance) { bestDistance = d; best = i; }
            }
            return best;
        }

        /// <summary>All vertices sharing a UV position with <paramref name="index"/> (one visible UV corner).</summary>
        private IEnumerable<int> SameUv(Mesh mesh, int index)
        {
            var uv = mesh.Vertices[index].UV;
            var key = MeshOperators.Key(mesh.Vertices[index].Position);
            for (var i = 0; i < mesh.Vertices.Count; i++)
                if (Vector2.DistanceSquared(mesh.Vertices[i].UV, uv) < 1e-12f && MeshOperators.Key(mesh.Vertices[i].Position) == key) yield return i;
        }

        // ---------------- mouse ----------------

        private void OnLeftDown(object sender, MouseButtonEventArgs e)
        {
            _canvas.Focus();
            var p = e.GetPosition(_canvas);
            if (_modal != Modal.None) { EndModal(confirm: true); return; }
            if (Mesh is not { } mesh) return;

            var mods = Keyboard.Modifiers;
            if (VertexAt(p) is { } hit)
            {
                var group = SameUv(mesh, hit).ToList();
                if (mods.HasFlag(ModifierKeys.Shift))
                {
                    if (_selection.Contains(hit)) foreach (var i in group) _selection.Remove(i);
                    else foreach (var i in group) _selection.Add(i);
                }
                else
                {
                    _selection.Clear();
                    foreach (var i in group) _selection.Add(i);
                }
                Redraw();
                return;
            }

            _boxStart = p;
            _canvas.CaptureMouse();
        }

        private void OnMove(object sender, MouseEventArgs e)
        {
            var p = e.GetPosition(_canvas);
            if (_isPanning)
            {
                _origin = new Point(_panOrigin.X + p.X - _panStart.X, _panOrigin.Y + p.Y - _panStart.Y);
                Redraw();
                return;
            }
            if (_modal != Modal.None) { UpdateModal(p); return; }
            if (_boxStart is { } start)
            {
                var r = new Rect(start, p);
                Canvas.SetLeft(_box, r.X); Canvas.SetTop(_box, r.Y);
                _box.Width = r.Width; _box.Height = r.Height;
                Redraw();
            }
        }

        private void OnLeftUp(object sender, MouseButtonEventArgs e)
        {
            if (_boxStart is not { } start) return;
            _boxStart = null;
            _canvas.ReleaseMouseCapture();
            if (Mesh is not { } mesh) return;
            var rect = new Rect(start, e.GetPosition(_canvas));
            var mods = Keyboard.Modifiers;
            if (rect.Width < 3 && rect.Height < 3)
            {
                if (!mods.HasFlag(ModifierKeys.Shift)) _selection.Clear();
            }
            else
            {
                if (!mods.HasFlag(ModifierKeys.Shift) && !mods.HasFlag(ModifierKeys.Control)) _selection.Clear();
                for (var i = 0; i < mesh.Vertices.Count; i++)
                {
                    if (!rect.Contains(ToScreen(mesh.Vertices[i].UV))) continue;
                    if (mods.HasFlag(ModifierKeys.Control)) _selection.Remove(i); else _selection.Add(i);
                }
            }
            Redraw();
        }

        private void OnOtherDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Right && _modal != Modal.None) { EndModal(confirm: false); e.Handled = true; return; }
            if (e.ChangedButton != MouseButton.Middle) return;
            _isPanning = true;
            _panStart = e.GetPosition(_canvas);
            _panOrigin = _origin;
            _canvas.CaptureMouse();
        }

        private void OnWheel(object sender, MouseWheelEventArgs e)
        {
            var p = e.GetPosition(_canvas);
            var uv = ToUv(p);
            _scale = Math.Clamp(_scale * Math.Pow(1.15, e.Delta / 120.0), 20, 20000);
            _origin = new Point(p.X - uv.X * _scale, p.Y - uv.Y * _scale);
            Redraw();
            e.Handled = true;
        }

        // ---------------- keyboard ----------------

        private void OnKey(object sender, KeyEventArgs e)
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            var mods = Keyboard.Modifiers;
            var mouse = Mouse.GetPosition(_canvas);

            if (_modal != Modal.None)
            {
                switch (key)
                {
                    case Key.Escape: EndModal(confirm: false); break;
                    case Key.Enter or Key.Space: EndModal(confirm: true); break;
                    case Key.X: _axis = _axis == 0 ? -1 : 0; UpdateModal(mouse); break;
                    case Key.Y: _axis = _axis == 1 ? -1 : 1; UpdateModal(mouse); break;
                    default: return;
                }
                e.Handled = true;
                return;
            }

            switch (key)
            {
                case Key.A when mods == ModifierKeys.None:
                    if (Mesh is { } m) foreach (var i in Enumerable.Range(0, m.Vertices.Count)) _selection.Add(i);
                    break;
                case Key.A when mods == ModifierKeys.Alt: _selection.Clear(); break;
                case Key.L when mods == ModifierKeys.None:
                    if (Mesh is { } mesh)
                    {
                        var seed = VertexAt(mouse) is { } under ? new[] { under } : _selection.ToArray();
                        foreach (var i in UvOperators.SelectIslands(mesh, seed)) _selection.Add(i);
                    }
                    break;
                case Key.G when mods == ModifierKeys.None: BeginModal(Modal.Grab, mouse); break;
                case Key.R when mods == ModifierKeys.None: BeginModal(Modal.Rotate, mouse); break;
                case Key.S when mods == ModifierKeys.None: BeginModal(Modal.Scale, mouse); break;
                case Key.Home: FitView(); break;
                default: return;
            }
            Redraw();
            e.Handled = true;
        }

        // ---------------- G / R / S ----------------

        private void BeginModal(Modal mode, Point mouse)
        {
            if (Mesh is not { } mesh || _selection.Count == 0) return;
            _modal = mode;
            _axis = -1;
            _modalStart = mouse;
            _before = mesh.Clone();
            _originalUvs = _selection.ToDictionary(i => i, i => mesh.Vertices[i].UV);
        }

        private Vector2 Pivot() => _originalUvs is { Count: > 0 } o ? o.Values.Aggregate(Vector2.Zero, (s, v) => s + v) / o.Count : Vector2.Zero;

        private void UpdateModal(Point mouse)
        {
            if (Mesh is not { } mesh || _originalUvs is null) return;
            var pivot = Pivot();
            var pivotScreen = ToScreen(pivot);
            Func<Vector2, Vector2> transform;
            switch (_modal)
            {
                case Modal.Grab:
                {
                    var delta = ToUv(mouse) - ToUv(_modalStart);
                    if (_axis == 0) delta.Y = 0;
                    if (_axis == 1) delta.X = 0;
                    if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) delta = new Vector2(MathF.Round(delta.X * 64) / 64, MathF.Round(delta.Y * 64) / 64);
                    transform = uv => uv + delta;
                    break;
                }
                case Modal.Rotate:
                {
                    var a0 = Math.Atan2(_modalStart.Y - pivotScreen.Y, _modalStart.X - pivotScreen.X);
                    var a1 = Math.Atan2(mouse.Y - pivotScreen.Y, mouse.X - pivotScreen.X);
                    var angle = (float)(a1 - a0);
                    if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) angle = MathF.Round(angle / (MathF.PI / 12)) * (MathF.PI / 12);
                    var (sin, cos) = MathF.SinCos(angle);
                    transform = uv => { var d = uv - pivot; return pivot + new Vector2(d.X * cos - d.Y * sin, d.X * sin + d.Y * cos); };
                    break;
                }
                default:
                {
                    var d0 = Math.Max(1, (_modalStart - pivotScreen).Length);
                    var factor = (float)((mouse - pivotScreen).Length / d0);
                    if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) factor = MathF.Round(factor * 10) / 10;
                    var f = new Vector2(_axis == 1 ? 1 : factor, _axis == 0 ? 1 : factor);
                    transform = uv => pivot + (uv - pivot) * f;
                    break;
                }
            }
            foreach (var (i, uv) in _originalUvs) mesh.SetVertexUV(i, transform(uv));
            _refresh();
            Redraw();
        }

        private void EndModal(bool confirm)
        {
            var node = _target();
            if (node?.Mesh is { } mesh && _originalUvs is not null && _before is not null)
            {
                if (confirm) _commit(node, _before, _modal switch { Modal.Grab => "Move UVs", Modal.Rotate => "Rotate UVs", _ => "Scale UVs" });
                else foreach (var (i, uv) in _originalUvs) mesh.SetVertexUV(i, uv);
            }
            _modal = Modal.None;
            _originalUvs = null;
            _before = null;
            _refresh();
            Redraw();
        }

        // ---------------- operators ----------------

        private void RunOperator(string name, Func<Mesh, bool> op)
        {
            var node = _target();
            if (node?.Mesh is not { } mesh) return;
            var before = mesh.Clone();
            if (!op(mesh)) return;
            _commit(node, before, name);
            _refresh();
            Redraw();
        }

        private void Pack() => RunOperator("Pack UV Islands", m => UvOperators.PackIslands(m, 0.01f) > 0);

        private void Unwrap() => RunOperator("Unwrap", m => m.Unwrap() > 0);

        private void SmartProject() => RunOperator("Smart UV Project", m => LscmUnwrapper.SmartProject(m) > 0);
    }
}
