using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;
using JolieCat3D.Engine.Camera;
using JolieCat3D.Engine.Editing;
using JolieCat3D.Engine.Geometry;
using JolieCat3D.Engine.Lighting;
using JolieCat3D.Engine.Selection;
using CoreNode = JolieCat3D.Core.Scene.Node;
using CoreScene = JolieCat3D.Core.Scene.Scene3D;

namespace JolieCat3D.Engine.Rendering
{
    /// <summary>
    /// The top-level facade tying every other <c>JolieCat3D.Engine</c> piece together:
    /// bind one instance to a <see cref="HelixViewport3D"/> (typically once, e.g. from a
    /// window's constructor), configure standard lighting and orbit/pan/zoom camera
    /// controls once via <see cref="Attach"/>, then call <see cref="Render"/> every time
    /// the <c>JolieCat3D.Core</c> scene it's showing changes. This is the one class a
    /// consumer (<c>JolieCat3D.UI</c>) actually needs to know about to get a
    /// <see cref="CoreScene"/> on screen and to hit-test/highlight the currently selected
    /// object in it - it owns no rendering logic of its own beyond delegating to
    /// <see cref="SceneGraphBuilder"/>, <see cref="SceneLightingFactory"/>,
    /// <see cref="CameraFraming"/>, and <see cref="SceneHitTester"/>/<see cref="SelectionHighlightFactory"/>.
    /// </summary>
    public sealed class Scene3DRenderer
    {
        private static readonly Color SelectionColor = Color.FromRgb(0xC2, 0x9B, 0x58); // JolieCat AccentBrush (gold)

        private readonly HelixViewport3D _viewport;
        private readonly ModelVisual3D _lightingVisual = new();
        private readonly ModelVisual3D _sceneLightingVisual = new();
        private readonly ModelVisual3D _sceneVisual = new();
        private readonly ModelVisual3D _componentOverlayVisual = new();
        private readonly Dictionary<GeometryModel3D, CoreNode> _modelToNode = new();
        private Visual3D? _selectionVisual;
        private Visual3D? _wireframeVisual;
        private ShadingMode _shadingMode = ShadingMode.Rendered;
        private CoreScene? _lastScene;
        private bool _isAttached;

        public LightingSettings Lighting { get; set; } = LightingSettings.CreateDefault();

        /// <summary>Which viewport shading style <see cref="Render"/>/<see cref="Refresh"/>
        /// currently draw the scene in - see <see cref="Rendering.ShadingMode"/>'s own
        /// remarks for what each mode actually changes. Setting this re-renders
        /// immediately (a no-op before any scene has ever been shown), the same
        /// "setting the mode also applies it" shape <c>Gizmos.TransformGizmo.Mode</c>
        /// already uses.</summary>
        public ShadingMode ShadingMode
        {
            get => _shadingMode;
            set
            {
                if (_shadingMode == value) return;
                _shadingMode = value;
                Refresh();
            }
        }

        /// <summary>The object the last <see cref="Select"/> call marked as selected -
        /// null if nothing is. <c>JolieCat3D.UI</c> reads this after a viewport click
        /// (via <see cref="HitTest"/> then <see cref="Select"/>) to know which
        /// <c>NodeViewModel</c> to make the Properties Inspector show.</summary>
        public CoreNode? SelectedNode { get; private set; }

        /// <summary>Edit Mode's own selection state (see <see cref="EnterEditMode"/>/
        /// <see cref="ExitEditMode"/>) - always exists (never null itself), with
        /// <see cref="MeshEditSession.Target"/> null whenever Edit Mode isn't active.
        /// <c>JolieCat3D.UI</c> reads/mutates this directly (via
        /// <c>Editing.ComponentHitTester</c> and its own selection methods) then calls
        /// <see cref="RefreshComponentOverlay"/> to redraw the resulting markers.</summary>
        public MeshEditSession EditSession { get; } = new();

        public Scene3DRenderer(HelixViewport3D viewport) =>
            _viewport = viewport ?? throw new ArgumentNullException(nameof(viewport));

        /// <summary>
        /// One-time setup: configures orbit/pan/zoom (see <see cref="CameraFraming.ConfigureOrbitPanZoom"/>)
        /// and adds this renderer's own lighting/scene/selection visuals to
        /// <see cref="_viewport"/>'s children. Idempotent - <see cref="Render"/> calls
        /// this itself, so a caller never strictly needs to call it directly, but doing
        /// so explicitly (e.g. right after constructing the viewport, before any scene
        /// exists yet) shows an empty, correctly-lit viewport immediately rather than a
        /// completely blank one.
        /// </summary>
        public void Attach()
        {
            if (_isAttached) return;

            CameraFraming.ConfigureOrbitPanZoom(_viewport);

            _lightingVisual.Content = SceneLightingFactory.CreateStandardLighting(Lighting);
            _viewport.Children.Add(_lightingVisual);
            _viewport.Children.Add(_sceneLightingVisual);
            _viewport.Children.Add(_sceneVisual);
            _viewport.Children.Add(_componentOverlayVisual);

            _isAttached = true;
        }

        /// <summary>Rebuilds the lighting visual from <see cref="Lighting"/>'s current
        /// settings - call after changing them; <see cref="Attach"/>/<see cref="Render"/>
        /// already do this once on their own.</summary>
        public void RefreshLighting()
        {
            Attach();
            _lightingVisual.Content = SceneLightingFactory.CreateStandardLighting(Lighting);
        }

        /// <summary>
        /// Maps <paramref name="scene"/>'s whole node tree into GPU-renderable
        /// <see cref="Model3DGroup"/>/<see cref="MeshGeometry3D"/> content (see
        /// <see cref="SceneGraphBuilder"/>) and shows it - replacing whatever this
        /// renderer showed last, so calling this again after editing
        /// <paramref name="scene"/> is how a caller re-renders it. Also rebuilds the
        /// scene's own light-node lighting (see <see cref="SceneLightingFactory.CreateSceneLights"/> -
        /// additive to the fixed <see cref="Lighting"/> rig, never a replacement for it)
        /// every call, since a light node can move/animate the same way a mesh can.
        ///
        /// If <see cref="CoreScene.ActiveCamera"/> is set, this pushes ITS current world
        /// transform onto the viewport's own rendering camera every call too (see
        /// <see cref="SceneCameraSync.Apply"/>) - moving, rotating, or animating that
        /// node updates the actual rendered view in real time, and <paramref name="zoomToFit"/>
        /// is skipped entirely in that case (an active camera's own transform IS the
        /// intended view; auto-framing the scene's bounds on top of it would fight with
        /// it). With no active camera (every scene from before this feature existed),
        /// <paramref name="zoomToFit"/> behaves exactly as it always has: reframes the
        /// free orbit/pan/zoom camera on the scene's own bounds (see
        /// <see cref="CameraFraming.ZoomToFit"/>) - on by default since a freshly
        /// replaced scene is otherwise not guaranteed to still be inside the previous
        /// scene's own framing; a live edit re-render (see <see cref="Refresh"/>) turns
        /// this off, since re-framing the camera on every keystroke of a Properties
        /// Inspector field or every frame of a gizmo drag would be disorienting.
        /// </summary>
        public void Render(CoreScene scene, bool zoomToFit = true)
        {
            ArgumentNullException.ThrowIfNull(scene);
            Attach();

            _lastScene = scene;
            _modelToNode.Clear();

            if (ShadingMode == ShadingMode.Wireframe)
            {
                // No filled geometry at all in Wireframe mode - see ShadingMode.Wireframe's
                // own remarks. _modelToNode stays empty, so HitTest/click-to-select finds
                // nothing (there is no GeometryModel3D to click); Edit Mode's own
                // ComponentHitTester works regardless, since it never uses WPF hit-testing.
                _sceneVisual.Content = new Model3DGroup();
                RefreshWireframeVisual(scene);
            }
            else
            {
                _sceneVisual.Content = SceneGraphBuilder.Build(scene, _modelToNode, ShadingMode);
                RefreshWireframeVisual(null);
            }

            _sceneLightingVisual.Content = SceneLightingFactory.CreateSceneLights(scene);

            if (scene.ActiveCamera is { } activeCamera) SceneCameraSync.Apply(_viewport, activeCamera);
            else if (zoomToFit) CameraFraming.ZoomToFit(_viewport, scene);

            RefreshSelectionHighlight();
            RefreshComponentOverlay();
        }

        /// <summary>Re-renders the same scene <see cref="Render"/> was last called with
        /// (a no-op if it never was), without re-framing the camera - what a gizmo drag
        /// or a Properties Inspector edit should call after changing a node's transform,
        /// so the mesh on screen (and the selection outline/gizmo position around it)
        /// catches up to the new values.</summary>
        public void Refresh()
        {
            if (_lastScene is { } scene) Render(scene, zoomToFit: false);
        }

        /// <summary>The <see cref="CoreNode"/> under <paramref name="position"/> (in
        /// this renderer's own viewport's coordinates) - null if nothing was hit. A
        /// meshed node is found via WPF's own precise per-triangle hit test; a
        /// camera/light node (which has no mesh, and therefore no <see cref="GeometryModel3D"/>
        /// for that test to ever find) via <see cref="SceneHitTester"/>'s own
        /// bounding-box fallback against <see cref="_lastScene"/> - see its own remarks.
        /// Does not itself change <see cref="SelectedNode"/>; call <see cref="Select"/>
        /// with the result to actually select it.</summary>
        public CoreNode? HitTest(Point position) => SceneHitTester.HitTest(_viewport, position, _modelToNode, _lastScene);

        /// <summary>Marks <paramref name="node"/> as selected (or clears selection, for
        /// null) and rebuilds the selection-outline visual around it. Deliberately
        /// separate from <see cref="HitTest"/> (rather than one combined "click to
        /// select" method) so <c>JolieCat3D.UI</c> can also call this from a Scene
        /// Outliner click, not just a viewport one.</summary>
        public void Select(CoreNode? node)
        {
            SelectedNode = node;
            RefreshSelectionHighlight();
        }

        private void RefreshSelectionHighlight()
        {
            if (_selectionVisual is not null) _viewport.Children.Remove(_selectionVisual);

            _selectionVisual = SelectionHighlightFactory.CreateHighlight(SelectedNode, SelectionColor);
            if (_selectionVisual is not null) _viewport.Children.Add(_selectionVisual);
        }

        /// <summary>Rebuilds (or removes, for a null <paramref name="scene"/>) the
        /// <see cref="ShadingMode.Wireframe"/>-only edge overlay - see
        /// <see cref="WireframeVisualFactory"/>. <see cref="Render"/> is the only caller;
        /// kept as its own method for the same "swap out one tracked Visual3D" shape
        /// <see cref="RefreshSelectionHighlight"/> already uses.</summary>
        private void RefreshWireframeVisual(CoreScene? scene)
        {
            if (_wireframeVisual is not null) _viewport.Children.Remove(_wireframeVisual);

            _wireframeVisual = scene is not null ? WireframeVisualFactory.CreateSceneWireframe(scene) : null;
            if (_wireframeVisual is not null) _viewport.Children.Add(_wireframeVisual);
        }

        /// <summary>Switches <see cref="EditSession"/> onto <paramref name="node"/> (its
        /// selection cleared, matching <see cref="MeshEditSession.Attach"/>) and draws
        /// its (initially empty) marker overlay - <c>JolieCat3D.UI</c>'s cue that Edit
        /// Mode is now active for this node.</summary>
        public void EnterEditMode(CoreNode node)
        {
            ArgumentNullException.ThrowIfNull(node);
            EditSession.Attach(node);
            RefreshComponentOverlay();
        }

        /// <summary>Detaches <see cref="EditSession"/> (clearing its selection) and
        /// removes the marker overlay - <c>JolieCat3D.UI</c>'s cue to switch back to
        /// Object Mode.</summary>
        public void ExitEditMode()
        {
            EditSession.Attach(null);
            RefreshComponentOverlay();
        }

        /// <summary>Rebuilds the vertex/edge/face marker overlay from
        /// <see cref="EditSession"/>'s current target and selection (see
        /// <see cref="ComponentMarkerVisualFactory.CreateOverlay"/>) - call after any
        /// selection change made directly against <see cref="EditSession"/> (a
        /// vertex/edge/face pick via <c>Editing.ComponentHitTester</c>). <see cref="Render"/>
        /// and <see cref="Refresh"/> already call this themselves, so a caller never
        /// needs to after a <see cref="Editing.ComponentGizmo"/> drag - only after a
        /// plain click-to-select.</summary>
        public void RefreshComponentOverlay()
        {
            _componentOverlayVisual.Children.Clear();
            foreach (var visual in ComponentMarkerVisualFactory.CreateOverlay(EditSession))
                _componentOverlayVisual.Children.Add(visual);
        }
    }
}
