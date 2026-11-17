using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Path = System.Windows.Shapes.Path;
using JolieCat3D.Core.Materials;
using JolieCat3D.Core.Materials.Nodes;
using JolieCat3D.Core.Scene;
using Microsoft.Win32;
using CoreColor4 = JolieCat3D.Core.Numerics.Color4;
using NVector4 = System.Numerics.Vector4;

namespace JolieCat3D.UI
{
    /// <summary>
    /// Blender-style Shader Editor for the selected object's material: drag from an output
    /// socket to an input socket to link, drag a linked input away to unlink, Shift+A /
    /// right-click adds nodes, X deletes, middle mouse pans, wheel zooms, Home frames all.
    /// "Apply" bakes the tree into the material's texture maps (base colour, metallic/
    /// roughness, normal, emission) so the viewport, F12 and every exporter use it.
    /// Every edit is one undo step in the main window's history.
    /// </summary>
    public sealed class ShaderEditorWindow : Window
    {
        private const double NodeWidth = 190;
        private const double SocketSize = 12;
        private const int PreviewSize = 64;

        private static readonly Brush Background1 = Freeze(new SolidColorBrush(Color.FromRgb(0x1D, 0x1B, 0x1A)));
        private static readonly Brush PanelBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x26, 0x22, 0x21)));
        private static readonly Brush NodeBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x30, 0x2C, 0x2A)));
        private static readonly Brush TextBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xD0, 0xC8, 0xC0)));
        private static readonly Brush DimTextBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x88, 0x80, 0x78)));
        private static readonly Brush SelectedBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xF0, 0xB0, 0x40)));
        private static readonly Brush LinkBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xB8, 0xB0, 0xA8)));
        private static readonly Brush FieldBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x22, 0x1F, 0x1E)));

        private static readonly Dictionary<string, Color> CategoryColors = new()
        {
            ["Output"] = Color.FromRgb(0x8A, 0x3A, 0x3A),
            ["Input"] = Color.FromRgb(0x8A, 0x3A, 0x52),
            ["Texture"] = Color.FromRgb(0x8A, 0x6A, 0x2A),
            ["Color"] = Color.FromRgb(0x6A, 0x6A, 0x2A),
            ["Vector"] = Color.FromRgb(0x3A, 0x3A, 0x8A),
            ["Converter"] = Color.FromRgb(0x2A, 0x6A, 0x8A),
        };

        private static readonly Dictionary<SocketKind, Brush> SocketBrushes = new()
        {
            [SocketKind.Float] = Freeze(new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xA0))),
            [SocketKind.Color] = Freeze(new SolidColorBrush(Color.FromRgb(0xC7, 0xC7, 0x29))),
            [SocketKind.Vector] = Freeze(new SolidColorBrush(Color.FromRgb(0x63, 0x63, 0xC7))),
        };

        private sealed record SocketRef(int NodeId, string Name, bool IsOutput, SocketKind Kind);

        private readonly Func<(Node Node, Material Material)?> _target;
        private readonly Action<Node, string, Action> _commit;
        private readonly Action<Node> _refreshNode;
        private readonly CheckBox _live = new() { Content = "Live preview", Margin = new Thickness(8, 0, 4, 4), VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "Re-bake at 256 px into temporary maps after every change so the model updates at once (Apply writes the final maps)" };
        private readonly System.Windows.Threading.DispatcherTimer _previewTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
        private bool _previewRunning, _previewAgain;
        private string _lastPreviewKey = string.Empty;

        private readonly Canvas _view = new() { ClipToBounds = true, Background = Background1 };
        private readonly Canvas _world = new();
        private readonly ScaleTransform _zoom = new(1, 1);
        private readonly TranslateTransform _pan = new(40, 40);
        private readonly TextBlock _status = new() { Foreground = TextBrush, Margin = new Thickness(8, 4, 8, 4), VerticalAlignment = VerticalAlignment.Center };
        private readonly ComboBox _resolution = new() { Width = 80, Margin = new Thickness(0, 0, 4, 4) };
        private readonly Button _applyButton;
        private readonly Border _empty;

        private readonly Dictionary<int, Border> _nodeVisuals = new();
        private readonly List<(Ellipse Ellipse, SocketRef Socket)> _sockets = new();
        private readonly List<(Path Path, ShaderLink Link)> _linkPaths = new();

        private int _selected = -1;
        private bool _isPanning;
        private Point _panStart, _panOrigin;

        // node drag
        private int _dragNode = -1;
        private Point _dragStart;
        private (float X, float Y) _dragNodeStart;

        // link drag
        private SocketRef? _linkFrom;
        private ShaderLink? _detached;
        private Path? _tempLink;
        private Point _lastWorldMouse;
        private bool _isBaking;

        public ShaderEditorWindow(Func<(Node Node, Material Material)?> target, Action<Node, string, Action> commit, Action<Node> refreshNode)
        {
            _target = target;
            _commit = commit;
            _refreshNode = refreshNode;
            _previewTimer.Tick += async (_, _) => { _previewTimer.Stop(); await RunPreviewAsync(); };
            _live.Foreground = TextBrush;
            _live.Checked += (_, _) => { _lastPreviewKey = string.Empty; SchedulePreview(); };

            Title = "Shader Editor - JolieCat3D";
            Width = 1100;
            Height = 620;
            Background = PanelBrush;
            Foreground = TextBrush;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            foreach (var size in new[] { 256, 512, 1024, 2048, 4096 }) _resolution.Items.Add(size);
            _resolution.SelectedItem = 1024;
            _resolution.SelectionChanged += (_, _) =>
            {
                if (_resolution.SelectedItem is int s && Graph is { } g && g.BakeSize != s) Edit("Bake Size", () => Graph!.BakeSize = s, rebuild: false);
            };

            var toolbar = new WrapPanel { Margin = new Thickness(4) };
            var addButton = MakeButton("Add ▾", (s, _) => ShowAddMenu((FrameworkElement)s, null));
            toolbar.Children.Add(addButton);
            toolbar.Children.Add(new TextBlock { Text = "Bake size:", Foreground = TextBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 4, 4) });
            toolbar.Children.Add(_resolution);
            _applyButton = MakeButton("Apply to Material (bake maps)", async (_, _) => await ApplyAsync());
            toolbar.Children.Add(_applyButton);
            toolbar.Children.Add(_live);
            toolbar.Children.Add(MakeButton("Frame All (Home)", (_, _) => FrameAll()));
            toolbar.Children.Add(MakeButton("Remove Node Tree", (_, _) => RemoveTree()));
            toolbar.Children.Add(_status);

            _world.RenderTransform = new TransformGroup { Children = { _zoom, _pan } };
            _view.Children.Add(_world);

            var useNodes = MakeButton("Use Nodes", (_, _) => CreateTree());
            useNodes.Padding = new Thickness(18, 6, 18, 6);
            _empty = new Border
            {
                Child = new StackPanel
                {
                    Children =
                    {
                        new TextBlock { Text = "This material has no node tree.", Foreground = TextBrush, Margin = new Thickness(0, 0, 0, 10), HorizontalAlignment = HorizontalAlignment.Center },
                        useNodes,
                    },
                },
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };

            var stage = new Grid();
            stage.Children.Add(_view);
            stage.Children.Add(_empty);

            var root = new DockPanel();
            DockPanel.SetDock(toolbar, Dock.Top);
            root.Children.Add(toolbar);
            root.Children.Add(stage);
            Content = root;

            _view.MouseDown += OnViewMouseDown;
            _view.MouseMove += OnViewMouseMove;
            _view.MouseUp += OnViewMouseUp;
            _view.MouseWheel += OnWheel;
            PreviewKeyDown += OnKey;
            Activated += (_, _) => Rebuild();
            Loaded += (_, _) => { Rebuild(); FrameAll(); };
        }

        private static Brush Freeze(Brush b) { b.Freeze(); return b; }

        private static Button MakeButton(string text, RoutedEventHandler click)
        {
            var b = new Button { Content = text, Margin = new Thickness(0, 0, 4, 4), Padding = new Thickness(8, 2, 8, 2) };
            b.Click += click;
            return b;
        }

        private Material? Material => _target()?.Material;
        private ShaderGraph? Graph => Material?.NodeGraph;

        // ======================= edits =======================

        /// <summary>One undo step on the selected object; rebuilds the canvas afterwards.</summary>
        private void Edit(string description, Action change, bool rebuild = true)
        {
            if (_target() is not { } t) return;
            _commit(t.Node, description, change);
            if (rebuild) Rebuild();
        }

        private void CreateTree()
        {
            if (Material is not { } m) return;
            Edit("Use Nodes", () =>
            {
                var graph = ShaderGraph.CreateDefault(m.DiffuseColor);
                var output = graph.Output!;
                output.Inputs["Roughness"] = new[] { m.Roughness };
                output.Inputs["Metallic"] = new[] { m.Metallic };
                output.Inputs["Emission"] = new[] { m.EmissiveColor.R, m.EmissiveColor.G, m.EmissiveColor.B, 1f };
                output.Inputs["Emission Strength"] = new[] { m.EmissiveStrength };
                output.Inputs["Alpha"] = new[] { m.Opacity };
                if (!string.IsNullOrEmpty(m.DiffuseTexturePath))
                {
                    var image = graph.AddNode(ShaderNodeTypes.Image, 200, 80);
                    image.Options["Image"] = m.DiffuseTexturePath!;
                    graph.Connect(image.Id, "Color", output.Id, "Base Color");
                }
                m.NodeGraph = graph;
            });
            FrameAll();
        }

        private void RemoveTree()
        {
            if (Material is not { NodeGraph: not null } m) return;
            if (MessageBox.Show(this, "Remove this material's node tree? Already applied texture maps stay on the material.", "Shader Editor",
                    MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
            Edit("Remove Node Tree", () => m.NodeGraph = null);
        }

        private void AddNode(string type, Point worldPosition)
        {
            if (Graph is null) return;
            var id = 0;
            Edit("Add " + ShaderNodeTypes.Get(type).Title, () => id = Graph!.AddNode(type, (float)worldPosition.X, (float)worldPosition.Y).Id);
            _selected = id;
            Rebuild();
        }

        private void DeleteSelected()
        {
            if (Graph?.Find(_selected) is not { } node) return;
            if (node.Type == ShaderNodeTypes.Output) { _status.Text = "The Material Output node can't be deleted."; return; }
            var id = node.Id;
            Edit("Delete Node", () => Graph!.RemoveNode(id));
            _selected = -1;
        }

        private void SetInput(int nodeId, string input, float[] value) =>
            Edit("Edit Node", () => { if (Graph?.Find(nodeId) is { } n) n.Inputs[input] = value; });

        private void SetOption(int nodeId, string option, string value) =>
            Edit("Edit Node", () => { if (Graph?.Find(nodeId) is { } n) n.Options[option] = value; });

        // ======================= drawing =======================

        public void Rebuild()
        {
            _world.Children.Clear();
            _nodeVisuals.Clear();
            _sockets.Clear();
            _linkPaths.Clear();
            _tempLink = null;

            var target = _target();
            var graph = target?.Material.NodeGraph;
            _empty.Visibility = target is not null && graph is null ? Visibility.Visible : Visibility.Collapsed;
            _view.Visibility = graph is null ? Visibility.Hidden : Visibility.Visible;
            if (target is null)
            {
                _status.Text = "Select a mesh object.";
                Title = "Shader Editor - JolieCat3D";
                _applyButton.IsEnabled = false;
                return;
            }
            Title = $"Shader Editor - {target.Value.Material.Name} ({target.Value.Node.Name})";
            _applyButton.IsEnabled = graph is not null && !_isBaking;
            if (graph is null) { _status.Text = string.Empty; return; }
            if (_resolution.SelectedItem is not int r || r != graph.BakeSize) _resolution.SelectedItem = _resolution.Items.Contains(graph.BakeSize) ? graph.BakeSize : 1024;
            SchedulePreview();

            Func<string, TextureBuffer?> loader = LoadImage;
            foreach (var node in graph.Nodes)
            {
                var visual = BuildNode(graph, node, loader);
                Canvas.SetLeft(visual, node.X);
                Canvas.SetTop(visual, node.Y);
                Panel.SetZIndex(visual, 1);
                _world.Children.Add(visual);
                _nodeVisuals[node.Id] = visual;
            }

            _world.UpdateLayout();
            foreach (var link in graph.Links)
            {
                var path = new Path { Stroke = LinkBrush, StrokeThickness = 2, IsHitTestVisible = false };
                Panel.SetZIndex(path, 0);
                _world.Children.Add(path);
                _linkPaths.Add((path, link));
            }
            UpdateLinks();
            if (!_isBaking) _status.Text = $"{graph.Nodes.Count} nodes, {graph.Links.Count} links" + (graph.BakePathPrefix is { } p ? $"  →  {System.IO.Path.GetFileName(p)}_*.png" : "");
        }

        private Point SocketCenter(SocketRef socket)
        {
            foreach (var (ellipse, s) in _sockets)
                if (s.NodeId == socket.NodeId && s.Name == socket.Name && s.IsOutput == socket.IsOutput && VisualTreeHelper.GetParent(ellipse) is not null)
                    return ellipse.TranslatePoint(new Point(SocketSize / 2, SocketSize / 2), _world);
            return new Point();
        }

        private void UpdateLinks()
        {
            foreach (var (path, link) in _linkPaths)
            {
                var from = SocketCenter(new SocketRef(link.FromNode, link.FromOutput, true, SocketKind.Float));
                var to = SocketCenter(new SocketRef(link.ToNode, link.ToInput, false, SocketKind.Float));
                path.Data = Curve(from, to);
            }
        }

        private static Geometry Curve(Point from, Point to)
        {
            var dx = Math.Max(40, Math.Abs(to.X - from.X) * 0.5);
            var figure = new PathFigure { StartPoint = from };
            figure.Segments.Add(new BezierSegment(new Point(from.X + dx, from.Y), new Point(to.X - dx, to.Y), to, true));
            var geometry = new PathGeometry();
            geometry.Figures.Add(figure);
            return geometry;
        }

        private Border BuildNode(ShaderGraph graph, ShaderNode node, Func<string, TextureBuffer?> loader)
        {
            var def = ShaderNodeTypes.Get(node.Type);
            var panel = new StackPanel();

            var header = new Border
            {
                Background = new SolidColorBrush(CategoryColors.TryGetValue(def.Category, out var c) ? c : Colors.DimGray),
                Padding = new Thickness(8, 3, 8, 3),
                CornerRadius = new CornerRadius(4, 4, 0, 0),
                Cursor = Cursors.SizeAll,
                Child = new TextBlock { Text = def.Title, Foreground = Brushes.White, FontWeight = FontWeights.SemiBold },
            };
            header.MouseLeftButtonDown += (_, e) =>
            {
                _selected = node.Id;
                _dragNode = node.Id;
                _dragStart = e.GetPosition(_world);
                _dragNodeStart = (node.X, node.Y);
                _view.CaptureMouse();
                HighlightSelection();
                e.Handled = true;
            };
            panel.Children.Add(header);

            // outputs
            foreach (var (name, kind) in def.Outputs)
            {
                var row = new Grid { Height = 22 };
                row.Children.Add(new TextBlock { Text = name, Foreground = TextBrush, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) });
                row.Children.Add(MakeSocket(new SocketRef(node.Id, name, true, kind), HorizontalAlignment.Right));
                panel.Children.Add(row);
            }

            // preview of the first output (Base Color for the output node)
            var previewSocket = node.Type == ShaderNodeTypes.Output ? "Base Color" : def.Outputs.FirstOrDefault().Name;
            if (previewSocket is not null && node.Type is not (ShaderNodeTypes.Value or ShaderNodeTypes.TexCoord))
            {
                try
                {
                    var pixels = ShaderGraphBaker.Preview(graph, node.Id, previewSocket, PreviewSize, loader);
                    panel.Children.Add(new Image { Source = ToBitmap(pixels, PreviewSize, node.Type == ShaderNodeTypes.Bump || node.Type == ShaderNodeTypes.Mapping), Width = PreviewSize * 1.6, Height = PreviewSize * 1.6, Margin = new Thickness(4), HorizontalAlignment = HorizontalAlignment.Center });
                }
                catch (Exception) { /* a broken preview must not stop the editor */ }
            }

            // options
            foreach (var option in def.Options)
            {
                if (option.Choices.Count == 0)
                {
                    var current = node.Option(option.Name);
                    var pick = new Button { Content = string.IsNullOrEmpty(current) ? "Open image..." : System.IO.Path.GetFileName(current), Margin = new Thickness(6, 2, 6, 2), ToolTip = current };
                    pick.Click += (_, _) =>
                    {
                        var dialog = new OpenFileDialog { Filter = "Images (*.png;*.jpg;*.jpeg;*.bmp;*.tga)|*.png;*.jpg;*.jpeg;*.bmp;*.tga", Title = "Image Texture" };
                        if (dialog.ShowDialog(this) == true) SetOption(node.Id, option.Name, dialog.FileName);
                    };
                    panel.Children.Add(pick);
                    continue;
                }
                var box = new ComboBox { Margin = new Thickness(6, 2, 6, 2), ItemsSource = option.Choices, SelectedItem = node.Option(option.Name), ToolTip = option.Name };
                box.SelectionChanged += (_, _) => { if (box.SelectedItem is string v && v != node.Option(option.Name)) SetOption(node.Id, option.Name, v); };
                panel.Children.Add(Labeled(option.Name, box));
            }

            if (node.Type == ShaderNodeTypes.ColorRamp) panel.Children.Add(BuildRampEditor(node));

            // inputs
            foreach (var input in def.Inputs)
            {
                var row = new Grid { MinHeight = 22, Margin = new Thickness(0, 1, 0, 1) };
                row.Children.Add(MakeSocket(new SocketRef(node.Id, input.Name, false, input.Kind), HorizontalAlignment.Left));
                var linked = graph.LinkInto(node.Id, input.Name) is not null;
                FrameworkElement content;
                if (linked || input.ImplicitUv && !node.Inputs.ContainsKey(input.Name) || node.Type == ShaderNodeTypes.Output && input.Name == "Normal")
                    content = new TextBlock { Text = input.Name, Foreground = linked ? TextBrush : DimTextBrush, VerticalAlignment = VerticalAlignment.Center };
                else if (input.Kind == SocketKind.Color)
                    content = ColorField(node, input);
                else if (input.Kind == SocketKind.Vector)
                    content = VectorField(node, input);
                else
                    content = FloatField(node, input);
                content.Margin = new Thickness(12, 0, 6, 0);
                row.Children.Add(content);
                panel.Children.Add(row);
            }

            var border = new Border
            {
                Width = NodeWidth,
                Background = NodeBrush,
                CornerRadius = new CornerRadius(5),
                BorderThickness = new Thickness(2),
                BorderBrush = node.Id == _selected ? SelectedBrush : Brushes.Transparent,
                Child = panel,
                Padding = new Thickness(0, 0, 0, 4),
                Tag = node.Id,
            };
            border.MouseLeftButtonDown += (_, e) =>
            {
                _selected = node.Id;
                HighlightSelection();
            };
            return border;
        }

        private void HighlightSelection()
        {
            foreach (var (id, visual) in _nodeVisuals)
            {
                visual.BorderBrush = id == _selected ? SelectedBrush : Brushes.Transparent;
                Panel.SetZIndex(visual, id == _selected ? 2 : 1);
            }
        }

        private static FrameworkElement Labeled(string label, FrameworkElement editor)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            var text = new TextBlock { Text = label, Foreground = DimTextBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
            grid.Children.Add(text);
            Grid.SetColumn(editor, 1);
            grid.Children.Add(editor);
            return grid;
        }

        private Ellipse MakeSocket(SocketRef socket, HorizontalAlignment side)
        {
            var ellipse = new Ellipse
            {
                Width = SocketSize,
                Height = SocketSize,
                Fill = SocketBrushes[socket.Kind],
                Stroke = Brushes.Black,
                StrokeThickness = 1,
                HorizontalAlignment = side,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = side == HorizontalAlignment.Left ? new Thickness(-SocketSize / 2 - 2, 0, 0, 0) : new Thickness(0, 0, -SocketSize / 2 - 2, 0),
                Cursor = Cursors.Cross,
                ToolTip = socket.Name,
            };
            ellipse.MouseLeftButtonDown += (_, e) =>
            {
                BeginLinkDrag(socket);
                e.Handled = true;
            };
            _sockets.Add((ellipse, socket));
            return ellipse;
        }

        private FrameworkElement FloatField(ShaderNode node, ShaderSocket input)
        {
            var value = node.InputValue(input.Name).X;
            var box = new TextBox { Text = value.ToString("0.###", CultureInfo.InvariantCulture), Background = FieldBrush, Foreground = TextBrush, BorderThickness = new Thickness(0), Padding = new Thickness(3, 1, 3, 1) };
            void Commit()
            {
                if (!float.TryParse(box.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) || !float.IsFinite(v))
                {
                    box.Text = value.ToString("0.###", CultureInfo.InvariantCulture);
                    return;
                }
                v = Math.Clamp(v, input.Min, input.Max);
                if (Math.Abs(v - value) > 1e-7f) SetInput(node.Id, input.Name, new[] { v });
            }
            box.LostKeyboardFocus += (_, _) => Commit();
            box.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Commit(); e.Handled = true; } };
            return Labeled(input.Name, box);
        }

        private FrameworkElement VectorField(ShaderNode node, ShaderSocket input)
        {
            var v = node.InputValue(input.Name);
            string Format(NVector4 x) => string.Join(", ", new[] { x.X, x.Y, x.Z }.Select(f => f.ToString("0.###", CultureInfo.InvariantCulture)));
            var box = new TextBox { Text = Format(v), Background = FieldBrush, Foreground = TextBrush, BorderThickness = new Thickness(0), Padding = new Thickness(3, 1, 3, 1), ToolTip = "x, y, z" };
            void Commit()
            {
                var parts = box.Text.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                var values = new float[3];
                for (var i = 0; i < 3; i++)
                {
                    if (i >= parts.Length || !float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]) || !float.IsFinite(values[i]))
                    {
                        box.Text = Format(v);
                        return;
                    }
                }
                if (values[0] != v.X || values[1] != v.Y || values[2] != v.Z) SetInput(node.Id, input.Name, new[] { values[0], values[1], values[2], 1f });
            }
            box.LostKeyboardFocus += (_, _) => Commit();
            box.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Commit(); e.Handled = true; } };
            return Labeled(input.Name, box);
        }

        private FrameworkElement ColorField(ShaderNode node, ShaderSocket input)
        {
            var v = node.InputValue(input.Name);
            var swatch = MakeSwatch(v, color => SetInput(node.Id, input.Name, new[] { color.X, color.Y, color.Z, color.W }));
            return Labeled(input.Name, swatch);
        }

        /// <summary>A colour swatch that opens R/G/B sliders and a hex box; <paramref name="commit"/>
        /// runs once when the popup closes with a changed colour.</summary>
        private Border MakeSwatch(NVector4 value, Action<NVector4> commit)
        {
            var swatch = new Border
            {
                Height = 18,
                Background = new SolidColorBrush(ToColor(value)),
                BorderBrush = Brushes.Black,
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                ToolTip = Hex(value),
            };
            swatch.MouseLeftButtonDown += (_, e) =>
            {
                e.Handled = true;
                var current = value;
                var preview = new Border { Height = 24, Background = new SolidColorBrush(ToColor(current)), Margin = new Thickness(0, 0, 0, 6) };
                var hex = new TextBox { Text = Hex(current), Margin = new Thickness(0, 6, 0, 0) };
                var sliders = new Slider[3];
                var updating = false;
                void Sync()
                {
                    updating = true;
                    preview.Background = new SolidColorBrush(ToColor(current));
                    hex.Text = Hex(current);
                    sliders[0].Value = current.X; sliders[1].Value = current.Y; sliders[2].Value = current.Z;
                    updating = false;
                }
                var stack = new StackPanel { Margin = new Thickness(10), Width = 200 };
                stack.Children.Add(preview);
                var names = new[] { "R", "G", "B" };
                for (var i = 0; i < 3; i++)
                {
                    var channel = i;
                    var slider = new Slider { Minimum = 0, Maximum = 1, Value = i == 0 ? current.X : i == 1 ? current.Y : current.Z, Width = 170 };
                    slider.ValueChanged += (_, _) =>
                    {
                        if (updating) return;
                        current = channel switch
                        {
                            0 => current with { X = (float)slider.Value },
                            1 => current with { Y = (float)slider.Value },
                            _ => current with { Z = (float)slider.Value },
                        };
                        Sync();
                    };
                    sliders[i] = slider;
                    var row = new StackPanel { Orientation = Orientation.Horizontal };
                    row.Children.Add(new TextBlock { Text = names[i], Width = 16, Foreground = TextBrush, VerticalAlignment = VerticalAlignment.Center });
                    row.Children.Add(slider);
                    stack.Children.Add(row);
                }
                hex.LostKeyboardFocus += (_, _) => { if (!updating && TryParseHex(hex.Text, out var parsed)) { current = parsed with { W = current.W }; Sync(); } };
                hex.KeyDown += (_, k) => { if (k.Key == Key.Enter && TryParseHex(hex.Text, out var parsed)) { current = parsed with { W = current.W }; Sync(); } };
                stack.Children.Add(hex);
                var popup = new Popup
                {
                    PlacementTarget = swatch,
                    Placement = PlacementMode.Bottom,
                    StaysOpen = false,
                    Child = new Border { Background = PanelBrush, BorderBrush = SelectedBrush, BorderThickness = new Thickness(1), Child = stack },
                };
                popup.Closed += (_, _) => { if (current != value) commit(current); };
                popup.IsOpen = true;
            };
            return swatch;
        }

        private FrameworkElement BuildRampEditor(ShaderNode node)
        {
            var stops = (node.Ramp ?? new List<RampStop>()).OrderBy(s => s.Position).ToList();
            var panel = new StackPanel { Margin = new Thickness(6, 2, 6, 2) };
            var gradient = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
            foreach (var s in stops) gradient.GradientStops.Add(new GradientStop(ToColor(new NVector4(s.R, s.G, s.B, 1)), s.Position));
            panel.Children.Add(new Border { Height = 14, Background = gradient, BorderBrush = Brushes.Black, BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 0, 3) });

            void Change(Action<List<RampStop>> edit) => Edit("Edit Color Ramp", () =>
            {
                if (Graph?.Find(node.Id) is not { } n) return;
                n.Ramp ??= new List<RampStop>();
                edit(n.Ramp);
                n.Ramp.Sort((a, b) => a.Position.CompareTo(b.Position));
            });

            for (var i = 0; i < stops.Count; i++)
            {
                var index = i;
                var stop = stops[i];
                var grid = new Grid { Margin = new Thickness(0, 1, 0, 1) };
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
                grid.ColumnDefinitions.Add(new ColumnDefinition());
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
                var pos = new TextBox { Text = stop.Position.ToString("0.###", CultureInfo.InvariantCulture), Background = FieldBrush, Foreground = TextBrush, BorderThickness = new Thickness(0), Margin = new Thickness(0, 0, 4, 0) };
                void CommitPos()
                {
                    if (float.TryParse(pos.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var p) && float.IsFinite(p) && Math.Abs(p - stop.Position) > 1e-6f)
                        Change(r => r[index] = r[index] with { Position = Math.Clamp(p, 0, 1) });
                }
                pos.LostKeyboardFocus += (_, _) => CommitPos();
                pos.KeyDown += (_, e) => { if (e.Key == Key.Enter) { CommitPos(); e.Handled = true; } };
                grid.Children.Add(pos);
                var swatch = MakeSwatch(new NVector4(stop.R, stop.G, stop.B, stop.A), c => Change(r => r[index] = r[index] with { R = c.X, G = c.Y, B = c.Z }));
                Grid.SetColumn(swatch, 1);
                grid.Children.Add(swatch);
                var remove = new Button { Content = "×", Padding = new Thickness(0), Margin = new Thickness(2, 0, 0, 0), IsEnabled = stops.Count > 1 };
                remove.Click += (_, _) => Change(r => r.RemoveAt(index));
                Grid.SetColumn(remove, 2);
                grid.Children.Add(remove);
                panel.Children.Add(grid);
            }
            var add = new Button { Content = "+ Stop", Margin = new Thickness(0, 2, 0, 0) };
            add.Click += (_, _) => Change(r =>
            {
                // Halfway into the widest gap.
                var sorted = r.OrderBy(s => s.Position).ToList();
                var at = 0.5f;
                var color = new NVector4(0.5f, 0.5f, 0.5f, 1);
                if (sorted.Count >= 2)
                {
                    var gap = Enumerable.Range(0, sorted.Count - 1).OrderByDescending(k => sorted[k + 1].Position - sorted[k].Position).First();
                    at = (sorted[gap].Position + sorted[gap + 1].Position) * 0.5f;
                    color = ShaderGraphEvaluator.EvaluateRamp(sorted.ToArray(), at, "Linear");
                }
                r.Add(new RampStop { Position = at, R = color.X, G = color.Y, B = color.Z, A = color.W });
            });
            panel.Children.Add(add);
            return panel;
        }

        // ======================= mouse =======================

        private void BeginLinkDrag(SocketRef socket)
        {
            if (Graph is not { } graph) return;
            if (socket.IsOutput)
            {
                _linkFrom = socket;
                _detached = null;
            }
            else if (graph.LinkInto(socket.NodeId, socket.Name) is { } existing)
            {
                // Pick the link up from its input end.
                var fromDef = ShaderNodeTypes.Get(graph.Find(existing.FromNode)!.Type).Outputs.First(o => o.Name == existing.FromOutput);
                _linkFrom = new SocketRef(existing.FromNode, existing.FromOutput, true, fromDef.Kind);
                _detached = existing;
                foreach (var (path, link) in _linkPaths) if (ReferenceEquals(link, existing)) path.Visibility = Visibility.Hidden;
            }
            else
            {
                return; // dragging out of an empty input does nothing (as in Blender)
            }
            _tempLink = new Path { Stroke = SelectedBrush, StrokeThickness = 2, IsHitTestVisible = false };
            Panel.SetZIndex(_tempLink, 3);
            _world.Children.Add(_tempLink);
            _view.CaptureMouse();
            _tempLink.Data = Curve(SocketCenter(_linkFrom), _lastWorldMouse);
        }

        private SocketRef? InputSocketAt(Point viewPoint)
        {
            SocketRef? found = null;
            VisualTreeHelper.HitTest(_view, null, hit =>
            {
                if (hit.VisualHit is Ellipse e && _sockets.FirstOrDefault(s => ReferenceEquals(s.Ellipse, e)) is { Socket: { IsOutput: false } s })
                {
                    found = s;
                    return HitTestResultBehavior.Stop;
                }
                return HitTestResultBehavior.Continue;
            }, new GeometryHitTestParameters(new EllipseGeometry(viewPoint, 8, 8)));
            return found;
        }

        private void OnViewMouseDown(object sender, MouseButtonEventArgs e)
        {
            Focus();
            if (e.ChangedButton == MouseButton.Middle)
            {
                _isPanning = true;
                _panStart = e.GetPosition(_view);
                _panOrigin = new Point(_pan.X, _pan.Y);
                _view.CaptureMouse();
                e.Handled = true;
            }
            else if (e.ChangedButton == MouseButton.Right && Graph is not null)
            {
                ShowAddMenu(_view, e.GetPosition(_world));
                e.Handled = true;
            }
            else if (e.ChangedButton == MouseButton.Left && e.OriginalSource == _view)
            {
                _selected = -1;
                HighlightSelection();
            }
        }

        private void OnViewMouseMove(object sender, MouseEventArgs e)
        {
            _lastWorldMouse = e.GetPosition(_world);
            if (_isPanning)
            {
                var p = e.GetPosition(_view);
                _pan.X = _panOrigin.X + p.X - _panStart.X;
                _pan.Y = _panOrigin.Y + p.Y - _panStart.Y;
                return;
            }
            if (_dragNode >= 0 && _nodeVisuals.TryGetValue(_dragNode, out var visual))
            {
                var p = e.GetPosition(_world);
                Canvas.SetLeft(visual, _dragNodeStart.X + p.X - _dragStart.X);
                Canvas.SetTop(visual, _dragNodeStart.Y + p.Y - _dragStart.Y);
                _world.UpdateLayout();
                UpdateLinks();
                return;
            }
            if (_tempLink is not null && _linkFrom is not null)
                _tempLink.Data = Curve(SocketCenter(_linkFrom), _lastWorldMouse);
        }

        private void OnViewMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Middle && _isPanning)
            {
                _isPanning = false;
                _view.ReleaseMouseCapture();
                return;
            }
            if (e.ChangedButton != MouseButton.Left) return;
            _view.ReleaseMouseCapture();

            if (_dragNode >= 0)
            {
                var id = _dragNode;
                _dragNode = -1;
                var p = e.GetPosition(_world);
                var x = (float)Math.Round(_dragNodeStart.X + p.X - _dragStart.X);
                var y = (float)Math.Round(_dragNodeStart.Y + p.Y - _dragStart.Y);
                if (Math.Abs(x - _dragNodeStart.X) > 0.5f || Math.Abs(y - _dragNodeStart.Y) > 0.5f)
                    Edit("Move Node", () => { if (Graph?.Find(id) is { } n) { n.X = x; n.Y = y; } }, rebuild: false);
                return;
            }

            if (_linkFrom is { } from)
            {
                var target = InputSocketAt(e.GetPosition(_view));
                var detached = _detached;
                _linkFrom = null;
                _detached = null;
                if (_tempLink is not null) { _world.Children.Remove(_tempLink); _tempLink = null; }

                if (target is not null && Graph is { } graph && !graph.DependsOn(from.NodeId, target.NodeId) && from.NodeId != target.NodeId)
                {
                    if (detached is not null && detached.ToNode == target.NodeId && detached.ToInput == target.Name) { Rebuild(); return; }
                    Edit("Link Nodes", () =>
                    {
                        if (detached is not null) Graph!.Links.Remove(detached);
                        Graph!.Connect(from.NodeId, from.Name, target.NodeId, target.Name);
                    });
                }
                else if (target is not null)
                {
                    _status.Text = "That link would make a loop.";
                    Rebuild();
                }
                else if (detached is not null)
                {
                    Edit("Unlink Nodes", () => Graph!.Links.Remove(detached));
                }
            }
        }

        private void OnWheel(object sender, MouseWheelEventArgs e)
        {
            var mouse = e.GetPosition(_view);
            var before = e.GetPosition(_world);
            var factor = e.Delta > 0 ? 1.12 : 1 / 1.12;
            var zoom = Math.Clamp(_zoom.ScaleX * factor, 0.25, 2.5);
            _zoom.ScaleX = _zoom.ScaleY = zoom;
            _pan.X = mouse.X - before.X * zoom;
            _pan.Y = mouse.Y - before.Y * zoom;
            e.Handled = true;
        }

        private void FrameAll()
        {
            if (Graph is not { Nodes.Count: > 0 } graph || _view.ActualWidth < 10) return;
            _world.UpdateLayout();
            double minX = graph.Nodes.Min(n => n.X), minY = graph.Nodes.Min(n => n.Y);
            double maxX = graph.Nodes.Max(n => n.X + NodeWidth);
            double maxY = graph.Nodes.Max(n => n.Y + (_nodeVisuals.TryGetValue(n.Id, out var v) ? v.ActualHeight : 200));
            var zoom = Math.Clamp(Math.Min((_view.ActualWidth - 60) / Math.Max(1, maxX - minX), (_view.ActualHeight - 60) / Math.Max(1, maxY - minY)), 0.25, 1.2);
            _zoom.ScaleX = _zoom.ScaleY = zoom;
            _pan.X = (_view.ActualWidth - (maxX - minX) * zoom) / 2 - minX * zoom;
            _pan.Y = (_view.ActualHeight - (maxY - minY) * zoom) / 2 - minY * zoom;
        }

        private void ShowAddMenu(FrameworkElement anchor, Point? worldPosition)
        {
            if (Graph is null) { _status.Text = "Click Use Nodes first."; return; }
            // From the toolbar: left of the leftmost node, so it never lands on top of another.
            var at = worldPosition ?? (Graph.Nodes.Count > 0
                ? new Point(Graph.Nodes.Min(n => n.X) - NodeWidth - 50, Graph.Nodes.OrderBy(n => n.X).First().Y)
                : new Point(0, 0));
            var menu = new ContextMenu { PlacementTarget = anchor };
            // One flat list with category captions (nested submenus don't pick up the dark theme).
            foreach (var group in ShaderNodeTypes.All.Where(d => d.Type != ShaderNodeTypes.Output).GroupBy(d => d.Category))
            {
                if (menu.Items.Count > 0) menu.Items.Add(new Separator());
                menu.Items.Add(new MenuItem { Header = group.Key.ToUpperInvariant(), IsEnabled = false, FontWeight = FontWeights.SemiBold });
                foreach (var def in group)
                {
                    var item = new MenuItem { Header = "   " + def.Title };
                    var type = def.Type;
                    item.Click += (_, _) => { AddNode(type, at); if (worldPosition is null) FrameAll(); };
                    menu.Items.Add(item);
                }
            }
            menu.IsOpen = true;
        }

        private void OnKey(object sender, KeyEventArgs e)
        {
            if (e.OriginalSource is TextBox) return;
            if (Keyboard.Modifiers == ModifierKeys.Control && (e.Key == Key.Z || e.Key == Key.Y) && Owner is IInputElement owner)
            {
                (e.Key == Key.Z ? ApplicationCommands.Undo : ApplicationCommands.Redo).Execute(null, owner);
                Rebuild();
                e.Handled = true;
                return;
            }
            switch (e.Key)
            {
                case Key.X:
                case Key.Delete:
                    DeleteSelected();
                    e.Handled = true;
                    break;
                case Key.Home:
                    FrameAll();
                    e.Handled = true;
                    break;
                case Key.A when Keyboard.Modifiers == ModifierKeys.Shift:
                    ShowAddMenu(_view, _lastWorldMouse);
                    e.Handled = true;
                    break;
            }
        }

        // ======================= live preview =======================

        private void SchedulePreview()
        {
            if (_live.IsChecked != true || _isBaking) return;
            _previewTimer.Stop();
            _previewTimer.Start();
        }

        /// <summary>Bakes a 256 px version into temporary maps and points the material at them
        /// (not an undo step - Apply makes the real one). Skips when the tree hasn't changed.</summary>
        private async Task RunPreviewAsync()
        {
            if (_live.IsChecked != true || _target() is not { } target || target.Material.NodeGraph is not { } graph) return;
            if (_previewRunning) { _previewAgain = true; return; }
            var key = System.Text.Json.JsonSerializer.Serialize(graph) + "|" + target.Material.GetHashCode();
            if (key == _lastPreviewKey) return;
            _previewRunning = true;
            try
            {
                var snapshot = graph.Clone();
                var result = await Task.Run(() => ShaderGraphBaker.Bake(snapshot, 256, LoadImage));
                var folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "JolieCat3D", "NodePreview");
                Directory.CreateDirectory(folder);
                var prefix = System.IO.Path.Combine(folder, $"{SafeName(target.Material.Name)}_{(uint)target.Material.GetHashCode():X8}");
                string? Save(TextureBuffer? map, string suffix)
                {
                    if (map is null) return null;
                    var path = $"{prefix}_{suffix}.png";
                    SavePng(map, path);
                    return path;
                }
                result.ApplyTo(target.Material, Save(result.BaseColor, "BaseColor"), Save(result.MetallicRoughness, "MetallicRoughness"),
                    Save(result.Normal, "Normal"), Save(result.Emission, "Emission"));
                _lastPreviewKey = key;
                _refreshNode(target.Node);
                if (!_isBaking) _status.Text = "Live preview updated (256 px) - Apply writes the final maps.";
            }
            catch (Exception ex)
            {
                _status.Text = "Preview failed: " + ex.Message;
            }
            finally
            {
                _previewRunning = false;
            }
            if (_previewAgain) { _previewAgain = false; SchedulePreview(); }
        }

        // ======================= bake =======================

        private async Task ApplyAsync()
        {
            if (_isBaking || _target() is not { } target || target.Material.NodeGraph is not { } graph) return;
            var material = target.Material;
            var prefix = graph.BakePathPrefix;
            if (string.IsNullOrEmpty(prefix) || !Directory.Exists(System.IO.Path.GetDirectoryName(prefix)))
            {
                var dialog = new SaveFileDialog
                {
                    Title = "Bake node tree - choose a base name for the texture maps",
                    Filter = "PNG image (*.png)|*.png",
                    FileName = SafeName(material.Name) + ".png",
                };
                if (dialog.ShowDialog(this) != true) return;
                prefix = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(dialog.FileName)!, System.IO.Path.GetFileNameWithoutExtension(dialog.FileName));
            }

            var size = _resolution.SelectedItem is int s ? s : graph.BakeSize;
            var snapshot = graph.Clone();
            _isBaking = true;
            _applyButton.IsEnabled = false;
            try
            {
                var progress = new Progress<float>(p => _status.Text = $"Baking {size}×{size}: {p:P0}");
                var result = await Task.Run(() => ShaderGraphBaker.Bake(snapshot, size, LoadImage, progress));
                string? Save(TextureBuffer? map, string suffix)
                {
                    if (map is null) return null;
                    var path = $"{prefix}_{suffix}.png";
                    SavePng(map, path);
                    return path;
                }
                var colorPath = Save(result.BaseColor, "BaseColor");
                var mrPath = Save(result.MetallicRoughness, "MetallicRoughness");
                var normalPath = Save(result.Normal, "Normal");
                var emissionPath = Save(result.Emission, "Emission");
                var finalPrefix = prefix;
                _commit(target.Node, "Apply Node Tree", () =>
                {
                    result.ApplyTo(material, colorPath, mrPath, normalPath, emissionPath);
                    if (material.NodeGraph is { } g) { g.BakePathPrefix = finalPrefix; g.BakeSize = size; }
                });
                var written = new[] { colorPath, mrPath, normalPath, emissionPath }.Count(p => p is not null);
                _isBaking = false;
                Rebuild();
                _status.Text = written == 0 ? "Applied (all outputs are constants - no maps needed)." : $"Applied: {written} map(s) written next to {System.IO.Path.GetFileName(prefix)}.";
            }
            catch (Exception ex)
            {
                _status.Text = "Bake failed: " + ex.Message;
            }
            finally
            {
                _isBaking = false;
                _applyButton.IsEnabled = Graph is not null;
            }
        }

        private static string SafeName(string name)
        {
            foreach (var c in System.IO.Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return string.IsNullOrWhiteSpace(name) ? "Material" : name;
        }

        // ======================= images =======================

        private static readonly Dictionary<string, (DateTime Time, TextureBuffer? Buffer)> ImageCache = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Decodes an image file into a <see cref="TextureBuffer"/> (cached until the file changes).</summary>
        internal static TextureBuffer? LoadImage(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                var time = File.GetLastWriteTimeUtc(path);
                lock (ImageCache)
                    if (ImageCache.TryGetValue(path, out var cached) && cached.Time == time) return cached.Buffer;

                using var stream = File.OpenRead(path);
                var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
                BitmapSource frame = decoder.Frames[0];
                if (frame.Format != PixelFormats.Bgra32) frame = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
                int w = frame.PixelWidth, h = frame.PixelHeight;
                var bytes = new byte[w * h * 4];
                frame.CopyPixels(bytes, w * 4, 0);
                var buffer = new TextureBuffer(w, h);
                for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                {
                    var o = (y * w + x) * 4;
                    buffer.SetPixel(x, y, new CoreColor4(bytes[o + 2] / 255f, bytes[o + 1] / 255f, bytes[o] / 255f, bytes[o + 3] / 255f));
                }
                lock (ImageCache) ImageCache[path] = (time, buffer);
                return buffer;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>PNG with the alpha channel kept.</summary>
        internal static void SavePng(TextureBuffer buffer, string path)
        {
            var pixels = new byte[buffer.Width * buffer.Height * 4];
            for (var i = 0; i < buffer.Pixels.Count; i++)
            {
                var c = buffer.Pixels[i];
                pixels[i * 4] = ToByte(c.B);
                pixels[i * 4 + 1] = ToByte(c.G);
                pixels[i * 4 + 2] = ToByte(c.R);
                pixels[i * 4 + 3] = ToByte(c.A);
            }
            var bitmap = BitmapSource.Create(buffer.Width, buffer.Height, 96, 96, PixelFormats.Bgra32, null, pixels, buffer.Width * 4);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(path);
            encoder.Save(stream);
            lock (ImageCache) ImageCache.Remove(path);
        }

        private static byte ToByte(float v) => (byte)Math.Clamp((int)MathF.Round(v * 255f), 0, 255);

        private static BitmapSource ToBitmap(NVector4[] pixels, int size, bool isVector)
        {
            var bytes = new byte[size * size * 4];
            for (var i = 0; i < pixels.Length; i++)
            {
                var p = pixels[i];
                if (isVector) p = new NVector4(p.X - MathF.Floor(p.X), p.Y - MathF.Floor(p.Y), p.Z, 1); // show coordinates as a tiling gradient
                bytes[i * 4] = ToByte(p.Z);
                bytes[i * 4 + 1] = ToByte(p.Y);
                bytes[i * 4 + 2] = ToByte(p.X);
                bytes[i * 4 + 3] = 255;
            }
            var bitmap = BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, bytes, size * 4);
            bitmap.Freeze();
            return bitmap;
        }

        private static Color ToColor(NVector4 v) => Color.FromRgb(ToByte(v.X), ToByte(v.Y), ToByte(v.Z));

        private static string Hex(NVector4 v) => $"#{ToByte(v.X):X2}{ToByte(v.Y):X2}{ToByte(v.Z):X2}";

        private static bool TryParseHex(string text, out NVector4 color)
        {
            color = default;
            text = text.Trim().TrimStart('#');
            if (text.Length != 6 || !int.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb)) return false;
            color = new NVector4(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, 1);
            return true;
        }
    }
}
