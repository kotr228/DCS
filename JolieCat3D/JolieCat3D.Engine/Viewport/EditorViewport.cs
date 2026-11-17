using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using JolieCat3D.Core.Camera;
using JolieCat3D.Core.Geometry;
using JolieCat3D.Engine.Editing;
using JolieCat3D.Engine.Lighting;
using JolieCat3D.Engine.Rendering;
using JolieCat3D.Engine.Rendering.Dx;
using CoreColor4 = JolieCat3D.Core.Numerics.Color4;
using CoreNode = JolieCat3D.Core.Scene.Node;
using CoreRay = JolieCat3D.Core.Geometry.Ray;
using CoreScene = JolieCat3D.Core.Scene.Scene3D;
using WpfPoint = System.Windows.Point;

namespace JolieCat3D.Engine.Viewport
{
    /// <summary>
    /// The 3D viewport control: a GPU (DirectX 11) render of the scene shown through a
    /// D3DImage, Blender-style navigation, object picking, edit-mode overlays and a
    /// clickable axis widget. Gizmos and tools draw into it through <see cref="BuildOverlay"/>.
    /// </summary>
    public sealed class EditorViewport : Grid, IViewportProjector
    {
        private static readonly CoreColor4 EdgeColor = new(0.02f, 0.02f, 0.025f, 0.9f);
        private static readonly CoreColor4 UnselectedVertexColor = new(0.0f, 0.0f, 0.0f);
        private static readonly CoreColor4 SelectedColor = new(0xF0 / 255f, 0xA0 / 255f, 0x30 / 255f);
        private static readonly CoreColor4 SelectedFaceColor = new(0xF0 / 255f, 0xA0 / 255f, 0x30 / 255f, 0.28f);
        private static readonly CoreColor4 BrushColor = new(1f, 1f, 1f, 0.85f);

        private readonly Image _image = new() { Stretch = Stretch.Fill, SnapsToDevicePixels = true };
        private readonly Canvas _overlayCanvas = new() { IsHitTestVisible = true, ClipToBounds = true };
        private readonly TextBlock _viewLabel = new()
        {
            Foreground = new SolidColorBrush(Color.FromArgb(0xC0, 0xE6, 0xE1, 0xDC)),
            FontSize = 12,
            Margin = new Thickness(10, 8, 0, 0),
            IsHitTestVisible = false,
        };

        private readonly Rectangle _selectionRect = new()
        {
            Stroke = new SolidColorBrush(Color.FromArgb(0xE0, 0xF0, 0xF0, 0xF0)),
            StrokeThickness = 1,
            StrokeDashArray = new DoubleCollection { 4, 3 },
            Fill = new SolidColorBrush(Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF)),
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
        };

        private readonly TextBlock _messageText = new()
        {
            Foreground = new SolidColorBrush(Color.FromRgb(0xF0, 0xE6, 0xD8)),
            Background = new SolidColorBrush(Color.FromArgb(0xC0, 0x20, 0x1C, 0x1A)),
            Padding = new Thickness(10, 5, 10, 5),
            FontSize = 12,
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 16),
        };
        private System.Windows.Threading.DispatcherTimer? _messageTimer;

        /// <summary>Shows a short hint at the bottom of the viewport for a few seconds.</summary>
        public void ShowMessage(string message)
        {
            _messageText.Text = message;
            _messageText.Visibility = Visibility.Visible;
            _messageTimer ??= new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _messageTimer.Stop();
            _messageTimer.Tick -= HideMessage;
            _messageTimer.Tick += HideMessage;
            _messageTimer.Start();
        }

        private void HideMessage(object? sender, EventArgs e)
        {
            _messageTimer?.Stop();
            _messageText.Visibility = Visibility.Collapsed;
        }

        private readonly Line _guideLine = new()
        {
            Stroke = new SolidColorBrush(Color.FromRgb(0x5C, 0xE0, 0x6C)),
            StrokeThickness = 1.5,
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
        };

        /// <summary>Shows (or with null hides) a straight guide line, e.g. the knife cut.</summary>
        public void SetGuideLine(Point? from, Point? to)
        {
            if (from is not { } a || to is not { } b) { _guideLine.Visibility = Visibility.Collapsed; return; }
            _guideLine.X1 = a.X; _guideLine.Y1 = a.Y; _guideLine.X2 = b.X; _guideLine.Y2 = b.Y;
            _guideLine.Visibility = Visibility.Visible;
        }

        /// <summary>Shows (or with null hides) the dashed box-select rectangle.</summary>
        public void SetSelectionRectangle(Rect? rect)
        {
            if (rect is not { } r) { _selectionRect.Visibility = Visibility.Collapsed; return; }
            Canvas.SetLeft(_selectionRect, r.X);
            Canvas.SetTop(_selectionRect, r.Y);
            _selectionRect.Width = r.Width;
            _selectionRect.Height = r.Height;
            _selectionRect.Visibility = Visibility.Visible;
        }

        private DxSceneRenderer? _renderer;
        private D3DImagePresenter? _presenter;
        private readonly OverlayDrawList _overlay = new();
        private CoreScene? _scene;
        private bool _needsPrepare = true;
        private bool _needsDraw = true;
        private bool _isRenderingHooked;
        private string? _initError;

        // navigation state
        private enum DragMode { None, Orbit, Pan, Zoom }
        private DragMode _drag;
        private WpfPoint _lastMouse;
        private bool _autoPerspective;
        private ViewportCamera? _preCameraViewState;

        public EditorViewport()
        {
            Background = Brushes.Black;
            Focusable = true;
            ClipToBounds = true;
            Children.Add(_image);
            Children.Add(_overlayCanvas);
            _overlayCanvas.Children.Add(_selectionRect);
            _overlayCanvas.Children.Add(_guideLine);
            Children.Add(_viewLabel);
            Children.Add(_messageText);
            _viewLabel.HorizontalAlignment = HorizontalAlignment.Left;
            _viewLabel.VerticalAlignment = VerticalAlignment.Top;

            Loaded += (_, _) => Initialize();
            Unloaded += (_, _) => Unhook();
            SizeChanged += (_, _) => InvalidateFrame();
        }

        // =====================================================================
        // Public state (mirrors the old Scene3DRenderer API)
        // =====================================================================

        public ViewportCamera Camera { get; private set; } = new();

        public ViewportRenderSettings Settings { get; } = new();

        public LightingSettings Lighting
        {
            get => Settings.Lighting;
            set { Settings.Lighting = value; Refresh(); }
        }

        public ShadingMode ShadingMode
        {
            get => Settings.ShadingMode;
            set { if (Settings.ShadingMode == value) return; Settings.ShadingMode = value; Refresh(); }
        }

        public bool ShowGrid
        {
            get => Settings.ShowGrid;
            set { Settings.ShowGrid = value; InvalidateFrame(); }
        }

        public float GridSize
        {
            get => Settings.GridSize;
            set { Settings.GridSize = MathF.Max(0.01f, value); InvalidateFrame(); }
        }

        public bool WireframeOverlay
        {
            get => Settings.WireframeOverlay;
            set { Settings.WireframeOverlay = value; InvalidateFrame(); }
        }

        public bool ShowShadows
        {
            get => Settings.ShowShadows;
            set { Settings.ShowShadows = value; InvalidateFrame(); }
        }

        public bool ShowAmbientOcclusion
        {
            get => Settings.ShowAmbientOcclusion;
            set { Settings.ShowAmbientOcclusion = value; InvalidateFrame(); }
        }

        public bool AntiAliasingEnabled
        {
            get => Settings.AntiAliasing;
            set { Settings.AntiAliasing = value; InvalidateFrame(); }
        }

        public bool BloomEnabled
        {
            get => Settings.Bloom;
            set { Settings.Bloom = value; InvalidateFrame(); }
        }

        public CoreNode? SelectedNode { get; private set; }

        /// <summary>Blender's 3D cursor: where new objects appear and the pivot for Spin.</summary>
        public Vector3 CursorPosition
        {
            get => _cursorPosition;
            set { _cursorPosition = value; InvalidateFrame(); }
        }
        private Vector3 _cursorPosition;

        /// <summary>Shift+right click: put the cursor on the surface under the mouse, else on
        /// the ground plane, else at the view target's depth.</summary>
        public void PlaceCursor(WpfPoint position)
        {
            if (HitTestPoint(position) is { } surface) { CursorPosition = surface; return; }
            var ray = ScreenToRay(position);
            if (MathF.Abs(ray.Direction.Y) > 1e-4f)
            {
                var t = -ray.Origin.Y / ray.Direction.Y;
                if (t > 0) { CursorPosition = ray.GetPoint(t); return; }
            }
            CursorPosition = ray.Origin + ray.Direction * Vector3.Dot(Camera.Target - ray.Origin, ray.Direction);
        }

        private void BuildCursorOverlay(OverlayDrawList list)
        {
            var c = _cursorPosition;
            var upp = WorldUnitsPerPixel(c);
            var normal = -Camera.Forward;
            var (u, v) = OverlayDrawList.Basis(normal);
            var radius = 11f * upp;
            const int segments = 16;
            for (var i = 0; i < segments; i++)
            {
                var a0 = i * MathF.Tau / segments;
                var a1 = (i + 1) * MathF.Tau / segments;
                var p0 = c + (u * MathF.Cos(a0) + v * MathF.Sin(a0)) * radius;
                var p1 = c + (u * MathF.Cos(a1) + v * MathF.Sin(a1)) * radius;
                list.AddLine(p0, p1, i % 2 == 0 ? new CoreColor4(0.9f, 0.15f, 0.15f) : new CoreColor4(1f, 1f, 1f), 2f, OverlayLayer.OnTop);
            }
            var cross = new CoreColor4(0.05f, 0.05f, 0.05f);
            foreach (var d in new[] { u, -u, v, -v })
                list.AddLine(c + d * (radius * 0.55f), c + d * (radius * 1.6f), cross, 1.3f, OverlayLayer.OnTop);
        }

        public MeshEditSession EditSession { get; } = new();
        public WeightPaintSession WeightPaintSession { get; } = new();
        public VertexPaintSession VertexPaintSession { get; } = new();
        public SculptSession SculptSession { get; } = new();
        public TexturePaintSession TexturePaintSession { get; } = new();

        /// <summary>True while the view looks through (and steers) the scene's active camera.</summary>
        public bool IsPilotingActiveCamera { get; private set; }

        /// <summary>Raised when navigation moved the piloted scene camera.</summary>
        public event Action? ActiveCameraPiloted;

        /// <summary>Raised after every <see cref="Refresh"/> (the scene may have changed).</summary>
        public event EventHandler? Refreshed;

        /// <summary>Raised whenever the camera moved (orbit/pan/zoom/preset).</summary>
        public event EventHandler? CameraChanged;

        /// <summary>Gizmos and tools add their per-frame shapes here.</summary>
        public event Action<OverlayDrawList>? BuildOverlay;

        /// <summary>Brush radius preview (world units) for paint/sculpt modes; null hides it.</summary>
        public Func<float?>? BrushRadiusProvider { get; set; }

        /// <summary>Why the GPU viewport could not start, or null.</summary>
        public string? InitializationError => _initError;

        public bool IsSoftwareFallback => _presenter?.IsFallback ?? true;

        // =====================================================================
        // Scene
        // =====================================================================

        public void Render(CoreScene scene, bool zoomToFit = true)
        {
            ArgumentNullException.ThrowIfNull(scene);
            _scene = scene;
            if (IsPilotingActiveCamera && scene.ActiveCamera is null) ExitActiveCameraView();
            _needsPrepare = true;
            if (zoomToFit) ZoomToFit(scene);
            InvalidateFrame();
        }

        public void Refresh()
        {
            _needsPrepare = true;
            if (IsPilotingActiveCamera && _scene?.ActiveCamera is { } cam) Camera.MatchSceneCamera(cam);
            InvalidateFrame();
            Refreshed?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Redraws without re-reading the scene (camera move, gizmo hover, ...).</summary>
        public void InvalidateFrame()
        {
            _needsDraw = true;
            Hook();
        }

        public void RefreshLighting() => Refresh();

        public void RefreshComponentOverlay() => InvalidateFrame();

        public void Select(CoreNode? node) => SetSelection(node is null ? Array.Empty<CoreNode>() : new[] { node }, node);

        /// <summary>Every selected object (outlined); <see cref="SelectedNode"/> is the active one.</summary>
        public IReadOnlyList<CoreNode> SelectedNodes { get; private set; } = Array.Empty<CoreNode>();

        public void SetSelection(IEnumerable<CoreNode> nodes, CoreNode? active)
        {
            SelectedNodes = nodes.Distinct().ToList();
            SelectedNode = active;
            _needsPrepare = true;
            InvalidateFrame();
        }

        public void EnterEditMode(CoreNode node) { EditSession.Attach(node); Refresh(); }
        public void ExitEditMode() { EditSession.Attach(null); Refresh(); }
        public void EnterWeightPaintMode(CoreNode node) { WeightPaintSession.Attach(node); Refresh(); }
        public void ExitWeightPaintMode() { WeightPaintSession.Attach(null); Refresh(); }
        public void EnterVertexPaintMode(CoreNode node) { VertexPaintSession.Attach(node); Refresh(); }
        public void ExitVertexPaintMode() { VertexPaintSession.Attach(null); Refresh(); }
        public void EnterSculptMode(CoreNode node) { SculptSession.Attach(node); Refresh(); }
        public void ExitSculptMode() { SculptSession.Attach(null); Refresh(); }

        public void EnterTexturePaintMode(CoreNode node)
        {
            TexturePaintSession.Attach(node);
            TexturePaintSession.EnsureTextureBuffer();
            Refresh();
        }

        public void ExitTexturePaintMode() { TexturePaintSession.Attach(null); Refresh(); }

        // =====================================================================
        // Camera operations
        // =====================================================================

        public void ZoomToFit(CoreScene scene)
        {
            var (min, max) = scene.GetBounds();
            if (min == max) { Camera.Target = min; Camera.Distance = MathF.Max(Camera.Distance, 5f); }
            else Camera.Frame(min, max, Aspect);
            OnCameraChanged();
        }

        public void ZoomToFit(CoreNode node)
        {
            var (min, max) = node.GetWorldBounds();
            if (min == max) { Camera.Target = min; }
            else Camera.Frame(min, max, Aspect);
            OnCameraChanged();
        }

        public void FrameSelected()
        {
            if (EditSession.Target is not null && EditSession.GetSelectionWorldCentroid() is { } centroid)
            {
                Camera.Target = centroid;
                OnCameraChanged();
                return;
            }
            if (SelectedNode is { } node) ZoomToFit(node);
            else if (_scene is { } scene) ZoomToFit(scene);
        }

        public void SetOrthographic(bool orthographic)
        {
            if (IsPilotingActiveCamera) return;
            Camera.IsOrthographic = orthographic;
            Camera.FixedOrthographicHeight = null;
            _autoPerspective = false;
            OnCameraChanged();
        }

        /// <summary>Front/Top/... view. Like Blender, switches to orthographic until you orbit away.</summary>
        public void AlignToPreset(ViewPreset preset, CoreNode? focusNode = null, bool frame = false)
        {
            if (IsPilotingActiveCamera) ExitActiveCameraView();
            Camera.AlignToPreset(preset);
            if (!Camera.IsOrthographic)
            {
                Camera.IsOrthographic = true;
                _autoPerspective = true;
            }
            if (frame)
            {
                if (focusNode is not null) ZoomToFit(focusNode);
                else if (_scene is not null) ZoomToFit(_scene);
            }
            OnCameraChanged();
        }

        public bool EnterActiveCameraView()
        {
            if (IsPilotingActiveCamera) return true;
            if (_scene?.ActiveCamera is not { Camera: not null } cameraNode) return false;
            _preCameraViewState = Camera.Clone();
            Camera = new ViewportCamera();
            Camera.MatchSceneCamera(cameraNode);
            IsPilotingActiveCamera = true;
            Refresh();
            return true;
        }

        public void ExitActiveCameraView()
        {
            if (!IsPilotingActiveCamera) return;
            IsPilotingActiveCamera = false;
            if (_preCameraViewState is not null) Camera = _preCameraViewState;
            _preCameraViewState = null;
            Refresh();
        }

        private float Aspect => (float)Math.Max(1, ActualWidth) / (float)Math.Max(1, ActualHeight);

        private void OnCameraChanged()
        {
            if (IsPilotingActiveCamera && _scene?.ActiveCamera is { } cameraNode)
            {
                Camera.WriteToSceneCamera(cameraNode);
                ActiveCameraPiloted?.Invoke();
                _needsPrepare = true;
            }
            InvalidateFrame();
            CameraChanged?.Invoke(this, EventArgs.Empty);
        }

        // =====================================================================
        // IViewportProjector
        // =====================================================================

        private Size ViewSize => new(Math.Max(1, ActualWidth), Math.Max(1, ActualHeight));

        public CoreRay ScreenToRay(WpfPoint screenPosition) => Camera.ScreenToRay(screenPosition, ViewSize);

        public WpfPoint? WorldToScreen(Vector3 worldPosition) => Camera.WorldToScreen(worldPosition, ViewSize);

        public float WorldUnitsPerPixel(Vector3 worldPosition) => Camera.WorldUnitsPerPixel(worldPosition, ViewSize);

        public WpfPoint MousePosition => Mouse.GetPosition(this);

        // =====================================================================
        // Picking
        // =====================================================================

        /// <summary>The node under a viewport point (meshes by triangle, cameras by icon), or null.</summary>
        public CoreNode? HitTest(WpfPoint position) => Pick(position, out _);

        /// <summary>The surface point under a viewport point, or null over empty space.</summary>
        public Vector3? HitTestPoint(WpfPoint position) => Pick(position, out var point) is not null ? point : null;

        private CoreNode? Pick(WpfPoint position, out Vector3 hitPoint)
        {
            hitPoint = default;
            EnsurePrepared();
            if (_renderer is null) return null;
            var ray = ScreenToRay(position);
            CoreNode? best = null;
            var bestDistance = float.MaxValue;

            foreach (var target in _renderer.PickTargets)
            {
                if (!Matrix4x4.Invert(target.World, out var inv)) continue;
                var localOrigin = Vector3.Transform(ray.Origin, inv);
                var localDir = Vector3.TransformNormal(ray.Direction, inv);
                if (localDir.LengthSquared() < 1e-20f) continue;
                var localRay = new CoreRay(localOrigin, localDir);
                if (RayIntersection.IntersectAABB(localRay, target.Mesh.LocalMin - new Vector3(1e-4f), target.Mesh.LocalMax + new Vector3(1e-4f)) is null) continue;

                var verts = target.Mesh.Evaluated.Vertices;
                foreach (var tri in target.Mesh.Triangles)
                {
                    if (RayIntersection.IntersectTriangle(localRay, verts[tri.A].Position, verts[tri.B].Position, verts[tri.C].Position) is not { } t) continue;
                    var worldHit = Vector3.Transform(localRay.GetPoint(t), target.World);
                    var d = Vector3.Dot(worldHit - ray.Origin, ray.Direction);
                    if (d >= 0 && d < bestDistance)
                    {
                        bestDistance = d;
                        best = target.Node;
                        hitPoint = worldHit;
                    }
                }
            }

            // Mesh-less helpers (cameras): pick near their origin/frustum.
            foreach (var helper in _renderer.HelperNodes)
            {
                var origin = helper.GetWorldPosition();
                var tip = origin + Vector3.Normalize(helper.GetWorldForward());
                foreach (var p in new[] { origin, tip })
                {
                    if (WorldToScreen(p) is not { } screen) continue;
                    if ((screen - position).Length > 14) continue;
                    var d = Vector3.Dot(p - ray.Origin, ray.Direction);
                    if (d < bestDistance) { bestDistance = d; best = helper; hitPoint = p; }
                }
            }

            return best;
        }

        // =====================================================================
        // Rendering loop
        // =====================================================================

        private void Initialize()
        {
            if (_renderer is not null) { Hook(); return; }
            try
            {
                _renderer = new DxSceneRenderer(DxGraphics.Shared, sharedOutput: true) { Settings = Settings };
                _presenter = new D3DImagePresenter(DxGraphics.Shared);
                _renderer.OutputRecreated += () =>
                {
                    _presenter.Attach(_renderer.Output!);
                    _image.Source = _presenter.Source;
                };
                _presenter.SourceChanged += () => _image.Source = _presenter.Source;
            }
            catch (Exception ex)
            {
                _initError = ex.Message;
                _viewLabel.Text = "3D viewport unavailable: " + ex.Message;
                return;
            }

            BuildAxisWidget();
            Hook();
        }

        private void Hook()
        {
            if (_isRenderingHooked || _renderer is null) return;
            CompositionTarget.Rendering += OnRendering;
            _isRenderingHooked = true;
        }

        private void Unhook()
        {
            if (!_isRenderingHooked) return;
            CompositionTarget.Rendering -= OnRendering;
            _isRenderingHooked = false;
        }

        private void EnsurePrepared()
        {
            if (_renderer is null || !_needsPrepare) return;
            _needsPrepare = false;
            var weightTarget = WeightPaintSession.Target;
            // Like Blender, no object outline while editing components or painting.
            _renderer.EditTarget = EditSession.Target;
            var showOutline = EditSession.Target is null && weightTarget is null && SculptSession.Target is null
                && VertexPaintSession.Target is null && TexturePaintSession.Target is null;
            _renderer.Prepare(_scene, showOutline ? SelectedNodes : Array.Empty<CoreNode>(), showOutline ? SelectedNode : null,
                weightTarget, WeightPaintSession.ActiveBoneIndex,
                _scene?.ActiveCamera, IsPilotingActiveCamera ? _scene?.ActiveCamera : null);
        }

        private void OnRendering(object? sender, EventArgs e)
        {
            if (!_needsDraw && !_needsPrepare) return;
            if (_renderer is null || _presenter is null) return;
            if (ActualWidth < 1 || ActualHeight < 1 || !IsVisible) return;

            try
            {
                EnsurePrepared();
                var dpi = VisualTreeHelper.GetDpi(this);
                var pixelW = Math.Max(1, (int)Math.Round(ActualWidth * dpi.DpiScaleX));
                var pixelH = Math.Max(1, (int)Math.Round(ActualHeight * dpi.DpiScaleY));

                _overlay.Clear();
                if (Settings.ShowOverlays)
                {
                    BuildCursorOverlay(_overlay);
                    BuildEditOverlay(_overlay);
                    BuildBrushOverlay(_overlay);
                    BuildOverlay?.Invoke(_overlay);
                }

                _renderer.Draw(Camera, pixelW, pixelH, _overlay);
                _presenter.Present(_renderer);
                _needsDraw = false;
                UpdateAxisWidget();
                UpdateViewLabel();
            }
            catch (Exception ex)
            {
                _needsDraw = false;
                _viewLabel.Text = "Render error: " + ex.Message;
            }
        }

        // ---------------- edit-mode overlay ----------------

        private void BuildEditOverlay(OverlayDrawList list)
        {
            if (EditSession.Target is not { Mesh: { } mesh } node || mesh.Vertices.Count == 0) return;
            var world = node.GetWorldTransform();
            var positions = new Vector3[mesh.Vertices.Count];
            for (var i = 0; i < positions.Length; i++) positions[i] = Vector3.Transform(mesh.Vertices[i].Position, world);

            // Selected faces
            foreach (var polygon in mesh.Polygons)
            {
                if (!EditSession.IsFaceSelected(polygon.Indices)) continue;
                foreach (var t in polygon.Triangulate())
                    list.AddTriangle(positions[t.A], positions[t.B], positions[t.C], SelectedFaceColor, OverlayLayer.Scene);
            }
            foreach (var f in mesh.Faces)
            {
                if (!EditSession.IsFaceSelected(new[] { f.A, f.B, f.C })) continue;
                list.AddTriangle(positions[f.A], positions[f.B], positions[f.C], SelectedFaceColor, OverlayLayer.Scene);
            }

            const float bias = 0.0004f;
            foreach (var (a, b) in mesh.GetEdges())
            {
                if (a >= positions.Length || b >= positions.Length) continue;
                var selected = EditSession.IsEdgeSelected(a, b);
                // Blender colours: creases magenta, UV seams red (selection wins).
                var color = selected ? SelectedColor
                    : mesh.IsCrease(a, b) ? new CoreColor4(0.85f, 0.25f, 0.85f)
                    : mesh.IsSeam(a, b) ? new CoreColor4(0.9f, 0.2f, 0.15f)
                    : EdgeColor;
                var width = selected ? 2.4f : mesh.IsCrease(a, b) || mesh.IsSeam(a, b) ? 2.2f : 1.5f;
                list.AddLine(positions[a], positions[b], color, width, OverlayLayer.Scene, bias);
            }

            if (EditSession.ComponentMode == ComponentType.Vertex || EditSession.HasSelection)
            {
                for (var i = 0; i < positions.Length; i++)
                {
                    var selected = EditSession.SelectedVertexIndices.Contains(i);
                    if (!selected && EditSession.ComponentMode != ComponentType.Vertex) continue;
                    list.AddPoint(positions[i], selected ? SelectedColor : UnselectedVertexColor, selected ? 8f : 6f, OverlayLayer.Scene, bias * 2);
                }
            }
        }

        private void BuildBrushOverlay(OverlayDrawList list)
        {
            if (BrushRadiusProvider?.Invoke() is not { } radius || radius <= 0) return;
            if (!IsMouseOver) return;
            var hit = SculptSession.Target is not null ? SculptSession.RaycastWorldHitPoint(this, MousePosition)
                : WeightPaintSession.Target is not null ? WeightPaintSession.RaycastWorldHitPoint(this, MousePosition)
                : VertexPaintSession.Target is not null ? VertexPaintSession.RaycastWorldHitPoint(this, MousePosition)
                : null;
            if (hit is not { } point) return;
            var normal = -Camera.Forward;
            list.AddCircle(point, normal, radius, BrushColor, 1.5f, OverlayLayer.OnTop, 48);
        }

        // =====================================================================
        // Input: Blender-style navigation
        // =====================================================================

        protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
        {
            base.OnPreviewMouseDown(e);
            Focus();
            var mods = Keyboard.Modifiers;
            DragMode mode = DragMode.None;
            if (e.ChangedButton == MouseButton.Right && mods.HasFlag(ModifierKeys.Shift))
            {
                PlaceCursor(e.GetPosition(this));
                e.Handled = true;
                return;
            }
            if (e.ChangedButton == MouseButton.Middle || e.ChangedButton == MouseButton.Right)
            {
                mode = mods.HasFlag(ModifierKeys.Shift) ? DragMode.Pan : mods.HasFlag(ModifierKeys.Control) ? DragMode.Zoom : DragMode.Orbit;
            }

            if (mode == DragMode.None) return;
            _drag = mode;
            _lastMouse = e.GetPosition(this);
            CaptureMouse();
            e.Handled = true;
        }

        protected override void OnPreviewMouseMove(MouseEventArgs e)
        {
            base.OnPreviewMouseMove(e);
            var p = e.GetPosition(this);
            if (_drag != DragMode.None)
            {
                var dx = (float)(p.X - _lastMouse.X);
                var dy = (float)(p.Y - _lastMouse.Y);
                _lastMouse = p;
                switch (_drag)
                {
                    case DragMode.Orbit:
                        if (_autoPerspective) { Camera.IsOrthographic = false; _autoPerspective = false; }
                        Camera.Orbit(-dx * 0.005f, -dy * 0.005f);
                        break;
                    case DragMode.Pan:
                        Camera.Pan(dx, dy, (float)ViewSize.Height);
                        break;
                    case DragMode.Zoom:
                        Camera.Zoom(MathF.Pow(1.01f, dy));
                        break;
                }
                OnCameraChanged();
                e.Handled = true;
                return;
            }

            // Hover feedback (gizmo highlight, brush circle).
            InvalidateFrame();
        }

        protected override void OnPreviewMouseUp(MouseButtonEventArgs e)
        {
            base.OnPreviewMouseUp(e);
            if (_drag == DragMode.None) return;
            _drag = DragMode.None;
            ReleaseMouseCapture();
            e.Handled = true;
        }

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            base.OnMouseWheel(e);
            var factor = MathF.Pow(0.85f, e.Delta / 120f);
            Camera.Zoom(factor, ScreenToRay(e.GetPosition(this)));
            OnCameraChanged();
            e.Handled = true;
        }

        protected override void OnMouseLeave(MouseEventArgs e)
        {
            base.OnMouseLeave(e);
            InvalidateFrame();
        }

        /// <summary>Numpad view keys, Home / numpad-period framing. Returns true if handled.</summary>
        public bool HandleKey(Key key, ModifierKeys modifiers)
        {
            var ctrl = modifiers.HasFlag(ModifierKeys.Control);
            switch (key)
            {
                case Key.NumPad1: AlignToPreset(ctrl ? ViewPreset.Back : ViewPreset.Front); return true;
                case Key.NumPad3: AlignToPreset(ctrl ? ViewPreset.Left : ViewPreset.Right); return true;
                case Key.NumPad7: AlignToPreset(ctrl ? ViewPreset.Bottom : ViewPreset.Top); return true;
                case Key.NumPad5: SetOrthographic(!Camera.IsOrthographic); return true;
                case Key.NumPad4: OrbitStep(0.2618f, 0); return true;
                case Key.NumPad6: OrbitStep(-0.2618f, 0); return true;
                case Key.NumPad8: OrbitStep(0, -0.2618f); return true;
                case Key.NumPad2: OrbitStep(0, 0.2618f); return true;
                case Key.Add: Camera.Zoom(0.8f); OnCameraChanged(); return true;
                case Key.Subtract: Camera.Zoom(1.25f); OnCameraChanged(); return true;
                case Key.Decimal: FrameSelected(); return true;
                case Key.Home: if (_scene is not null) ZoomToFit(_scene); return true;
                default: return false;
            }
        }

        private void OrbitStep(float yaw, float pitch)
        {
            if (_autoPerspective) { Camera.IsOrthographic = false; _autoPerspective = false; }
            Camera.Orbit(yaw, pitch);
            OnCameraChanged();
        }

        // =====================================================================
        // Axis widget (top-right) and view label
        // =====================================================================

        private readonly (Line Line, Ellipse Dot, TextBlock Label, Vector3 Axis, ViewPreset Preset, bool Positive)[] _axisParts = new (Line, Ellipse, TextBlock, Vector3, ViewPreset, bool)[6];

        private void BuildAxisWidget()
        {
            var defs = new (Vector3 Axis, string Name, Color Color, ViewPreset Preset, bool Positive)[]
            {
                (Vector3.UnitX, "X", Color.FromRgb(0xE0, 0x4A, 0x55), ViewPreset.Right, true),
                (Vector3.UnitY, "Y", Color.FromRgb(0x72, 0xC0, 0x4C), ViewPreset.Top, true),
                (Vector3.UnitZ, "Z", Color.FromRgb(0x4C, 0x80, 0xE8), ViewPreset.Front, true),
                (-Vector3.UnitX, "", Color.FromRgb(0xE0, 0x4A, 0x55), ViewPreset.Left, false),
                (-Vector3.UnitY, "", Color.FromRgb(0x72, 0xC0, 0x4C), ViewPreset.Bottom, false),
                (-Vector3.UnitZ, "", Color.FromRgb(0x4C, 0x80, 0xE8), ViewPreset.Back, false),
            };

            for (var i = 0; i < defs.Length; i++)
            {
                var d = defs[i];
                var brush = new SolidColorBrush(d.Color);
                var line = new Line { Stroke = brush, StrokeThickness = 2, IsHitTestVisible = false, Visibility = d.Positive ? Visibility.Visible : Visibility.Collapsed };
                var dot = new Ellipse
                {
                    Width = d.Positive ? 18 : 14,
                    Height = d.Positive ? 18 : 14,
                    Fill = d.Positive ? brush : new SolidColorBrush(Color.FromArgb(0x70, d.Color.R, d.Color.G, d.Color.B)),
                    Stroke = d.Positive ? null : brush,
                    StrokeThickness = 1.5,
                    Cursor = Cursors.Hand,
                    ToolTip = d.Preset + " view",
                };
                var label = new TextBlock { Text = d.Name, FontSize = 11, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0x18, 0x16, 0x15)), IsHitTestVisible = false };
                var preset = d.Preset;
                dot.MouseLeftButtonDown += (_, e) =>
                {
                    AlignToPreset(preset);
                    e.Handled = true;
                };
                _overlayCanvas.Children.Add(line);
                _overlayCanvas.Children.Add(dot);
                _overlayCanvas.Children.Add(label);
                _axisParts[i] = (line, dot, label, d.Axis, d.Preset, d.Positive);
            }
            UpdateAxisWidget();
        }

        private void UpdateAxisWidget()
        {
            if (_axisParts[0].Line is null) return;
            const double radius = 34;
            var center = new WpfPoint(ActualWidth - 52, 52);
            var view = Camera.GetView();
            var order = _axisParts
                .Select((part, index) => (part, index, v: Vector3.TransformNormal(part.Axis, view)))
                .OrderBy(t => t.v.Z) // farther first (view space looks down -Z)
                .ToList();

            var z = 0;
            foreach (var (part, _, v) in order)
            {
                var end = new WpfPoint(center.X + v.X * radius, center.Y - v.Y * radius);
                part.Line.X1 = center.X; part.Line.Y1 = center.Y;
                part.Line.X2 = end.X; part.Line.Y2 = end.Y;
                Canvas.SetLeft(part.Dot, end.X - part.Dot.Width / 2);
                Canvas.SetTop(part.Dot, end.Y - part.Dot.Height / 2);
                Canvas.SetLeft(part.Label, end.X - 4);
                Canvas.SetTop(part.Label, end.Y - 8);
                Panel.SetZIndex(part.Line, z++);
                Panel.SetZIndex(part.Dot, z++);
                Panel.SetZIndex(part.Label, z++);
            }
        }

        private void UpdateViewLabel()
        {
            if (IsPilotingActiveCamera)
            {
                _viewLabel.Text = $"Camera View  ·  {_scene?.ActiveCamera?.Name}";
                return;
            }

            var f = Camera.Forward;
            string? axisName = null;
            if (Camera.IsOrthographic)
            {
                if (Vector3.Dot(f, -Vector3.UnitY) > 0.999f) axisName = "Top";
                else if (Vector3.Dot(f, Vector3.UnitY) > 0.999f) axisName = "Bottom";
                else if (Vector3.Dot(f, -Vector3.UnitZ) > 0.999f) axisName = "Front";
                else if (Vector3.Dot(f, Vector3.UnitZ) > 0.999f) axisName = "Back";
                else if (Vector3.Dot(f, -Vector3.UnitX) > 0.999f) axisName = "Right";
                else if (Vector3.Dot(f, Vector3.UnitX) > 0.999f) axisName = "Left";
            }
            var projection = Camera.IsOrthographic ? "Orthographic" : "Perspective";
            _viewLabel.Text = (axisName ?? "User") + " " + projection + (IsSoftwareFallback ? "  (software)" : string.Empty);
        }

        // =====================================================================
        // Capture
        // =====================================================================

        /// <summary>The scene currently shown.</summary>
        public CoreScene? Scene => _scene;

        /// <summary>An offscreen renderer configured like this viewport (no overlays, always
        /// anti-aliased). Reuse one for a whole frame sequence so meshes upload once.</summary>
        public OffscreenRenderer CreateCaptureRenderer() => new()
        {
            Settings = new ViewportRenderSettings
            {
                ShadingMode = Settings.ShadingMode,
                ShowGrid = Settings.ShowGrid,
                GridSize = Settings.GridSize,
                ShowShadows = Settings.ShowShadows,
                ShowAmbientOcclusion = Settings.ShowAmbientOcclusion,
                AntiAliasing = true,
                Bloom = Settings.Bloom,
                ShowOverlays = false,
                Lighting = Settings.Lighting,
            },
        };

        /// <summary>Renders the current view (same camera) with <paramref name="renderer"/>.</summary>
        public BitmapSource CaptureFrame(OffscreenRenderer renderer, int width, int height) =>
            renderer.Render(_scene ?? new CoreScene(), Camera, width, height,
                hiddenCamera: IsPilotingActiveCamera ? _scene?.ActiveCamera : null);

        /// <summary>Renders the current view at any resolution.</summary>
        public BitmapSource CaptureFrame(int width, int height)
        {
            using var renderer = CreateCaptureRenderer();
            return CaptureFrame(renderer, width, height);
        }

        /// <summary>Viewport size in physical pixels.</summary>
        public (int Width, int Height) PixelSize
        {
            get
            {
                var dpi = VisualTreeHelper.GetDpi(this);
                return (Math.Max(1, (int)Math.Round(ActualWidth * dpi.DpiScaleX)), Math.Max(1, (int)Math.Round(ActualHeight * dpi.DpiScaleY)));
            }
        }
    }
}
