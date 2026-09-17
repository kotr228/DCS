using System.IO;
using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using JolieCat3D.Core.Camera;
using JolieCat3D.Core.Geometry;
using JolieCat3D.Core.Materials;
using JolieCat3D.Core.Numerics;
using JolieCat3D.Core.Scene;
using JolieCat3D.Core.Sculpting;
using JolieCat3D.Core.Skinning;
using JolieCat3D.Engine.Camera;
using JolieCat3D.Engine.Editing;
using JolieCat3D.Engine.Gizmos;
using JolieCat3D.Engine.Lighting;
using JolieCat3D.Engine.Rendering;
using JolieCat3D.Service;
using JolieCat3D.Service.Animation;
using JolieCat3D.Service.Commands;
using JolieCat3D.Service.Export;
using JolieCat3D.Service.Interop;
using JolieCat3D.UI.ViewModels;
using Microsoft.Win32;

namespace JolieCat3D.UI
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        // "3D Models (*.obj;*.stl)" first, so it's the default choice in both dialogs;
        // the format-specific entries after it are what actually determine each format's
        // own default *extension* SaveFileDialog appends when a user types a bare name
        // with no extension at all.
        private const string FileDialogFilter =
            $"{MeshFileService.AnyMeshFilter}|{MeshFileService.ObjFilter}|{MeshFileService.StlFilter}";

        private readonly Scene3DRenderer _renderer;
        private readonly TransformGizmo _gizmo;
        private readonly ComponentGizmo _componentGizmo;
        private readonly SceneViewModel _sceneViewModel = new();

        // Where a node's currently-loaded diffuse texture came from - either a plain
        // image file, or one layer of a whole .jolie project (see
        // LoadFromJolieProjectButton_Click) - so a later JolieWorkspaceWatcher.AssetChanged
        // for that same path knows how to redo the load (see ReloadNodeTexture). Keyed by
        // the Core Node itself, not its Material, since a node's material can be swapped
        // out entirely by an Import/Open and the tracking should just quietly stop
        // mattering at that point rather than point at a stale material instance.
        private readonly Dictionary<Node, TextureSource> _textureSources = new();
        private JolieWorkspaceWatcher? _workspaceWatcher;

        // The 3D animation timeline (Core.Scene.Node Position/Rotation/Scale keyframes -
        // see JolieCat3D.Service.Animation's own remarks) plus the WPF-side clock that
        // actually advances it: a DispatcherTimer ticking at a fixed ~60Hz regardless of
        // the timeline's own FrameRate (that's a display/scrubber unit only - see
        // AnimationTimeline.FrameRate's own remarks), each tick moving CurrentTime
        // forward by its own nominal interval and pushing the result onto every
        // animated node via Apply().
        private readonly AnimationTimeline _timeline = new();
        private readonly DispatcherTimer _playbackTimer;
        private bool _isUpdatingAnimationUI;
        private bool _isUpdatingCameraViewUI;

        // WPF can (and does) invoke a XAML-wired event handler (Checked/Unchecked,
        // TextChanged, SelectedItemChanged, ...) SYNCHRONOUSLY from inside
        // InitializeComponent() itself, the moment a declared initial value is applied -
        // a RadioButton/CheckBox's own IsChecked="True", a TextBox's own Text="1" - well
        // before this window's own constructor has reached the point where _renderer/
        // _gizmo/_componentGizmo (all assigned later in the constructor, never via a
        // field initializer) actually exist. Every one of this window's own event
        // handlers checks this FIRST and returns immediately if it isn't set yet -
        // flipped to true as the very last statement in the constructor, once every
        // field any handler could possibly touch is guaranteed to be assigned.
        private bool _isInitialized;

        // Guards Viewport_MouseLeftButtonDown against the reentrant call WPF itself makes
        // synchronously from inside GizmoHitTester.BeginDrag's own RaiseEvent - see
        // TryBeginGizmoDrag's own remarks for exactly why that reentrant call happens
        // (an observed System.StackOverflowException, not a hypothetical). Set for the
        // duration of that one RaiseEvent call and nothing else, so it has no effect at
        // all on any later, genuinely separate click.
        private bool _isDispatchingGizmoMouseDown;

        // Guards RenderAnimationFramesMenuItem_Click against a second, overlapping
        // render being started while one is already in flight - see that method's own
        // remarks.
        private bool _isRenderingFrames;

        // The Undo/Redo stack (see Service.Commands.CommandHistory's own remarks) -
        // every gizmo drag (via _gizmo.TransformCommitted, wired below) and every
        // Extrude/Subdivide (see ExtrudeButton_Click/SubdivideButton_Click) is recorded
        // here; ApplicationCommands.Undo/Redo (bound below - their own default gestures
        // ARE Ctrl+Z/Ctrl+Y) drive it. Cleared whenever the whole scene is replaced (see
        // LoadScene) - a recorded command references THAT scene's own Node/Mesh
        // instances, meaningless against a completely different one.
        private readonly CommandHistory _commandHistory = new();

        private Scene3D _currentScene = new("Untitled");
        private string? _currentFilePath;

        private sealed record TextureSource(string SourcePath, bool IsJolieProject);

        public MainWindow()
        {
            InitializeComponent();

            DataContext = _sceneViewModel;

            // The dark professional viewport preset (background gradient + matching
            // lighting) shared with any other JolieCat3D window that wants the same
            // BlackCat/JolieCat-aligned look - see ViewportTheme's own remarks for why
            // this isn't just baked into Scene3DRenderer's/LightingSettings' own defaults.
            Viewport.Background = ViewportTheme.CreateDarkBackground();

            _renderer = new Scene3DRenderer(Viewport) { Lighting = ViewportTheme.CreateDarkThemeLighting() };
            _gizmo = new TransformGizmo(Viewport) { SceneNodes = () => _currentScene.Traverse() };
            _componentGizmo = new ComponentGizmo(Viewport);

            // A gizmo drag changes the same Core Node a NodeViewModel wraps directly
            // (TransformGizmo has no idea NodeViewModel exists) - re-render the mesh at
            // its new transform, and tell the matching view model (if the edited node is
            // the one currently shown) to re-read Position/Rotation/Scale, or the
            // Properties Inspector would keep showing the pre-drag values.
            _gizmo.TransformChanged += (_, _) =>
            {
                _renderer.Refresh();
                _sceneViewModel.FindViewModel(_gizmo.Target)?.SyncFromCore();
            };

            // Camera Piloting: live viewport navigation, while "View > Active Camera" is
            // toggled on, writes straight onto Scene3D.ActiveCamera's own transform (see
            // Scene3DRenderer.OnPilotedCameraChanged) - resync the Properties Inspector
            // if that node happens to be the one currently shown there, the same reason
            // _gizmo.TransformChanged's own handler exists. Deliberately does NOT call
            // _renderer.Refresh() here - only the camera itself changed, not any mesh
            // geometry, so there is nothing for a full re-render to pick up.
            _renderer.ActiveCameraPiloted += () =>
            {
                if (_currentScene.ActiveCamera is { } cameraNode)
                    _sceneViewModel.FindViewModel(cameraNode)?.SyncFromCore();
            };

            // A WHOLE gizmo drag gesture just ended (see TransformGizmo.TransformCommitted's
            // own remarks) - record it as ONE undoable command, not one per
            // TransformChanged tick. The command's own onChanged callback mirrors
            // TransformChanged's handler above exactly, since Undo/Redo need the same
            // "re-render + resync the Properties Inspector" refresh a live drag tick does.
            _gizmo.TransformCommitted += (_, args) =>
            {
                var command = new TransformNodeCommand(args.Target, args.Before, args.After, "Transform Object", onChanged: () =>
                {
                    _renderer.Refresh();
                    _gizmo.Refresh();
                    _sceneViewModel.FindViewModel(args.Target)?.SyncFromCore();
                });
                _commandHistory.Record(command);
            };

            // A component-gizmo drag mutates the target mesh's own vertex positions
            // directly (see MeshEditSession.ApplyTranslation) - Refresh() re-renders the
            // scene from that updated Core.Geometry.Mesh AND rebuilds the marker overlay
            // (Scene3DRenderer.Refresh already calls RefreshComponentOverlay itself), so
            // both the mesh on screen and its vertex/edge/face dots catch up to the drag
            // together - the "immediate GPU geometry update" Edit Mode needs.
            _componentGizmo.EditApplied += (_, _) => _renderer.Refresh();

            // A WHOLE component-gizmo drag gesture just ended (see
            // ComponentGizmo.TranslationCommitted's own remarks) - record it as ONE
            // undoable command, not one per EditApplied tick, mirroring _gizmo's own
            // TransformCommitted wiring above exactly (same "re-render, reposition the
            // gizmo, resync the Properties Inspector" onChanged callback shape).
            _componentGizmo.TranslationCommitted += (_, args) =>
            {
                var command = new VertexTranslateCommand(args.Target, args.Changes, "Move Vertices", onChanged: () =>
                {
                    _renderer.Refresh();
                    _componentGizmo.Refresh();
                    _sceneViewModel.FindViewModel(args.Target)?.SyncFromCore();
                });
                _commandHistory.Record(command);
            };

            // The reverse direction: a Properties Inspector field edit changes the Core
            // Node directly through its NodeViewModel - re-render the mesh and
            // reposition the gizmo (which sits at the node's world position) to match.
            _sceneViewModel.SceneChanged += (_, _) =>
            {
                _renderer.Refresh();
                _gizmo.Refresh();
            };

            // File > New/Open/Save/Save As bind to WPF's own standard commands rather
            // than ad-hoc ones - their default gestures (Ctrl+N/O/S, ...) and
            // InputGestureText both come for free, and MenuItem.Command="ApplicationCommands.X"
            // resolves them directly in XAML with no further wiring needed there.
            CommandBindings.Add(new CommandBinding(ApplicationCommands.New, (_, _) => NewScene()));
            CommandBindings.Add(new CommandBinding(ApplicationCommands.Open, (_, _) => OpenScene()));
            CommandBindings.Add(new CommandBinding(ApplicationCommands.Save, (_, _) => SaveScene()));
            CommandBindings.Add(new CommandBinding(ApplicationCommands.SaveAs, (_, _) => SaveSceneAs()));

            // Ctrl+Z/Ctrl+Y - ApplicationCommands.Undo/Redo's own DEFAULT gestures
            // already are exactly that, the same "let WPF's own standard command supply
            // the gesture/InputGestureText for free" reasoning as New/Open/Save/SaveAs
            // above. CanExecute is wired to CommandHistory's own CanUndo/CanRedo so the
            // key (and any menu item bound to the same command) is a no-op, not a
            // confusing error, once there's genuinely nothing left to undo/redo.
            CommandBindings.Add(new CommandBinding(ApplicationCommands.Undo,
                (_, _) => _commandHistory.Undo(),
                (_, e) => e.CanExecute = _commandHistory.CanUndo));
            CommandBindings.Add(new CommandBinding(ApplicationCommands.Redo,
                (_, _) => _commandHistory.Redo(),
                (_, e) => e.CanExecute = _commandHistory.CanRedo));

            // A watched JolieWorkspaceWatcher owns a real OS file-system handle - stop
            // and dispose it when the window closes rather than leaking it for the rest
            // of the process's lifetime.
            Closed += (_, _) => _workspaceWatcher?.Dispose();

            // The playback transport's own clock - runs for the window's whole lifetime
            // (stopped on Closed, alongside the workspace watcher above); each tick is a
            // no-op unless _timeline.IsPlaying (see OnPlaybackTick), so idling at frame 0
            // with nothing playing costs only the empty check every ~16ms.
            _playbackTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.0 / 60.0) };
            _playbackTimer.Tick += OnPlaybackTick;
            _playbackTimer.Start();
            Closed += (_, _) => _playbackTimer.Stop();

            LoadScene(BuildDemoScene(), filePath: null);
            RefreshAnimationUI();
            RefreshLiveSyncStatus();

            // Only past this point is every field this window's own event handlers touch
            // actually assigned - see _isInitialized's own remarks.
            _isInitialized = true;
        }

        // ================= File menu =================

        /// <summary>Replaces the whole current scene - the Outliner, viewport, gizmo,
        /// and selection all reset together, and <paramref name="filePath"/> becomes
        /// what <see cref="SaveScene"/> writes back to (null, for a scene that isn't
        /// backed by a file yet - New, or the built-in demo scene at startup).</summary>
        private void LoadScene(Scene3D scene, string? filePath)
        {
            _currentScene = scene;
            _currentFilePath = filePath;

            SelectNode(null);
            _sceneViewModel.Load(scene);
            _renderer.Render(scene);

            // Every recorded command references THIS scene's own Node/Mesh instances -
            // undoing one against a completely different, freshly loaded scene would be
            // meaningless (see CommandHistory.Clear's own remarks), so a whole-scene
            // replacement always starts Undo/Redo completely fresh too.
            _commandHistory.Clear();

            // _textureSources keys are the PREVIOUS scene's own Node instances - keeping
            // them around after that whole scene is gone is a real, unbounded memory leak
            // over a session that opens/creates many scenes in turn (every one of THEIR
            // own now-unreachable nodes stays referenced here forever, keeping the
            // Dictionary - and every Node it points to - alive well past the point
            // anything else in the app can still reach them). A fresh scene starts with
            // no tracked texture sources of its own regardless, so this is never a loss.
            _textureSources.Clear();

            Title = $"JolieCat3D - {(filePath is null ? "Untitled" : Path.GetFileName(filePath))}";
        }

        private void NewScene()
        {
            if (!ConfirmDiscardIfNeeded("starting a new scene")) return;
            LoadScene(new Scene3D("Untitled"), filePath: null);
        }

        private void OpenScene()
        {
            if (!ConfirmDiscardIfNeeded("opening another file")) return;

            var dialog = new OpenFileDialog { Filter = FileDialogFilter, Title = "Open" };
            if (dialog.ShowDialog(this) != true) return;

            TryRun("Open", () => LoadScene(MeshFileService.ImportScene(dialog.FileName), dialog.FileName));
        }

        private void SaveScene()
        {
            if (_currentFilePath is null) { SaveSceneAs(); return; }
            TryRun("Save", () => MeshFileService.ExportScene(_currentScene, _currentFilePath));
        }

        private void SaveSceneAs()
        {
            var dialog = new SaveFileDialog { Filter = FileDialogFilter, Title = "Save As", FileName = _currentFilePath ?? "Untitled.obj" };
            if (dialog.ShowDialog(this) != true) return;

            TryRun("Save", () =>
            {
                MeshFileService.ExportScene(_currentScene, dialog.FileName);
                _currentFilePath = dialog.FileName;
                Title = $"JolieCat3D - {Path.GetFileName(_currentFilePath)}";
            });
        }

        /// <summary>Adds a file's content into the current scene as new root node(s) -
        /// unlike Open, which replaces the whole scene, Import composes (a second model
        /// brought in alongside whatever's already there).</summary>
        private void ImportMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;

            var dialog = new OpenFileDialog { Filter = FileDialogFilter, Title = "Import" };
            if (dialog.ShowDialog(this) != true) return;

            TryRun("Import", () =>
            {
                var imported = MeshFileService.ImportScene(dialog.FileName);
                foreach (var root in imported.RootNodes) _currentScene.AddRootNode(root);

                _sceneViewModel.Load(_currentScene);
                _renderer.Render(_currentScene);
            });
        }

        /// <summary>File > Scene > "Add Cube"/"Add Sphere"/"Add Plane"/"Add Cylinder" -
        /// one new root node wrapping a default-settings <see cref="Primitives"/> mesh
        /// each. Dynamic scene population never participates in
        /// <see cref="_commandHistory"/> (adding a node isn't a transform/mesh-edit
        /// command in the sense that system models - see <see cref="ExtrudeButton_Click"/>'s
        /// own remarks on what DOES) - the same precedent <see cref="ImportMenuItem_Click"/>'s
        /// own new root nodes already set.</summary>
        private void AddCubeMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            AddNodeToScene(new Node("Cube") { Mesh = Primitives.CreateCube() });
        }

        private void AddSphereMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            AddNodeToScene(new Node("Sphere") { Mesh = Primitives.CreateSphere() });
        }

        private void AddPlaneMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            AddNodeToScene(new Node("Plane") { Mesh = Primitives.CreatePlane() });
        }

        private void AddCylinderMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            AddNodeToScene(new Node("Cylinder") { Mesh = Primitives.CreateCylinder() });
        }

        /// <summary>File > Scene > "Add Camera" - a new root node carrying a default
        /// <see cref="CameraData"/>, immediately selectable/editable exactly like a
        /// meshed node (its own Position/Rotation/Scale drive where it looks - see
        /// <see cref="Node.GetWorldForward"/>'s own remarks - and it can be transformed/
        /// keyframed on the timeline the same way). Not yet THE active camera (see
        /// <see cref="SetActiveCameraButton_Click"/>) - adding one never silently
        /// changes what the viewport is currently looking through.</summary>
        private void AddCameraMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            AddNodeToScene(new Node("Camera") { Camera = new CameraData() });
        }

        /// <summary>File > Scene > "Add Light" - a new root node carrying a default
        /// (Directional) <see cref="LightData"/> - see <see cref="AddCameraMenuItem_Click"/>'s
        /// own remarks; unlike a camera, every light node ALWAYS contributes to the
        /// scene's own lighting the moment it exists (see
        /// <see cref="SceneLightingFactory.CreateSceneLights"/>), no separate
        /// "active"/"set as active" step needed.</summary>
        private void AddLightMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            AddNodeToScene(new Node("Light") { Light = new LightData() });
        }

        /// <summary>File > Scene > "Add Curve" - a new root node carrying a default
        /// <see cref="CurveData"/> (a simple 3-point gentle bend, handles left at their
        /// own default "coincident with the point" position - a straight-segment curve
        /// until the Curve Inspector's own points panel moves a handle) - see
        /// <see cref="AddCameraMenuItem_Click"/>'s own remarks. <see cref="Node.Mesh"/>
        /// is set immediately from <see cref="CurveData.GenerateMesh"/> (not left null
        /// until the first Inspector edit), so the new curve is visible in the viewport
        /// the instant it's added, exactly like every other "Add ..." primitive.</summary>
        private void AddCurveMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;

            var curve = new CurveData();
            curve.Points.Add(new CurvePoint(new System.Numerics.Vector3(-1f, 0f, 0f)));
            curve.Points.Add(new CurvePoint(new System.Numerics.Vector3(0f, 1f, 0f)));
            curve.Points.Add(new CurvePoint(new System.Numerics.Vector3(1f, 0f, 0f)));

            AddNodeToScene(new Node("Curve") { Curve = curve, Mesh = curve.GenerateMesh() });
        }

        /// <summary>File > Scene > "Add Armature" - a new root node carrying
        /// <see cref="ArmatureData"/>, plus one root <see cref="BoneData"/> bone
        /// already attached to it (an armature with zero bones has nothing to select/
        /// rotate/skin against, so this never leaves one in that useless state) - see
        /// <see cref="AddCameraMenuItem_Click"/>'s own remarks on the shared "Add ..."
        /// shape. The new bone's own rest pose is captured immediately (see
        /// <see cref="BoneData.CaptureRestPose"/>), while its <see cref="Node.LocalRotation"/>
        /// is still Identity - exactly the moment a bone's own rest pose is meant to be
        /// captured.</summary>
        private void AddArmatureMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;

            var armature = new Node("Armature") { Armature = new ArmatureData() };
            var rootBone = new Node("Bone") { Bone = new BoneData { Head = Vector3.Zero, Tail = new Vector3(0f, 1f, 0f) } };
            armature.AddChild(rootBone);
            rootBone.Mesh = rootBone.Bone!.GenerateMesh();
            rootBone.Bone.CaptureRestPose(rootBone);

            AddNodeToScene(armature);
        }

        /// <summary>File > Scene > "Add Bone" - appends a new bone as a CHILD of
        /// whichever Armature/Bone node is currently selected (never as a new root -
        /// see <see cref="AddNodeToScene"/>'s own remarks on why every other "Add ..."
        /// item DOES add a root, and why this one deliberately does not: a bone with no
        /// parent bone/armature isn't part of any skeleton at all). Attached at the
        /// parent bone's own Tail when the parent IS a bone (Blender's own "connected
        /// child bone" convention - see <see cref="BoneData"/>'s own remarks on Head/
        /// Tail), or at the local origin when the parent is the Armature root itself.</summary>
        private void AddBoneMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;

            if (_sceneViewModel.SelectedNode?.UnderlyingNode is not { } parent || (parent.Bone is null && parent.Armature is null))
            {
                MessageBox.Show(this, "Select an Armature or Bone node first, to attach the new bone to it.", "JolieCat3D", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var bone = new Node("Bone")
            {
                Bone = new BoneData { Head = Vector3.Zero, Tail = new Vector3(0f, 1f, 0f) },
                LocalPosition = parent.Bone?.Tail ?? Vector3.Zero,
            };
            parent.AddChild(bone);
            bone.Mesh = bone.Bone!.GenerateMesh();
            bone.Bone.CaptureRestPose(bone);

            _sceneViewModel.Load(_currentScene);
            _renderer.Render(_currentScene, zoomToFit: false);
        }

        /// <summary>Adds <paramref name="node"/> as a new root of <see cref="_currentScene"/>
        /// and refreshes the Outliner/viewport immediately - the shared plumbing every
        /// File > Scene > "Add ..." handler above uses, so a freshly added primitive/
        /// camera/light node is instantly visible in the Outliner (<see cref="SceneViewModel.Load"/>
        /// rebuilds the whole tree), selectable via <see cref="Scene3DRenderer.HitTest"/>
        /// (which reads straight from <see cref="_currentScene"/>/<c>Editing.MeshEditSession</c>'s
        /// own live state, nothing cached to go stale), and rendered
        /// (<see cref="Scene3DRenderer.Render"/>) all in one call - never zooming to fit,
        /// so adding a second object doesn't yank the camera away from whatever the user
        /// was already looking at.</summary>
        private void AddNodeToScene(Node node)
        {
            _currentScene.AddRootNode(node);
            _sceneViewModel.Load(_currentScene);
            _renderer.Render(_currentScene, zoomToFit: false);
        }

        /// <summary>The Camera Inspector's "Set as Active Camera" button - designates the
        /// currently selected node <see cref="Scene3D.ActiveCamera"/>. Does NOT, on its
        /// own, change what the viewport is currently looking through - see
        /// <see cref="Scene3D.ActiveCamera"/>'s own remarks; a separate, explicit "View >
        /// Active Camera" toggle (<see cref="ActiveCameraViewCheckBox_Changed"/> / Numpad
        /// 0) is what actually locks/pilots the viewport onto it.</summary>
        private void SetActiveCameraButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            if (_sceneViewModel.SelectedNode?.UnderlyingNode is not { } node) return;

            _currentScene.ActiveCamera = node;
            _renderer.Refresh();
        }

        /// <summary>The Camera toolbar's "Camera View" checkbox - "View > Active Camera"
        /// turned on/off (see <see cref="Scene3DRenderer.EnterActiveCameraView"/>/
        /// <see cref="Scene3DRenderer.ExitActiveCameraView"/>), also reachable via Numpad
        /// 0 (see <see cref="MainWindow_PreviewKeyDown"/>, which just flips this same
        /// checkbox so both paths share this one method). Checking it with no
        /// <see cref="Scene3D.ActiveCamera"/> set yet reports that back to the user and
        /// un-checks itself again, rather than silently doing nothing.
        /// <see cref="_isUpdatingCameraViewUI"/> guards the programmatic un-check below
        /// from re-entering this same handler as an "Unchecked" event.</summary>
        private void ActiveCameraViewCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            if (_isUpdatingCameraViewUI) return;

            if (ActiveCameraViewCheckBox.IsChecked == true)
            {
                if (_renderer.EnterActiveCameraView()) return;

                _isUpdatingCameraViewUI = true;
                try { ActiveCameraViewCheckBox.IsChecked = false; }
                finally { _isUpdatingCameraViewUI = false; }

                MessageBox.Show(this,
                    "Set a camera as the Active Camera first (Camera Inspector > \"Set as Active Camera\").",
                    "JolieCat3D", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                _renderer.ExitActiveCameraView();
            }
        }

        /// <summary>Writes the current scene out to a file without adopting it as "the"
        /// working file the way Save/Save As do - <see cref="_currentFilePath"/> (and
        /// the window title) are left exactly as they were, so a one-off "send a copy as
        /// STL" doesn't silently redirect a later Ctrl+S away from the OBJ someone's
        /// actually been editing.</summary>
        private void ExportMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;

            var dialog = new SaveFileDialog { Filter = FileDialogFilter, Title = "Export", FileName = _currentFilePath ?? "Untitled.obj" };
            if (dialog.ShowDialog(this) != true) return;

            TryRun("Export", () => MeshFileService.ExportScene(_currentScene, dialog.FileName));
        }

        /// <summary>File > "Export GLB/glTF..." - the modern, animation/PBR-capable
        /// counterpart to <see cref="ExportMenuItem_Click"/>'s own OBJ/STL (see
        /// <see cref="GltfExporter"/>'s own remarks): a single self-contained
        /// <c>.glb</c> carrying the real node hierarchy, every material's PBR channels
        /// (diffuse/normal/metallic-roughness textures included), and every keyframe
        /// currently on <see cref="_timeline"/> - a separate, parallel File menu action
        /// from <see cref="ExportMenuItem_Click"/> rather than one more
        /// <see cref="MeshFileService"/> format, the same "animation export is its own
        /// path" precedent <see cref="ExportAnimationMenuItem_Click"/> already set (OBJ/STL
        /// take only a <c>Scene3D</c>; this needs the timeline too).</summary>
        private void ExportGltfMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;

            var dialog = new SaveFileDialog
            {
                Filter = "glTF Binary (*.glb)|*.glb|glTF (*.gltf)|*.gltf",
                Title = "Export GLB/glTF",
                FileName = Path.GetFileNameWithoutExtension(_currentFilePath) is { Length: > 0 } baseName ? baseName + ".glb" : "Untitled.glb",
            };
            if (dialog.ShowDialog(this) != true) return;

            TryRun("Export GLB/glTF", () => GltfExporter.Export(_currentScene, _timeline, dialog.FileName));
        }

        /// <summary>File > Scene > "Set Skybox..." - picks ONE of a skybox's own 6 face
        /// image files (see <see cref="EnvironmentSettings.SkyboxSource"/>'s own remarks
        /// on the naming convention every face is expected to follow) and derives the
        /// shared prefix the other 5 are expected to share, rather than asking for a
        /// whole folder - a plain <see cref="OpenFileDialog"/> is already the established
        /// picker everywhere else in this window (see <see cref="LoadTextureButton_Click"/>),
        /// so this reuses that same, familiar flow instead of introducing a folder-browse
        /// dialog type this project has never needed before. Immediately re-renders so
        /// the skybox visual and its environment tint (see
        /// <see cref="MaterialFactory.EnvironmentTint"/>) take effect without needing any
        /// other trigger.</summary>
        private void SetSkyboxMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;

            var dialog = new OpenFileDialog
            {
                Filter = "Skybox face image (e.g. cube_f.jpg)|*.jpg;*.jpeg;*.png;*.bmp",
                Title = "Set Skybox - pick any ONE of its 6 face images",
            };
            if (dialog.ShowDialog(this) != true) return;

            TryRun("Set Skybox", () =>
            {
                if (DeriveSkyboxPrefix(dialog.FileName) is not { } prefix)
                {
                    MessageBox.Show(this,
                        "The chosen file doesn't look like one of a skybox's own 6 named faces "
                        + "(expected a name ending in _f/_b/_l/_r/_u/_d before its extension, e.g. 'cube_f.jpg').",
                        "JolieCat3D", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                _currentScene.Environment = new EnvironmentSettings { SkyboxSource = prefix };
                _renderer.Render(_currentScene, zoomToFit: false);
            });
        }

        /// <summary>File > Scene > "Clear Skybox" - back to no environment at all
        /// (<see cref="Scene3D.Environment"/> null), matching every scene that never had
        /// one set.</summary>
        private void ClearSkyboxMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;

            _currentScene.Environment = null;
            _renderer.Render(_currentScene, zoomToFit: false);
        }

        private static readonly string[] SkyboxFaceSuffixes = { "_f", "_b", "_l", "_r", "_u", "_d" };

        /// <summary>Strips a recognized face suffix (and extension) off
        /// <paramref name="facePath"/> to recover the shared prefix every one of a
        /// skybox's own 6 faces is expected to share - null if the file name doesn't end
        /// in one of <see cref="EnvironmentSettings.SkyboxSource"/>'s own documented
        /// suffixes at all.</summary>
        private static string? DeriveSkyboxPrefix(string facePath)
        {
            var withoutExtension = Path.Combine(Path.GetDirectoryName(facePath) ?? string.Empty, Path.GetFileNameWithoutExtension(facePath));

            foreach (var suffix in SkyboxFaceSuffixes)
                if (withoutExtension.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    return withoutExtension[..^suffix.Length];

            return null;
        }

        /// <summary>The Camera toolbar's Perspective/Orthographic toggle - swaps the
        /// viewport's own projection (see <see cref="CameraFraming.SetPerspective"/>/
        /// <see cref="CameraFraming.SetOrthographic"/>), preserving the current vantage
        /// point exactly - only the PROJECTION changes.</summary>
        private void CameraProjectionButton_Checked(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            if (sender is not RadioButton { Tag: string modeName }) return;

            if (modeName == "Orthographic") CameraFraming.SetOrthographic(Viewport);
            else CameraFraming.SetPerspective(Viewport);
        }

        /// <summary>The Camera toolbar's Top/Bottom/Front/Back/Left/Right buttons -
        /// aligns the viewport to that preset (see <see cref="CameraFraming.AlignToPreset"/>),
        /// framed on the currently selected node when one exists, or the whole scene
        /// otherwise - the same "prefer the selection, fall back to the whole scene"
        /// precedent <see cref="CameraFraming"/>'s own <c>ZoomToFit</c> overloads already
        /// establish.</summary>
        private void ViewPresetButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            if (sender is not Button { Tag: string presetName }) return;
            if (!Enum.TryParse<ViewPreset>(presetName, out var preset)) return;

            CameraFraming.AlignToPreset(Viewport, preset, _currentScene, _sceneViewModel.SelectedNode?.UnderlyingNode);
        }

        /// <summary>The Camera toolbar's "Grid" checkbox - shows/hides the reference grid
        /// (<see cref="Scene3DRenderer.ShowGrid"/>).</summary>
        private void ShowGridCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            _renderer.ShowGrid = ShowGridCheckBox.IsChecked == true;
        }

        /// <summary>The Camera toolbar's "Shadows" checkbox - <see cref="Scene3DRenderer.ShowShadows"/>.</summary>
        private void ShowShadowsCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            _renderer.ShowShadows = ShowShadowsCheckBox.IsChecked == true;
        }

        /// <summary>The Camera toolbar's "Anti-Aliasing" checkbox - <see cref="Scene3DRenderer.AntiAliasingEnabled"/>.</summary>
        private void AntiAliasingCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            _renderer.AntiAliasingEnabled = AntiAliasingCheckBox.IsChecked == true;
        }

        /// <summary>The Camera toolbar's "SSAO" checkbox - <see cref="Scene3DRenderer.ShowAmbientOcclusion"/>.</summary>
        private void SsaoCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            _renderer.ShowAmbientOcclusion = SsaoCheckBox.IsChecked == true;
        }

        /// <summary>The Camera toolbar's "Bloom" checkbox - shows/hides
        /// <see cref="BloomOverlayRectangle"/> (see its own XAML remarks). Nothing here
        /// touches <see cref="Scene3DRenderer"/> at all: Bloom, unlike Shadows/SSAO/
        /// Anti-Aliasing, is a pure 2D compositing effect layered OVER the already-
        /// rendered viewport, not anything added to the 3D scene itself.</summary>
        private void BloomCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            BloomOverlayRectangle.Visibility = BloomCheckBox.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>The Camera toolbar's grid size field - keeps the reference grid
        /// visual (<see cref="Scene3DRenderer.GridSize"/>) AND both gizmos' own
        /// Ctrl-held-drag snap increment (<see cref="TransformGizmo.GridSize"/>/
        /// <see cref="ComponentGizmo.GridSize"/>) in step with the same one value, so
        /// what's drawn on the ground is exactly what a snapped drag actually snaps to.
        /// Silently ignores anything that doesn't parse as a positive number (the same
        /// "an in-progress/invalid edit just isn't applied yet, never a crash" tolerance
        /// <see cref="FpsTextBox_TextChanged"/> already has) rather than validating/
        /// blocking the TextBox itself.</summary>
        private void GridSizeTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isInitialized) return;
            if (!float.TryParse(GridSizeTextBox.Text, out var size) || size <= 0f) return;

            _renderer.GridSize = size;
            _gizmo.GridSize = size;
            _componentGizmo.GridSize = size;
        }

        /// <summary>Writes the whole <see cref="_timeline"/> (every node's transform
        /// keyframes, every material's texture-frame track) out to a file - the
        /// SaveFileDialog's own two filter entries pick which of the two formats this
        /// application supports: <see cref="AnimationExporter.ExportJson"/> (this app's
        /// own native, full-fidelity interchange JSON - "*.j3danim.json" by convention),
        /// the default/first filter, or <see cref="JolieAnimationExporter.ExportTimelineTracksJson"/>
        /// (a JolieCat-2D-compatible "TimelineTracks" fragment - keyframe TIMES only, no
        /// transform curves - see that class's own remarks on why), the second. Neither
        /// touches <see cref="_currentFilePath"/> - exporting animation data is a
        /// separate concern from "the file Save/Save As write the scene itself to",
        /// the same distinction <see cref="ExportMenuItem_Click"/> already draws for
        /// mesh data.</summary>
        private void ExportAnimationMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;

            var dialog = new SaveFileDialog
            {
                Filter = "JolieCat3D Animation (*.j3danim.json)|*.j3danim.json|JolieCat-Compatible Timeline (*.json)|*.json",
                Title = "Export Animation",
                FileName = "Animation.j3danim.json",
            };
            if (dialog.ShowDialog(this) != true) return;

            TryRun("Export Animation", () =>
            {
                if (dialog.FilterIndex == 2) JolieAnimationExporter.ExportTimelineTracksJson(_timeline, dialog.FileName);
                else AnimationExporter.ExportJson(_timeline, dialog.FileName);
            });
        }

        /// <summary>Bakes the current <see cref="_timeline"/> out to a numbered PNG
        /// sequence, one <see cref="ViewportCaptureService.CaptureFrameAsync"/> per frame
        /// across the timeline's own <see cref="AnimationTimeline.TotalFrames"/> - the
        /// "render out this animation as pictures" counterpart to
        /// <see cref="ExportAnimationMenuItem_Click"/>'s "save the CURVES themselves"
        /// (see <see cref="ViewportCaptureService"/>'s own remarks on why a caller-driven
        /// per-frame callback, rather than this method knowing anything about
        /// <see cref="AnimationTimeline"/> itself, is what actually advances the scene
        /// between captures). Runs via <see cref="ViewportCaptureService.CaptureSequenceAsync"/>,
        /// so this whole method is itself <c>async</c> - a many-hundred-frame render
        /// would otherwise freeze the window for its entire duration (no Cancel button,
        /// no repaint, an unresponsive title bar) the way a fully synchronous version
        /// of this same loop would. <see cref="_isRenderingFrames"/> refuses a second,
        /// overlapping render (two capture loops racing over the same
        /// <see cref="_timeline"/>/viewport would corrupt both) rather than queuing or
        /// silently ignoring the second click. Playback is paused first (a render
        /// capture scrubbing frame by frame while <see cref="AnimationTimeline.IsPlaying"/>
        /// was also independently advancing the SAME timeline from
        /// <see cref="OnPlaybackTick"/> would fight over <see cref="AnimationTimeline.CurrentTime"/>),
        /// and resumed afterward (success OR failure) only if it was actually playing
        /// before.</summary>
        private async void RenderAnimationFramesMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            if (_isRenderingFrames)
            {
                MessageBox.Show(this, "A render is already in progress - please wait for it to finish.",
                    "JolieCat3D", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new SaveFileDialog
            {
                Filter = "PNG Image Sequence (*.png)|*.png",
                Title = "Render Animation Frames",
                FileName = "Frame.png",
            };
            if (dialog.ShowDialog(this) != true) return;

            await TryRunAsync("Render Animation Frames", async () =>
            {
                var outputDirectory = Path.GetDirectoryName(dialog.FileName);
                if (string.IsNullOrEmpty(outputDirectory)) outputDirectory = ".";
                var baseFileName = Path.GetFileNameWithoutExtension(dialog.FileName);

                var wasPlaying = _timeline.IsPlaying;
                _timeline.Pause();

                _isRenderingFrames = true;
                try
                {
                    var paths = await ViewportCaptureService.CaptureSequenceAsync(Viewport, _timeline.TotalFrames, frameIndex =>
                    {
                        _timeline.CurrentFrame = frameIndex;
                        _timeline.Apply();
                        _renderer.Refresh();
                    }, outputDirectory, baseFileName);

                    _sceneViewModel.SelectedNode?.SyncFromCore();
                    RefreshAnimationUI();

                    MessageBox.Show(this, $"Rendered {paths.Count} frame(s) to '{outputDirectory}'.",
                        "JolieCat3D", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                finally
                {
                    _isRenderingFrames = false;
                    if (wasPlaying) _timeline.Play();
                }
            });
        }

        private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            Close();
        }

        /// <summary>No dirty/unsaved-changes tracking exists yet (every scene edit -
        /// gizmo drag, Properties field, Import - would need to flip a flag this project
        /// doesn't have), so this always just asks - a harmless extra confirmation on an
        /// already-saved scene, and a real save against silently discarding one that
        /// isn't. Skipped entirely for a brand-new, still-Untitled scene with no file of
        /// its own (nothing meaningful to lose).</summary>
        private bool ConfirmDiscardIfNeeded(string action)
        {
            if (_currentFilePath is null && _currentScene.RootNodes.Count == 0) return true;

            var result = MessageBox.Show(
                $"Discard the current scene before {action}? Any unsaved changes will be lost.",
                "JolieCat3D", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
            return result == MessageBoxResult.Yes;
        }

        private void TryRun(string operationName, Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"{operationName} failed:\n{ex.Message}", "JolieCat3D", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>The async-<see cref="Task"/> twin of <see cref="TryRun"/> - for an
        /// operation (like <see cref="RenderAnimationFramesMenuItem_Click"/>'s own
        /// render loop) that itself needs to <c>await</c> without freezing this window
        /// in the meantime. Same catch-and-report behavior: any exception the awaited
        /// action lets through (a missing/inaccessible output directory, a locked file,
        /// a cancelled operation, ...) ends up as one friendly message box instead of an
        /// unhandled exception on this window's dispatcher.</summary>
        private async Task TryRunAsync(string operationName, Func<Task> action)
        {
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"{operationName} failed:\n{ex.Message}", "JolieCat3D", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Click-to-select in the viewport - in Object Mode, a whole node (see
        /// <see cref="SelectNode"/>); in Edit Mode, a single mesh component (see
        /// <see cref="HandleComponentClick"/>) of whichever node Edit Mode is currently
        /// targeting. Either way, only ever reached for a click the active gizmo's own
        /// handle didn't already claim first (see <see cref="TryBeginGizmoDrag"/>),
        /// so dragging a gizmo handle never gets misread as "clicked empty space,
        /// deselect/clear" partway through the gesture - WPF's own native 3D hit-testing
        /// alone can't be relied on for that (see <see cref="GizmoHitTester"/>'s own
        /// remarks on why a mesh occluding the manipulator's hit-test geometry can stop the
        /// click ever reaching it at all).
        /// </summary>
        private void Viewport_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!_isInitialized || _isDispatchingGizmoMouseDown) return;

            var position = e.GetPosition(Viewport);

            if (IsWeightPaintMode)
            {
                _isPaintingWeights = true;
                HandleWeightPaintClick(position);
                return;
            }

            if (IsVertexPaintMode)
            {
                _isPaintingVertexColors = true;
                HandleVertexPaintClick(position);
                return;
            }

            if (IsSculptMode)
            {
                _renderer.SculptSession.Invert = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
                _isSculpting = true;
                _renderer.SculptSession.BeginStroke(Viewport, position);
                _renderer.Refresh();
                return;
            }

            if (TryBeginGizmoDrag(position, e))
            {
                e.Handled = true;
                return;
            }

            if (IsEditMode)
            {
                HandleComponentClick(position, additive: Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
                return;
            }

            var hitNode = _renderer.HitTest(position);
            SelectNode(hitNode);
        }

        /// <summary>Checks <paramref name="position"/> against the currently-active gizmo's
        /// own handles - Translate/Scale arrows AND Rotate rings alike, whichever <c>Mode</c>
        /// currently has handles built (<see cref="ComponentGizmo.Handles"/> in Edit Mode,
        /// <see cref="TransformGizmo.Handles"/> otherwise - never both, mirroring which one
        /// is actually attached/visible at a time) - BEFORE either mode's own mesh/component
        /// selection logic runs, via <see cref="GizmoHitTester"/> - an explicit,
        /// occlusion-independent check WPF's own native 3D hit-testing on the manipulator
        /// itself can't be relied on for (see that class's own remarks - a Rotate ring
        /// suffered the exact same occlusion bug as a Translate arrow, so it gets the exact
        /// same fix here, not a separate one). Starts the hit handle's own real drag
        /// (<see cref="GizmoHitTester.BeginDrag"/>) and returns true the moment one is found,
        /// so the caller can consume <paramref name="sourceArgs"/> and skip selection
        /// entirely - a hit is never ambiguous with a selection click, since a gizmo handle
        /// is never itself a selectable mesh.
        ///
        /// <see cref="GizmoHitTester.BeginDrag"/>'s own <c>RaiseEvent</c> call is
        /// SYNCHRONOUS, and (confirmed by an actual observed <see cref="StackOverflowException"/>
        /// on real hardware, not just a theoretical concern) WPF's own class handling
        /// promotes an unhandled <c>Mouse.MouseDownEvent</c> into a fresh
        /// <c>MouseLeftButtonDownEvent</c> regardless of whether the manipulator's own
        /// <c>OnMouseDown</c> already marked the ORIGINAL event handled - and that
        /// promoted event bubbles right back out to this same <see cref="Viewport_MouseLeftButtonDown"/>
        /// handler, with the mouse still sitting on the very same handle, which would
        /// hit-test and dispatch again, forever - true of a Rotate ring's own drag exactly as
        /// much as a Translate arrow's, since the recursion is a property of RaiseEvent
        /// itself, not of which manipulator subtype raised it.
        /// <see cref="_isDispatchingGizmoMouseDown"/> is set for the exact duration of the
        /// <c>BeginDrag</c> call so that reentrant, WPF-generated invocation (and only that
        /// one - it happens nested inside THIS call's own stack frame, never on a later
        /// dispatcher tick) bails out immediately instead of hit-testing and dispatching
        /// again.</summary>
        private bool TryBeginGizmoDrag(Point position, MouseButtonEventArgs sourceArgs)
        {
            var handles = IsEditMode ? _componentGizmo.Handles : _gizmo.Handles;
            if (GizmoHitTester.HitTest(Viewport, handles, position) is not { } manipulator) return false;

            _isDispatchingGizmoMouseDown = true;
            try
            {
                GizmoHitTester.BeginDrag(manipulator, sourceArgs);
            }
            finally
            {
                _isDispatchingGizmoMouseDown = false;
            }

            return true;
        }

        /// <summary>Selection made from the Scene Outliner instead of a viewport click -
        /// the same <see cref="SelectNode"/> path either way, so the renderer's highlight,
        /// the gizmo, and the Properties Inspector all stay in sync regardless of which
        /// one the user actually clicked.</summary>
        private void SceneTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (!_isInitialized) return;
            SelectNode((e.NewValue as NodeViewModel)?.UnderlyingNode);
        }

        // ================= Scene Outliner drag-and-drop (reparenting) =================

        /// <summary>The data format key a drag payload is stored/read under - an
        /// in-process-only drag (the dragged value is a live <see cref="NodeViewModel"/>
        /// reference, never serialized), so this only ever needs to be unique within
        /// this window, not globally.</summary>
        private const string NodeDragDataFormat = "JolieCat3D.NodeViewModel";

        private Point _outlinerDragStartPoint;

        /// <summary>Records where a potential drag on this <see cref="TreeViewItem"/>
        /// started - <see cref="SceneTreeViewItem_PreviewMouseMove"/> compares against
        /// this to decide whether the mouse has moved far enough to actually BE a drag
        /// (as opposed to the small, inevitable mouse jitter of an ordinary click-to-select),
        /// the same threshold-based distinction every WPF drag-source implementation
        /// needs to make.</summary>
        private void SceneTreeViewItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) =>
            _outlinerDragStartPoint = e.GetPosition(null);

        /// <summary>Starts an actual drag (<see cref="DragDrop.DoDragDrop"/>) once the
        /// mouse has moved past the standard system drag threshold while the left
        /// button is held over a <see cref="TreeViewItem"/> - carries that item's own
        /// <see cref="NodeViewModel"/> as the payload (see
        /// <see cref="NodeDragDataFormat"/>). A plain click (no real drag) never reaches
        /// this far, so ordinary Outliner selection (<see cref="SceneTreeView_SelectedItemChanged"/>)
        /// is completely unaffected.</summary>
        private void SceneTreeViewItem_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed) return;
            if (sender is not TreeViewItem { DataContext: NodeViewModel nodeViewModel } item) return;

            var currentPosition = e.GetPosition(null);
            var delta = currentPosition - _outlinerDragStartPoint;
            if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;

            DragDrop.DoDragDrop(item, new DataObject(NodeDragDataFormat, nodeViewModel), DragDropEffects.Move);
        }

        private void SceneTreeViewItem_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = TryGetReparentPair(sender, e, out _, out _) ? DragDropEffects.Move : DragDropEffects.None;
            e.Handled = true;
        }

        /// <summary>Dropping ONTO a <see cref="TreeViewItem"/> re-parents the dragged
        /// node under that item's own node (<see cref="Scene3D.Reparent"/>). Marks
        /// <paramref name="e"/> handled unconditionally (even when the drop is rejected)
        /// so this bubbling <c>Drop</c> event never ALSO reaches
        /// <see cref="SceneTreeView_Drop"/>'s own "dropped into empty space" handling -
        /// a drop that landed ON an item is never ALSO an unparent-to-root drop.</summary>
        private void SceneTreeViewItem_Drop(object sender, DragEventArgs e)
        {
            e.Handled = true;
            if (!TryGetReparentPair(sender, e, out var draggedNode, out var targetNode)) return;

            ReparentNode(draggedNode, targetNode);
        }

        /// <summary>Dropping into the Outliner's own empty background space (anywhere a
        /// <see cref="TreeViewItem"/>'s own <c>Drop</c> handler above didn't already
        /// claim the event) un-parents the dragged node to the scene root - the "drag it
        /// out into empty space" half of the task's own drag-and-drop ask.</summary>
        private void SceneTreeView_Drop(object sender, DragEventArgs e)
        {
            if (e.Handled) return;
            e.Handled = true;

            if (e.Data.GetData(NodeDragDataFormat) is not NodeViewModel dragged) return;
            ReparentNode(dragged.UnderlyingNode, null);
        }

        /// <summary>The dragged/target <see cref="Node"/> pair for a drop on
        /// <paramref name="sender"/> (a <see cref="TreeViewItem"/>) - false (nothing
        /// written to either <c>out</c> parameter) if <paramref name="e"/> carries no
        /// <see cref="NodeDragDataFormat"/> payload, <paramref name="sender"/> isn't a
        /// <see cref="TreeViewItem"/> bound to a <see cref="NodeViewModel"/>, or the
        /// target is the dragged node itself or one of its own descendants - the exact
        /// same structural-cycle rule <see cref="Scene3D.Reparent"/> itself enforces,
        /// checked here too so the drag cursor shows "no drop" during
        /// <see cref="SceneTreeViewItem_DragOver"/> rather than only silently no-op'ing
        /// on an actual <see cref="SceneTreeViewItem_Drop"/>.</summary>
        private static bool TryGetReparentPair(object sender, DragEventArgs e, out Node draggedNode, out Node targetNode)
        {
            draggedNode = null!;
            targetNode = null!;

            if (e.Data.GetData(NodeDragDataFormat) is not NodeViewModel dragged) return false;
            if (sender is not TreeViewItem { DataContext: NodeViewModel target }) return false;

            var current = target.UnderlyingNode;
            while (current is not null)
            {
                if (current == dragged.UnderlyingNode) return false;
                current = current.Parent;
            }

            draggedNode = dragged.UnderlyingNode;
            targetNode = target.UnderlyingNode;
            return true;
        }

        /// <summary>The actual reparent (<see cref="Scene3D.Reparent"/>, which recomputes
        /// <paramref name="node"/>'s own Local Position/Rotation/Scale so its WORLD
        /// transform never visibly jumps - see that method's own remarks), followed by
        /// the same "rebuild the whole Outliner tree and re-render" refresh
        /// <see cref="AddNodeToScene"/> already uses. Rebuilding the tree (<see cref="SceneViewModel.Load"/>)
        /// discards every existing <see cref="NodeViewModel"/> instance, including the
        /// one the Outliner/Properties Inspector had selected - <see cref="SelectNode"/>
        /// re-selects the SAME underlying <see cref="Node"/> (which the rebuild doesn't
        /// touch at all) against the freshly-built tree, so the selection survives the
        /// drag from the user's own point of view.</summary>
        private void ReparentNode(Node node, Node? newParent)
        {
            if (!_currentScene.Reparent(node, newParent)) return;

            _sceneViewModel.Load(_currentScene);
            _renderer.Render(_currentScene, zoomToFit: false);
            SelectNode(node);
        }

        /// <summary>True once <see cref="EditModeButton"/> (rather than
        /// <see cref="ObjectModeButton"/>/<see cref="WeightPaintModeButton"/>) is the
        /// checked radio button - what every Edit-Mode-vs-everything-else branch in
        /// this window reads.</summary>
        private bool IsEditMode => EditModeButton.IsChecked == true;

        /// <summary>True once <see cref="WeightPaintModeButton"/> is the checked radio
        /// button - what <see cref="Viewport_MouseLeftButtonDown"/>/<see cref="Viewport_MouseMove"/>
        /// read to route a viewport click/drag to the brush instead of selection/Edit
        /// Mode.</summary>
        private bool IsWeightPaintMode => WeightPaintModeButton.IsChecked == true;

        /// <summary>True once <see cref="VertexPaintModeButton"/> is the checked radio
        /// button - the same role <see cref="IsWeightPaintMode"/> plays for Weight
        /// Paint mode.</summary>
        private bool IsVertexPaintMode => VertexPaintModeButton.IsChecked == true;

        /// <summary>True once <see cref="SculptModeButton"/> is the checked radio
        /// button - the same role <see cref="IsWeightPaintMode"/> plays for Weight
        /// Paint mode.</summary>
        private bool IsSculptMode => SculptModeButton.IsChecked == true;

        private void SelectNode(Node? node)
        {
            _renderer.Select(node);
            _sceneViewModel.SelectedNode = _sceneViewModel.FindViewModel(node);

            if (IsWeightPaintMode)
            {
                // Same "selection changed, so does everything driven by it" behavior
                // Edit Mode's own re-targeting (below) already has - a target with no
                // mesh/binding has nothing to paint, so this bounces back to Object Mode.
                if (node is { Mesh: not null, SkinBinding: not null })
                {
                    _renderer.EnterWeightPaintMode(node);
                    RefreshActiveBoneCombo();
                }
                else
                {
                    ObjectModeButton.IsChecked = true;
                }
                return;
            }

            if (IsVertexPaintMode)
            {
                if (node?.Mesh is not null) _renderer.EnterVertexPaintMode(node);
                else ObjectModeButton.IsChecked = true;
                return;
            }

            if (IsSculptMode)
            {
                if (node?.Mesh is not null) _renderer.EnterSculptMode(node);
                else ObjectModeButton.IsChecked = true;
                return;
            }

            if (!IsEditMode)
            {
                _gizmo.Attach(node);
                return;
            }

            // A selection change made WHILE already in Edit Mode (e.g. an Outliner
            // click) re-targets editing at the newly selected node, rather than leaving
            // Edit Mode pointed at whatever was selected before - the same "selection
            // changed, so does everything driven by it" behavior Object Mode's own
            // gizmo already has. A target with no mesh (or no selection at all) has
            // nothing to edit, so this bounces back to Object Mode instead.
            if (node?.Mesh is not null)
            {
                _renderer.EnterEditMode(node);
                _componentGizmo.Attach(_renderer.EditSession);
            }
            else
            {
                ObjectModeButton.IsChecked = true;
            }
        }

        private void GizmoModeButton_Checked(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            if (sender is not RadioButton { Tag: string modeName }) return;
            if (Enum.TryParse<GizmoMode>(modeName, out var mode)) _gizmo.Mode = mode;
        }

        /// <summary>The Gizmo Mode toolbar's Global/Local toggle - sets BOTH gizmos' own
        /// <see cref="TransformGizmo.Space"/>/<see cref="ComponentGizmo.Space"/> together
        /// (only one of the two is ever attached/visible at a time - see
        /// <see cref="SelectNode"/>/<see cref="EditorModeButton_Checked"/> - but keeping
        /// both in sync means the choice carries over correctly whichever one the user
        /// switches to next, rather than each silently reverting to Global).</summary>
        private void TransformSpaceButton_Checked(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            if (sender is not RadioButton { Tag: string spaceName }) return;
            if (!Enum.TryParse<TransformSpace>(spaceName, out var space)) return;

            _gizmo.Space = space;
            _componentGizmo.Space = space;
        }

        /// <summary>The viewport shading toolbar - see <see cref="ShadingMode"/>'s own
        /// remarks for what each of the 4 modes actually changes. Setting
        /// <see cref="Scene3DRenderer.ShadingMode"/> re-renders immediately on its own,
        /// the same "setting the mode applies it too" shape <see cref="GizmoModeButton_Checked"/>
        /// already uses for <see cref="TransformGizmo.Mode"/>.</summary>
        private void ShadingModeButton_Checked(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            if (sender is not RadioButton { Tag: string modeName }) return;
            if (Enum.TryParse<ShadingMode>(modeName, out var mode)) _renderer.ShadingMode = mode;
        }

        /// <summary>Switches between Object Mode (the existing whole-node
        /// Translate/Rotate/Scale gizmo) and Edit Mode (per-component selection and
        /// <see cref="ComponentGizmo"/>) - enabling/disabling the Vertex/Edge/Face
        /// buttons to match, and requiring a mesh-bearing node already selected before
        /// Edit Mode can actually be entered (bouncing back to Object Mode with a
        /// message otherwise - there is nothing to select vertices/edges/faces of with
        /// nothing chosen to edit).</summary>
        private void EditorModeButton_Checked(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            if (sender is not RadioButton { Tag: string modeName }) return;
            var enteringEditMode = modeName == "Edit";
            var enteringWeightPaintMode = modeName == "WeightPaint";
            var enteringVertexPaintMode = modeName == "VertexPaint";
            var enteringSculptMode = modeName == "Sculpt";

            VertexModeButton.IsEnabled = enteringEditMode;
            EdgeModeButton.IsEnabled = enteringEditMode;
            FaceModeButton.IsEnabled = enteringEditMode;
            ProportionalEditingCheckBox.IsEnabled = enteringEditMode;
            ProportionalRadiusSlider.IsEnabled = enteringEditMode;
            ExtrudeButton.IsEnabled = enteringEditMode;
            SubdivideButton.IsEnabled = enteringEditMode;
            LoopCutButton.IsEnabled = enteringEditMode;
            BevelButton.IsEnabled = enteringEditMode;
            MarkSeamButton.IsEnabled = enteringEditMode;
            ClearSeamButton.IsEnabled = enteringEditMode;
            UnwrapButton.IsEnabled = enteringEditMode;
            // DeleteButton is deliberately NOT toggled here (unlike the others above) -
            // it means something in BOTH modes now (see DeleteSelected's own remarks),
            // so it stays enabled regardless of which one is active.

            // Custom Pivot Points only makes sense in Object Mode (it moves a whole
            // NODE'S own origin, not any per-vertex selection) - the exact opposite
            // enablement from every Edit-Mode-only control above.
            AffectOnlyOriginCheckBox.IsEnabled = !enteringEditMode && !enteringWeightPaintMode && !enteringVertexPaintMode && !enteringSculptMode;

            WeightPaintToolbar.Visibility = enteringWeightPaintMode ? Visibility.Visible : Visibility.Collapsed;
            VertexPaintToolbar.Visibility = enteringVertexPaintMode ? Visibility.Visible : Visibility.Collapsed;
            SculptToolbar.Visibility = enteringSculptMode ? Visibility.Visible : Visibility.Collapsed;

            // Leaving Weight/Vertex Paint/Sculpt mode (for any of the other modes)
            // always detaches its own session first, exactly like leaving Edit Mode
            // below - never left silently active (and still eating viewport clicks)
            // once its own toolbar is hidden.
            if (!enteringWeightPaintMode) _renderer.ExitWeightPaintMode();
            if (!enteringVertexPaintMode) _renderer.ExitVertexPaintMode();
            if (!enteringSculptMode) _renderer.ExitSculptMode();

            if (enteringEditMode)
            {
                if (_sceneViewModel.SelectedNode?.UnderlyingNode is not { Mesh: not null } node)
                {
                    MessageBox.Show(this, "Select an object with a mesh before entering Edit Mode.",
                        "JolieCat3D", MessageBoxButton.OK, MessageBoxImage.Information);
                    ObjectModeButton.IsChecked = true;
                    return;
                }

                // Object Mode's own whole-node gizmo and Edit Mode's own per-component
                // one are never shown at once - hide the former while the latter is active.
                _gizmo.Attach(null);
                _renderer.EnterEditMode(node);
                _componentGizmo.Attach(_renderer.EditSession);
            }
            else if (enteringWeightPaintMode)
            {
                if (_sceneViewModel.SelectedNode?.UnderlyingNode is not { Mesh: not null, SkinBinding: not null } node)
                {
                    MessageBox.Show(this, "Select a mesh already bound to an Armature (see its own Skinning panel) before entering Weight Paint mode.",
                        "JolieCat3D", MessageBoxButton.OK, MessageBoxImage.Information);
                    ObjectModeButton.IsChecked = true;
                    return;
                }

                _gizmo.Attach(null);
                _componentGizmo.Attach(null);
                _renderer.EnterWeightPaintMode(node);
                RefreshActiveBoneCombo();
            }
            else if (enteringVertexPaintMode)
            {
                if (_sceneViewModel.SelectedNode?.UnderlyingNode is not { Mesh: not null } node)
                {
                    MessageBox.Show(this, "Select an object with a mesh before entering Vertex Paint mode.",
                        "JolieCat3D", MessageBoxButton.OK, MessageBoxImage.Information);
                    ObjectModeButton.IsChecked = true;
                    return;
                }

                _gizmo.Attach(null);
                _componentGizmo.Attach(null);
                _renderer.EnterVertexPaintMode(node);
            }
            else if (enteringSculptMode)
            {
                if (_sceneViewModel.SelectedNode?.UnderlyingNode is not { Mesh: not null } node)
                {
                    MessageBox.Show(this, "Select an object with a mesh before entering Sculpt mode.",
                        "JolieCat3D", MessageBoxButton.OK, MessageBoxImage.Information);
                    ObjectModeButton.IsChecked = true;
                    return;
                }

                _gizmo.Attach(null);
                _componentGizmo.Attach(null);
                _renderer.EnterSculptMode(node);
            }
            else
            {
                _componentGizmo.Attach(null);
                _renderer.ExitEditMode();
                _gizmo.Attach(_sceneViewModel.SelectedNode?.UnderlyingNode);
            }
        }

        /// <summary>Rebuilds <see cref="ActiveBoneComboBox"/>'s own item list from
        /// whatever <see cref="Scene3DRenderer.WeightPaintSession"/>'s current
        /// <c>Target</c> is bound to - called whenever that target changes (entering
        /// Weight Paint mode, or re-targeting it via a selection change) so the combo
        /// always lists the CURRENTLY painted mesh's own bones, never a stale set left
        /// over from whatever was painted before.</summary>
        private void RefreshActiveBoneCombo()
        {
            var boneNames = _renderer.WeightPaintSession.Target?.SkinBinding?.Bones.Select(bone => bone.Name).ToList() ?? new List<string>();
            ActiveBoneComboBox.ItemsSource = boneNames;
            ActiveBoneComboBox.SelectedIndex = boneNames.Count > 0 ? 0 : -1;
            _renderer.WeightPaintSession.ActiveBoneIndex = 0;
        }

        private void ActiveBoneComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isInitialized) return;
            _renderer.WeightPaintSession.ActiveBoneIndex = ActiveBoneComboBox.SelectedIndex;
        }

        private void BrushRadiusSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_isInitialized) return;
            _renderer.WeightPaintSession.BrushRadius = (float)e.NewValue;
        }

        private void BrushStrengthSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_isInitialized) return;
            _renderer.WeightPaintSession.Strength = (float)e.NewValue;
        }

        private void BrushModeButton_Checked(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            if (sender is not RadioButton { Tag: string modeName }) return;
            // "Add" paints toward full weight (1.0), "Subtract" erases toward none (0.0) -
            // see WeightPaintBrush.Apply's own remarks on TargetWeight.
            _renderer.WeightPaintSession.TargetWeight = modeName == "Subtract" ? 0f : 1f;
        }

        /// <summary>Weight Paint mode's own mouse-down: paints one brush tick right
        /// where the click landed, then starts <see cref="_isPaintingWeights"/> so
        /// <see cref="Viewport_MouseMove"/> keeps painting for the rest of the drag -
        /// the same "one tick per mouse-move sample during the drag" brush feel a
        /// real paint tool has.</summary>
        private bool _isPaintingWeights;

        private void HandleWeightPaintClick(Point position)
        {
            if (_renderer.WeightPaintSession.RaycastWorldHitPoint(Viewport, position) is not { } worldHitPoint) return;
            _renderer.WeightPaintSession.PaintAt(worldHitPoint);
            _renderer.Refresh();
        }

        /// <summary>Vertex Paint mode's own target-color picker - a plain "#RRGGBB"
        /// hex field, the same convention <c>NodeViewModel.DiffuseColorHex</c>/
        /// <c>LightColorHex</c> already use.</summary>
        private void VertexPaintColorTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isInitialized) return;
            if (sender is not TextBox { Text: var text }) return;
            if (System.Windows.Media.ColorConverter.ConvertFromString(text) is not System.Windows.Media.Color parsed) return;

            _renderer.VertexPaintSession.TargetColor = new Color4(parsed.ScR, parsed.ScG, parsed.ScB, parsed.ScA);
        }

        private void VertexBrushRadiusSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_isInitialized) return;
            _renderer.VertexPaintSession.BrushRadius = (float)e.NewValue;
        }

        private void VertexBrushStrengthSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_isInitialized) return;
            _renderer.VertexPaintSession.Strength = (float)e.NewValue;
        }

        /// <summary>Vertex Paint mode's own mouse-down: paints one brush tick right
        /// where the click landed, then starts <see cref="_isPaintingVertexColors"/> so
        /// <see cref="Viewport_MouseMove"/> keeps painting for the rest of the drag -
        /// the same shape <see cref="HandleWeightPaintClick"/>/<see cref="_isPaintingWeights"/>
        /// already have.</summary>
        private bool _isPaintingVertexColors;

        private void HandleVertexPaintClick(Point position)
        {
            if (_renderer.VertexPaintSession.RaycastWorldHitPoint(Viewport, position) is not { } worldHitPoint) return;
            _renderer.VertexPaintSession.PaintAt(worldHitPoint);
            _renderer.Refresh();
        }

        /// <summary>Sculpt Mode's own mouse-down flag - <see cref="Viewport_MouseLeftButtonDown"/>
        /// already calls <see cref="SculptSession.BeginStroke"/> directly (unlike
        /// <see cref="HandleWeightPaintClick"/>/<see cref="HandleVertexPaintClick"/>'s
        /// own shared click handler, Sculpt's own Draw/Smooth-vs-Grab dispatch already
        /// lives inside <see cref="SculptSession"/> itself, so there is nothing extra
        /// to branch on here) - this just keeps <see cref="Viewport_MouseMove"/> calling
        /// <see cref="SculptSession.UpdateStroke"/> for the rest of the drag.</summary>
        private bool _isSculpting;

        private void SculptBrushModeButton_Checked(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            if (sender is not RadioButton { Tag: string modeName }) return;
            if (Enum.TryParse<SculptBrushMode>(modeName, out var mode)) _renderer.SculptSession.Mode = mode;
        }

        private void SculptBrushRadiusSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_isInitialized) return;
            _renderer.SculptSession.BrushRadius = (float)e.NewValue;
        }

        private void SculptBrushStrengthSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_isInitialized) return;
            _renderer.SculptSession.Strength = (float)e.NewValue;
        }

        private void Viewport_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isInitialized) return;

            if (_isPaintingWeights)
            {
                if (e.LeftButton == MouseButtonState.Pressed) HandleWeightPaintClick(e.GetPosition(Viewport));
                else _isPaintingWeights = false;
                return;
            }

            if (_isPaintingVertexColors)
            {
                if (e.LeftButton == MouseButtonState.Pressed) HandleVertexPaintClick(e.GetPosition(Viewport));
                else _isPaintingVertexColors = false;
                return;
            }

            if (_isSculpting)
            {
                if (e.LeftButton == MouseButtonState.Pressed)
                {
                    _renderer.SculptSession.Invert = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
                    _renderer.SculptSession.UpdateStroke(Viewport, e.GetPosition(Viewport));
                    _renderer.Refresh();
                }
                else _isSculpting = false;
            }
        }

        private void Viewport_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _isPaintingWeights = false;
            _isPaintingVertexColors = false;
            if (_isSculpting)
            {
                _isSculpting = false;
                _renderer.SculptSession.EndStroke();
            }
        }

        private void ComponentModeButton_Checked(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            if (sender is not RadioButton { Tag: string modeName }) return;
            if (!Enum.TryParse<ComponentType>(modeName, out var mode)) return;

            _renderer.EditSession.ComponentMode = mode;
            // Switching what kind of component a click resolves to doesn't imply the
            // existing selection (a set of vertex indices either way - see
            // MeshEditSession's own remarks) is still meaningful to look at the same
            // way, so clear it rather than leave, say, a single stray vertex "selected"
            // while showing Face mode's own overlay.
            _renderer.EditSession.Clear();
            _renderer.RefreshComponentOverlay();
            _componentGizmo.Attach(_renderer.EditSession);
        }

        /// <summary>The Component toolbar's "Proportional" (Soft Selection) toggle - see
        /// <see cref="MeshEditSession.ProportionalEditingEnabled"/>'s own remarks. Takes
        /// effect on the NEXT drag (<see cref="ComponentGizmo"/> reads it fresh at
        /// <see cref="MeshEditSession.BeginProportionalDrag"/>, called right at the start
        /// of each gesture) - toggling it mid-drag has no effect on whatever drag is
        /// already in progress, only future ones.</summary>
        private void ProportionalEditingCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            _renderer.EditSession.ProportionalEditingEnabled = ProportionalEditingCheckBox.IsChecked == true;
        }

        /// <summary>The Component toolbar's own Radius slider - see
        /// <see cref="MeshEditSession.ProportionalRadius"/>'s own remarks. Same "takes
        /// effect on the next drag" timing as <see cref="ProportionalEditingCheckBox_Changed"/>.</summary>
        private void ProportionalRadiusSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_isInitialized) return;
            _renderer.EditSession.ProportionalRadius = (float)ProportionalRadiusSlider.Value;
        }

        /// <summary>Routes an Edit Mode viewport click through whichever
        /// <see cref="ComponentHitTester"/> method matches the current
        /// <see cref="MeshEditSession.ComponentMode"/>, applies the resulting
        /// selection change (replacing the current selection, or adding to it for
        /// <paramref name="additive"/> - a Shift-click), and rebuilds both the marker
        /// overlay and the component gizmo (at the new selection's centroid) to match.
        /// A click that hits nothing clears the selection entirely unless
        /// <paramref name="additive"/> is set (a Shift-click on empty space is a no-op,
        /// not a clear - the same convention a plain click already has for "add to",
        /// not "replace, with nothing").</summary>
        private void HandleComponentClick(Point position, bool additive)
        {
            var session = _renderer.EditSession;
            if (session.Target is not { } node) return;

            switch (session.ComponentMode)
            {
                case ComponentType.Vertex:
                    if (ComponentHitTester.HitTestVertex(Viewport, node, position) is { } vertexIndex)
                        session.SelectVertex(vertexIndex, additive);
                    else if (!additive)
                        session.Clear();
                    break;

                case ComponentType.Edge:
                    if (ComponentHitTester.HitTestEdge(Viewport, node, position) is { } edge)
                        session.SelectEdge(edge.A, edge.B, additive);
                    else if (!additive)
                        session.Clear();
                    break;

                case ComponentType.Face:
                    if (ComponentHitTester.HitTestFace(Viewport, node, position) is { } face)
                        session.SelectFace(face.Indices, additive);
                    else if (!additive)
                        session.Clear();
                    break;
            }

            _renderer.RefreshComponentOverlay();
            _componentGizmo.Attach(session);
        }

        /// <summary>A modest, fixed initial offset for the Extrude toolbar button -
        /// enough to visibly separate the new cap face from the original one so the
        /// result is never a degenerate zero-thickness extrusion, while still leaving
        /// the freshly-extruded (now selected - see <see cref="MeshEditSession.ExtrudeSelectedFace"/>)
        /// face for <see cref="ComponentGizmo"/> to drag further afterward - the same
        /// "Extrude immediately creates geometry a small amount out, then let the user
        /// drag it" convention most modeling tools use for a toolbar/menu-triggered
        /// Extrude (as opposed to one dragged out live from the very first mouse-move).</summary>
        private const float DefaultExtrudeDistance = 0.5f;

        /// <summary>The Edit Mode toolbar's "Extrude" button - extrudes whichever face
        /// is currently fully selected (Face mode) via <see cref="MeshEditSession.ExtrudeSelectedFace"/>,
        /// wrapped into an undoable <see cref="MeshEditCommand"/> via
        /// <see cref="MeshEditCommandFactory.Capture"/> and recorded onto
        /// <see cref="_commandHistory"/>, then re-renders (<see cref="Scene3DRenderer.Refresh"/>
        /// rebuilds the viewport's geometry straight from the now-extruded
        /// <c>Core.Geometry.Mesh</c>, the same "real-time" update mechanism every other
        /// Edit Mode operation already uses) and rebuilds the component gizmo at the new
        /// cap face's own centroid. Tells the user what to do instead if nothing
        /// extrudable is currently selected, rather than silently doing nothing.</summary>
        private void ExtrudeButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;

            var session = _renderer.EditSession;
            if (session.Target is not { } node) return;

            TryRun("Extrude", () =>
            {
                var command = MeshEditCommandFactory.Capture(node, "Extrude Face",
                    () => session.ExtrudeSelectedFace(DefaultExtrudeDistance),
                    onChanged: () =>
                    {
                        _renderer.Refresh();
                        _componentGizmo.Attach(session);
                    });

                if (command is null)
                {
                    MessageBox.Show(this,
                        "Select a single quad/n-gon face (Face mode) to extrude - a triangle "
                        + "(from an STL import, or after Subdivide) can't be extruded directly.",
                        "JolieCat3D", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                _commandHistory.Record(command);
                _renderer.Refresh();
                _componentGizmo.Attach(session);
            });
        }

        /// <summary>The Edit Mode toolbar's "Subdivide" button - subdivides the WHOLE
        /// target mesh via <see cref="MeshEditSession.SubdivideMesh"/> (see its own
        /// remarks on why this is never a partial/selection-scoped operation), likewise
        /// wrapped into an undoable, recorded <see cref="MeshEditCommand"/>, and
        /// re-renders/rebuilds the gizmo the same way <see cref="ExtrudeButton_Click"/>
        /// does.</summary>
        private void SubdivideButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;

            var session = _renderer.EditSession;
            if (session.Target is not { } node) return;

            TryRun("Subdivide", () =>
            {
                var command = MeshEditCommandFactory.Capture(node, "Subdivide Mesh",
                    () => session.SubdivideMesh(),
                    onChanged: () =>
                    {
                        _renderer.Refresh();
                        _componentGizmo.Attach(session);
                    });

                if (command is null) return; // no mesh on this node at all - nothing to subdivide or undo

                _commandHistory.Record(command);
                _renderer.Refresh();
                _componentGizmo.Attach(session);
            });
        }

        /// <summary>The Edit Mode toolbar's "Loop Cut" button - inserts a new edge loop
        /// through the ring of quads reachable from whichever single edge (exactly 2
        /// selected vertices - Edge mode) is currently selected, via
        /// <see cref="MeshEditSession.LoopCutSelectedEdge"/>, wrapped into an undoable,
        /// recorded <see cref="MeshEditCommand"/> the same way <see cref="ExtrudeButton_Click"/>
        /// already is.</summary>
        private void LoopCutButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;

            var session = _renderer.EditSession;
            if (session.Target is not { } node) return;

            TryRun("Loop Cut", () =>
            {
                var command = MeshEditCommandFactory.Capture(node, "Loop Cut",
                    () => session.LoopCutSelectedEdge(),
                    onChanged: () =>
                    {
                        _renderer.Refresh();
                        _componentGizmo.Attach(session);
                    });

                if (command is null)
                {
                    MessageBox.Show(this,
                        "Select a single edge (Edge mode) that's part of at least one quad to Loop Cut.",
                        "JolieCat3D", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                _commandHistory.Record(command);
                _renderer.Refresh();
                _componentGizmo.Attach(session);
            });
        }

        /// <summary>The size of the corner facet the Edit Mode toolbar's "Bevel" button
        /// cuts - a fraction of each surrounding edge's own length (see
        /// <see cref="Mesh.BevelVertex"/>'s own remarks), the same "a modest, fixed,
        /// visibly-there starting amount, immediately ready for a further tweak" role
        /// <see cref="DefaultExtrudeDistance"/> plays for Extrude.</summary>
        private const float DefaultBevelAmount = 0.25f;

        /// <summary>The Edit Mode toolbar's "Bevel" button - chamfers whichever single
        /// vertex is currently selected (Vertex mode) via <see cref="MeshEditSession.BevelSelectedVertex"/>,
        /// wrapped into an undoable, recorded <see cref="MeshEditCommand"/> the same way
        /// <see cref="ExtrudeButton_Click"/> already is.</summary>
        private void BevelButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;

            var session = _renderer.EditSession;
            if (session.Target is not { } node) return;

            TryRun("Bevel", () =>
            {
                var command = MeshEditCommandFactory.Capture(node, "Bevel Vertex",
                    () => session.BevelSelectedVertex(DefaultBevelAmount),
                    onChanged: () =>
                    {
                        _renderer.Refresh();
                        _componentGizmo.Attach(session);
                    });

                if (command is null)
                {
                    MessageBox.Show(this,
                        "Select a single vertex (Vertex mode) fully surrounded by faces to Bevel - "
                        + "a boundary/edge vertex can't be beveled.",
                        "JolieCat3D", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                _commandHistory.Record(command);
                _renderer.Refresh();
                _componentGizmo.Attach(session);
            });
        }

        /// <summary>The Edit Mode toolbar's "Mark Seam" button - flags whichever single
        /// edge (exactly 2 selected vertices - Edge mode) is currently selected as a UV
        /// Seam via <see cref="MeshEditSession.MarkSeamOnSelectedEdge"/>, wrapped into an
        /// undoable, recorded <see cref="MeshEditCommand"/> the same way
        /// <see cref="LoopCutButton_Click"/> already is - marking/clearing a seam is a
        /// real mesh edit (persisted on <see cref="Mesh.SeamEdges"/>), so it deserves the
        /// same Undo/Redo coverage as any other Edit Mode action.</summary>
        private void MarkSeamButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;

            var session = _renderer.EditSession;
            if (session.Target is not { } node) return;

            TryRun("Mark Seam", () =>
            {
                var command = MeshEditCommandFactory.Capture(node, "Mark Seam",
                    () => session.MarkSeamOnSelectedEdge(),
                    onChanged: _renderer.Refresh);

                if (command is null)
                {
                    MessageBox.Show(this, "Select a single edge (Edge mode) to Mark Seam.",
                        "JolieCat3D", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                _commandHistory.Record(command);
                _renderer.Refresh();
            });
        }

        /// <summary>The reverse of <see cref="MarkSeamButton_Click"/> - same selection
        /// requirement, un-flags the edge instead via <see cref="MeshEditSession.ClearSeamOnSelectedEdge"/>.</summary>
        private void ClearSeamButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;

            var session = _renderer.EditSession;
            if (session.Target is not { } node) return;

            TryRun("Clear Seam", () =>
            {
                var command = MeshEditCommandFactory.Capture(node, "Clear Seam",
                    () => session.ClearSeamOnSelectedEdge(),
                    onChanged: _renderer.Refresh);

                if (command is null)
                {
                    MessageBox.Show(this, "Select a single edge (Edge mode) to Clear Seam.",
                        "JolieCat3D", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                _commandHistory.Record(command);
                _renderer.Refresh();
            });
        }

        /// <summary>The Edit Mode toolbar's "Unwrap" button - runs <see cref="MeshEditSession.UnwrapTargetMesh"/>
        /// against the WHOLE target mesh (never a partial/selection-scoped operation,
        /// the same "always applies to everything" convention <see cref="SubdivideButton_Click"/>
        /// already follows), wrapped into an undoable, recorded <see cref="MeshEditCommand"/>
        /// and re-rendered/re-gizmo'd the same way every other Edit Mode action here
        /// is.</summary>
        private void UnwrapButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;

            var session = _renderer.EditSession;
            if (session.Target is not { } node) return;

            TryRun("Unwrap", () =>
            {
                var command = MeshEditCommandFactory.Capture(node, "Unwrap",
                    () => session.UnwrapTargetMesh(),
                    onChanged: () =>
                    {
                        _renderer.Refresh();
                        _componentGizmo.Attach(session);
                    });

                if (command is null) return; // no mesh on this node at all - nothing to unwrap or undo

                _commandHistory.Record(command);
                _renderer.Refresh();
                _componentGizmo.Attach(session);
            });
        }

        /// <summary>The Object Mode toolbar's "Affect Only: Origin" checkbox - Custom
        /// Pivot Points (see <see cref="TransformGizmo.AffectOnlyOrigin"/>'s own
        /// remarks).</summary>
        private void AffectOnlyOriginCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            _gizmo.AffectOnlyOrigin = AffectOnlyOriginCheckBox.IsChecked == true;
        }

        /// <summary>Face Snapping's own "Align to Surface" toggle - see
        /// <see cref="TransformGizmo.AlignToSurfaceEnabled"/>'s own remarks.</summary>
        private void AlignToSurfaceCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            _gizmo.AlignToSurfaceEnabled = AlignToSurfaceCheckBox.IsChecked == true;
        }

        /// <summary>The "Delete" toolbar button - Object Mode deletes the whole selected
        /// node (see <see cref="DeleteSelectedNode"/>); Edit Mode deletes the selected
        /// vertices/edges/faces instead (see <see cref="DeleteSelectedComponents"/>). Also
        /// reachable via the Delete key from anywhere in the window - see
        /// <see cref="MainWindow_PreviewKeyDown"/> - this button exists purely so the same
        /// command has a mouse-only path too, mirroring <see cref="DuplicateButton_Click"/>.</summary>
        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            DeleteSelected();
        }

        /// <summary>Routes to whichever "Delete" actually means right now - Object Mode
        /// (the whole selected node) or Edit Mode (a component selection) - the shared
        /// entry point both <see cref="DeleteButton_Click"/> and the Delete key (see
        /// <see cref="MainWindow_PreviewKeyDown"/>) call, mirroring
        /// <see cref="DuplicateSelected"/>'s own exact shape.</summary>
        private void DeleteSelected()
        {
            if (IsEditMode) DeleteSelectedComponents();
            else DeleteSelectedNode();
        }

        /// <summary>Object Mode's own "Delete" command - removes the selected node (and
        /// its whole subtree) from the scene graph via <see cref="DeleteNodeCommandFactory.Create"/>/
        /// <see cref="DeleteNodeCommand"/> (wrapped for Undo/Redo the same
        /// <see cref="CommandHistory.Execute"/> way <see cref="DuplicateSelectedNode"/>'s
        /// own <see cref="DuplicateNodeCommand"/> already is), clears the selection
        /// (there is nothing left to keep the gizmo attached to or the Properties
        /// Inspector showing), and drops every <see cref="_textureSources"/> entry for the
        /// deleted subtree - keeping one around for a node that's no longer reachable from
        /// <see cref="_currentScene"/> at all would be a real, silently-accumulating memory
        /// leak over a session that deletes many nodes in turn (the same reasoning
        /// <see cref="LoadScene"/>'s own wholesale <c>_textureSources.Clear()</c> already
        /// discloses, just scoped to one deleted subtree instead of an entire replaced
        /// scene). A no-op with nothing selected.</summary>
        private void DeleteSelectedNode()
        {
            if (_sceneViewModel.SelectedNode?.UnderlyingNode is not { } node) return;

            TryRun("Delete", () =>
            {
                var command = DeleteNodeCommandFactory.Create(_currentScene, node, _timeline, onChanged: () =>
                {
                    _sceneViewModel.Load(_currentScene);
                    _renderer.Render(_currentScene, zoomToFit: false);
                });

                _commandHistory.Execute(command);
                SelectNode(null);

                foreach (var removedNode in command.Target.Traverse()) _textureSources.Remove(removedNode);
            });
        }

        /// <summary>The actual "Delete Vertices/Edges/Faces" command both
        /// <see cref="DeleteButton_Click"/> and the Delete key (see
        /// <see cref="MainWindow_PreviewKeyDown"/>) funnel into (via
        /// <see cref="DeleteSelected"/>) - a no-op (not even an undoable no-op command
        /// recorded) with nothing currently selected, matching
        /// <see cref="MeshEditSession.DeleteSelected"/>'s own "false = nothing happened"
        /// return.</summary>
        private void DeleteSelectedComponents()
        {
            var session = _renderer.EditSession;
            if (session.Target is not { } node) return;

            TryRun("Delete", () =>
            {
                var command = MeshEditCommandFactory.Capture(node, "Delete Selected",
                    () => session.DeleteSelected(),
                    onChanged: () =>
                    {
                        _renderer.Refresh();
                        _componentGizmo.Attach(session);
                    });

                if (command is null) return; // nothing selected (or no mesh) - nothing to delete or undo

                _commandHistory.Record(command);
                _renderer.Refresh();
                _componentGizmo.Attach(session);
            });
        }

        /// <summary>The "Duplicate" toolbar button - Object Mode duplicates the whole
        /// selected node (see <see cref="DuplicateSelectedNode"/>); Edit Mode duplicates
        /// the selected vertices/edges/faces instead (see
        /// <see cref="DuplicateSelectedComponents"/>). Also reachable via Ctrl+D from
        /// anywhere in the window - see <see cref="MainWindow_PreviewKeyDown"/> - this
        /// button exists purely so the same command has a mouse-only path too, the same
        /// "button and key both funnel into one method" shape <see cref="DeleteButton_Click"/>/
        /// <see cref="DeleteSelectedComponents"/> already have.</summary>
        private void DuplicateButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            DuplicateSelected();
        }

        /// <summary>Routes to whichever "Duplicate" actually means right now - Object Mode
        /// (a whole node) or Edit Mode (a component selection) - the shared entry point
        /// both <see cref="DuplicateButton_Click"/> and Ctrl+D (see
        /// <see cref="MainWindow_PreviewKeyDown"/>) call.</summary>
        private void DuplicateSelected()
        {
            if (IsEditMode) DuplicateSelectedComponents();
            else DuplicateSelectedNode();
        }

        /// <summary>Object Mode's own "Duplicate" command - deep-clones the selected node
        /// (see <see cref="DuplicateNodeCommandFactory.Create"/>/<see cref="Node.Clone"/>:
        /// its own mesh, modifiers, material binding, AND every descendant, recursively,
        /// plus - once actually executed - a copy of any keyframe track it or any of its
        /// descendants had on <see cref="_timeline"/>), inserts the clone into the scene
        /// graph next to the original (wrapped in an undoable <see cref="DuplicateNodeCommand"/>,
        /// via <see cref="CommandHistory.Execute"/> rather than <see cref="CommandHistory.Record"/>
        /// since - unlike a gizmo drag - nothing has inserted it anywhere yet), and selects
        /// the new clone - the standard "duplicate leaves the COPY selected, ready to move"
        /// convention every modeling tool's own Duplicate follows. A no-op with nothing
        /// selected.</summary>
        private void DuplicateSelectedNode()
        {
            if (_sceneViewModel.SelectedNode?.UnderlyingNode is not { } node) return;

            TryRun("Duplicate", () =>
            {
                var command = DuplicateNodeCommandFactory.Create(_currentScene, node, _timeline, onChanged: () =>
                {
                    _sceneViewModel.Load(_currentScene);
                    _renderer.Render(_currentScene, zoomToFit: false);
                });

                _commandHistory.Execute(command);
                SelectNode(command.Clone);
            });
        }

        /// <summary>Edit Mode's own "Duplicate" command - duplicates whichever
        /// vertices/edges/faces are currently selected via
        /// <see cref="MeshEditSession.DuplicateSelected"/>, wrapped into an undoable
        /// <see cref="MeshEditCommand"/> exactly the same way <see cref="DeleteSelectedComponents"/>
        /// already is (a duplicate changes the mesh's own vertex COUNT, the same reason
        /// Delete needs a full before/after <see cref="Mesh.Clone"/> snapshot rather than
        /// the lightweight per-index <see cref="VertexTranslateCommand"/> a plain drag
        /// uses). Leaves the freshly duplicated geometry selected, ready to drag via
        /// <see cref="ComponentGizmo"/> - see <see cref="MeshEditSession.DuplicateSelected"/>'s
        /// own remarks.</summary>
        private void DuplicateSelectedComponents()
        {
            var session = _renderer.EditSession;
            if (session.Target is not { } node) return;

            TryRun("Duplicate", () =>
            {
                var command = MeshEditCommandFactory.Capture(node, "Duplicate Selected",
                    () => session.DuplicateSelected(),
                    onChanged: () =>
                    {
                        _renderer.Refresh();
                        _componentGizmo.Attach(session);
                    });

                if (command is null) return; // nothing selected (or no mesh) - nothing to duplicate or undo

                _commandHistory.Record(command);
                _renderer.Refresh();
                _componentGizmo.Attach(session);
            });
        }

        /// <summary>Window-wide Delete/Ctrl+D handling for <see cref="DeleteSelected"/>/
        /// <see cref="DuplicateSelected"/> - <see cref="PreviewKeyDown"/> (a tunneling
        /// event reaching this window before any child control's own bubbling
        /// <c>KeyDown</c>) rather than a routed <see cref="System.Windows.Input.KeyBinding"/>/
        /// <see cref="RoutedCommand"/>, so both keys work regardless of which control
        /// inside the window currently has keyboard focus (the viewport, the Outliner
        /// tree, a Properties panel field) - the same "reachable from anywhere in the
        /// window" reasoning <see cref="ApplicationCommands.Undo"/>/<see cref="ApplicationCommands.Redo"/>'s
        /// own <see cref="CommandBindings"/> already rely on (their default Ctrl+Z/Ctrl+Y
        /// gestures), just via a plain key check instead of a full <c>RoutedCommand</c>
        /// since there's no menu item/<c>InputGestureText</c> this needs to also drive.
        ///
        /// The one thing this must NEVER do is steal a keystroke away from an actively
        /// focused <see cref="TextBox"/> (the Properties Inspector's own Name/Position/
        /// Rotation/Scale/... fields, among others): Delete has its own native, entirely
        /// different meaning there (delete the character ahead of the caret), and this
        /// handler firing anyway would silently destroy the whole selected object (Object
        /// Mode) or mesh selection (Edit Mode) the moment someone tries to edit a text
        /// field with the Delete key - a real, easy-to-hit data-loss trap a window-wide
        /// tunneling handler is otherwise exactly positioned to spring. Checked once, up
        /// front, for BOTH keys below (Ctrl+D isn't a TextBox's own native shortcut
        /// either, so it could arguably still fire there safely, but skipping it too keeps
        /// this one rule simple and exception-free rather than needing a second, subtly
        /// different justification per key).</summary>
        private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!_isInitialized) return;
            if (e.OriginalSource is TextBox) return;

            if (e.Key == Key.Delete)
            {
                DeleteSelected();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.D && Keyboard.Modifiers == ModifierKeys.Control)
            {
                DuplicateSelected();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.NumPad0)
            {
                // Just flips the same checkbox "View > Active Camera" itself is - both
                // paths funnel into ActiveCameraViewCheckBox_Changed, the same "button and
                // key both call one shared method" shape DeleteSelected/DuplicateSelected
                // already use above.
                ActiveCameraViewCheckBox.IsChecked = ActiveCameraViewCheckBox.IsChecked != true;
                e.Handled = true;
            }
        }

        /// <summary>The Modifiers panel's "Add Mirror"/"Add Subsurf" buttons - append a
        /// new, default-settings modifier to the selected node's own stack.
        /// <see cref="NodeViewModel.AddMirrorModifier"/>/<see cref="NodeViewModel.AddSubdivisionSurfaceModifier"/>
        /// already call this view model's own "something changed" callback themselves
        /// (the same one wired to <see cref="SceneViewModel.SceneChanged"/> in this
        /// window's own constructor), so no explicit <see cref="Scene3DRenderer.Refresh"/>
        /// is needed here - it happens automatically, the same real-time-update path
        /// every other Properties Inspector edit already goes through.</summary>
        private void AddMirrorModifierButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            _sceneViewModel.SelectedNode?.AddMirrorModifier();
        }

        private void AddSubsurfModifierButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            _sceneViewModel.SelectedNode?.AddSubdivisionSurfaceModifier();
        }

        private void AddBooleanModifierButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            _sceneViewModel.SelectedNode?.AddBooleanModifier();
        }

        private void AddArrayModifierButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            _sceneViewModel.SelectedNode?.AddArrayModifier();
        }

        private void AddSolidifyModifierButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            _sceneViewModel.SelectedNode?.AddSolidifyModifier();
        }

        private void AddEdgeSplitModifierButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            _sceneViewModel.SelectedNode?.AddEdgeSplitModifier();
        }

        private void ShadeSmoothButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            _sceneViewModel.SelectedNode?.ShadeSmooth();
        }

        private void ShadeFlatButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            _sceneViewModel.SelectedNode?.ShadeFlat();
        }

        private void AddTrackToConstraintButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            _sceneViewModel.SelectedNode?.AddTrackToConstraint();
        }

        /// <summary>The Constraints panel's own per-entry "Remove" button - the clicked
        /// <see cref="Button"/>'s own DataContext (from its enclosing <c>DataTemplate</c>)
        /// IS the <see cref="ConstraintViewModelBase"/> to remove, the same pattern
        /// <see cref="RemoveModifierButton_Click"/> already uses.</summary>
        private void RemoveConstraintButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            if (sender is not FrameworkElement { DataContext: ConstraintViewModelBase constraintViewModel }) return;
            _sceneViewModel.SelectedNode?.RemoveConstraint(constraintViewModel);
        }

        /// <summary>The Modifiers panel's own per-entry "Remove" button - the clicked
        /// <see cref="Button"/>'s own DataContext (from its enclosing <c>DataTemplate</c>)
        /// IS the <see cref="ModifierViewModelBase"/> to remove, since each list item's
        /// template is data-bound to exactly one.</summary>
        private void RemoveModifierButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            if (sender is not FrameworkElement { DataContext: ModifierViewModelBase modifierViewModel }) return;
            _sceneViewModel.SelectedNode?.RemoveModifier(modifierViewModel);
        }

        /// <summary>The Properties panel's "Add Keyframe" button - records the selected
        /// node's CURRENT Position/Rotation/Scale at the timeline's own current playback
        /// position, using whichever of <see cref="LinearInterpolationButton"/>/<see cref="BezierInterpolationButton"/>
        /// is checked for the segment LEAVING this keyframe (see
        /// <see cref="AnimationTrack.AddKeyframeFromCurrentTransform"/>/<see cref="InterpolationMode"/>).
        /// Recording a keyframe at exactly the transform the node already has never
        /// changes its appearance, so no re-render is needed here.</summary>
        private void AddKeyframeButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            if (_sceneViewModel.SelectedNode?.UnderlyingNode is not { } node) return;

            var interpolation = BezierInterpolationButton.IsChecked == true ? InterpolationMode.Bezier : InterpolationMode.Linear;
            _timeline.GetOrCreateTrack(node).AddKeyframeFromCurrentTransform(_timeline.CurrentTime, interpolation);
        }

        /// <summary>The Material Inspector's "Load Clipbar Animation..." button - picks
        /// a <c>.jolie</c> project and imports it as an animated texture sequence on the
        /// selected node's material (see <see cref="ClipbarAnimationBridge"/>): a real
        /// Clipbar Animation project (one frame per "Frame NNN" layer) or a Sprite
        /// Sheet project (one frame per grid cell) both work; anything else is reported
        /// rather than silently failing. Installs the resulting track via
        /// <see cref="AnimationTimeline.SetTextureTrack"/> and applies/renders
        /// immediately, so the first frame shows right away rather than only once
        /// playback starts.</summary>
        private void LoadClipbarAnimationButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            if (_sceneViewModel.SelectedNode is not { } nodeViewModel) return;
            if (nodeViewModel.UnderlyingNode.Mesh?.Material is not { } material) return;

            var dialog = new OpenFileDialog
            {
                Filter = "JolieCat Projects (*.jolie)|*.jolie",
                Title = "Load Clipbar Animation",
            };
            if (dialog.ShowDialog(this) != true) return;

            TryRun("Load Clipbar Animation", () =>
            {
                var manifest = JolieProjectReader.ReadManifest(dialog.FileName);
                var cacheDirectory = GetTextureCacheDirectory();

                TextureAnimationTrack track;
                if (ClipbarReader.IsClipbarProject(manifest))
                    track = ClipbarAnimationBridge.CreateFromClipbarProject(material, dialog.FileName, cacheDirectory);
                else if (string.Equals(manifest.ProjectType, "SpriteSheet", StringComparison.OrdinalIgnoreCase))
                    track = ClipbarAnimationBridge.CreateFromSpriteSheetProject(material, dialog.FileName, cacheDirectory);
                else
                {
                    MessageBox.Show(this,
                        $"'{Path.GetFileName(dialog.FileName)}' is a '{manifest.ProjectType}' project - "
                        + "only Clipbar Animation and Sprite Sheet projects can be imported as an animated texture sequence.",
                        "JolieCat3D", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                _timeline.SetTextureTrack(track);
                _timeline.Apply();
                nodeViewModel.SyncFromCore();
                _renderer.Refresh();
            });
        }

        // ================= Animation playback transport =================

        /// <summary>Fires roughly every 1/60th of a second for the whole window's
        /// lifetime (started once in the constructor, stopped on Closed) - a no-op
        /// unless <see cref="AnimationTimeline.IsPlaying"/>, in which case it advances
        /// the timeline by its own nominal tick interval, applies the result to every
        /// animated node, and re-renders - the actual "preview 3D animations" loop.</summary>
        private void OnPlaybackTick(object? sender, EventArgs e)
        {
            if (!_timeline.IsPlaying) return;

            _timeline.Advance(_playbackTimer.Interval.TotalSeconds);
            _timeline.Apply();
            _sceneViewModel.SelectedNode?.SyncFromCore();
            _renderer.Refresh();
            RefreshAnimationUI();
        }

        private void PlayPauseButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            if (_timeline.IsPlaying) _timeline.Pause();
            else _timeline.Play();

            RefreshAnimationUI();
        }

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            _timeline.Stop();
            _timeline.Apply();
            _sceneViewModel.SelectedNode?.SyncFromCore();
            _renderer.Refresh();
            RefreshAnimationUI();
        }

        /// <summary>The frame scrubber - dragging it moves the timeline directly to
        /// that frame (rather than only while playing) and re-renders immediately, the
        /// standard "scrub to preview any moment" behavior an animation timeline needs.
        /// Guarded by <see cref="_isUpdatingAnimationUI"/> so <see cref="RefreshAnimationUI"/>
        /// setting <c>FrameScrubber.Value</c> to reflect a PLAYING timeline's own
        /// already-applied position doesn't loop back into re-applying (harmless, since
        /// it would just reassign the same value, but doubling the render work every
        /// single playback tick for nothing).</summary>
        private void FrameScrubber_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_isInitialized) return;
            if (_isUpdatingAnimationUI) return;

            _timeline.CurrentFrame = e.NewValue;
            _timeline.Apply();
            _sceneViewModel.SelectedNode?.SyncFromCore();
            _renderer.Refresh();
            RefreshAnimationUI();
        }

        /// <summary>The FPS field - purely a display/scrubber-granularity unit (see
        /// <see cref="AnimationTimeline.FrameRate"/>'s own remarks); an unparsable or
        /// non-positive value is simply ignored (the field re-reads as whatever it last
        /// validly was on the next <see cref="RefreshAnimationUI"/>, rather than
        /// throwing mid-edit).</summary>
        private void FpsTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isInitialized) return;
            if (_isUpdatingAnimationUI) return;
            if (!double.TryParse(FpsTextBox.Text, out var fps) || fps <= 0) return;

            _timeline.FrameRate = fps;
            RefreshAnimationUI();
        }

        /// <summary>Syncs the transport panel's own controls (scrubber position/range,
        /// frame label, Play/Pause button text) from <see cref="_timeline"/>'s current
        /// state - called after anything changes it (a tick, Play/Pause/Stop, a scrub,
        /// an FPS edit). Guards every write with <see cref="_isUpdatingAnimationUI"/> so
        /// setting <c>FrameScrubber.Value</c> here doesn't re-trigger
        /// <see cref="FrameScrubber_ValueChanged"/> as if the user had dragged it.</summary>
        private void RefreshAnimationUI()
        {
            _isUpdatingAnimationUI = true;
            try
            {
                FrameScrubber.Maximum = _timeline.TotalFrames;
                FrameScrubber.Value = _timeline.CurrentFrame;
                FrameLabel.Text = $"{_timeline.CurrentFrame:0} / {_timeline.TotalFrames}";
                PlayPauseButton.Content = _timeline.IsPlaying ? "Pause" : "Play";
            }
            finally
            {
                _isUpdatingAnimationUI = false;
            }
        }

        /// <summary>The Properties panel's "Load Texture..." button - loads an image
        /// (typically something exported from <c>JolieCat</c>'s own 2D editor workspace)
        /// through <see cref="TwoDAssetBridge"/> and adopts it as the selected node's
        /// material's whole-image diffuse texture (see <see cref="NodeViewModel.SetDiffuseTexture"/>).
        /// Disabled implicitly by the same <c>HasMaterial</c> visibility the rest of the
        /// Material section already uses - this can only ever be clicked with a
        /// mesh+material actually selected.</summary>
        private void LoadTextureButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            if (_sceneViewModel.SelectedNode is not { } nodeViewModel) return;

            var dialog = new OpenFileDialog
            {
                Filter = "Images (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|All files (*.*)|*.*",
                Title = "Load Texture",
            };
            if (dialog.ShowDialog(this) != true) return;

            TryRun("Load Texture", () =>
            {
                // TwoDAssetBridge.LoadTextureMaterial is the actual interop bridge (see
                // its own remarks) - this adopts the DiffuseTexturePath it resolves onto
                // the node's EXISTING material rather than replacing the whole material,
                // which would also discard its DiffuseColor/specular settings.
                var loaded = TwoDAssetBridge.LoadTextureMaterial(dialog.FileName);
                nodeViewModel.SetDiffuseTexture(loaded.DiffuseTexturePath!);
                _textureSources[nodeViewModel.UnderlyingNode] = new TextureSource(dialog.FileName, IsJolieProject: false);
                _renderer.Refresh();
            });
        }

        /// <summary>The Properties panel's "Load Normal Map..." button - a plain file path
        /// assignment (see <see cref="NodeViewModel.SetNormalTexture"/>), unlike
        /// <see cref="LoadTextureButton_Click"/> this doesn't go through
        /// <see cref="TwoDAssetBridge"/>/<see cref="_textureSources"/> at all: a normal map
        /// is never something <c>JolieCat</c>'s own 2D editor exports or live-watches, so
        /// there is no equivalent "reload when this file changes" bridge for it to
        /// register with.</summary>
        private void LoadNormalMapButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            if (_sceneViewModel.SelectedNode is not { } nodeViewModel) return;

            var dialog = new OpenFileDialog
            {
                Filter = "Images (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|All files (*.*)|*.*",
                Title = "Load Normal Map",
            };
            if (dialog.ShowDialog(this) != true) return;

            TryRun("Load Normal Map", () =>
            {
                nodeViewModel.SetNormalTexture(dialog.FileName);
                _renderer.Refresh();
            });
        }

        /// <summary>The Properties panel's "Load Metallic/Roughness Map..." button - see
        /// <see cref="LoadNormalMapButton_Click"/>'s own remarks; the picked image is
        /// expected to already be packed in the glTF metallicRoughness convention
        /// (roughness in green, metallic in blue - see
        /// <see cref="Core.Materials.Material.MetallicRoughnessTexturePath"/>'s own
        /// remarks), this button does no repacking of its own.</summary>
        private void LoadMetallicRoughnessMapButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            if (_sceneViewModel.SelectedNode is not { } nodeViewModel) return;

            var dialog = new OpenFileDialog
            {
                Filter = "Images (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|All files (*.*)|*.*",
                Title = "Load Metallic/Roughness Map",
            };
            if (dialog.ShowDialog(this) != true) return;

            TryRun("Load Metallic/Roughness Map", () =>
            {
                nodeViewModel.SetMetallicRoughnessTexture(dialog.FileName);
                _renderer.Refresh();
            });
        }

        /// <summary>The Material Inspector's "Load From JolieCat Project..." button -
        /// picks a <c>.jolie</c> project file and extracts its active layer's own bitmap
        /// (via <see cref="JolieProjectReader.ExtractActiveLayerTexture"/>) as this
        /// node's diffuse texture. Remembers the source <c>.jolie</c> path against this
        /// node (see <see cref="_textureSources"/>) so a later
        /// <see cref="JolieWorkspaceWatcher.AssetChanged"/> for that same file
        /// re-extracts and refreshes automatically - see <see cref="ReloadNodeTexture"/>.</summary>
        private void LoadFromJolieProjectButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            if (_sceneViewModel.SelectedNode is not { } nodeViewModel) return;

            var dialog = new OpenFileDialog
            {
                Filter = "JolieCat Projects (*.jolie)|*.jolie",
                Title = "Load From JolieCat Project",
            };
            if (dialog.ShowDialog(this) != true) return;

            TryRun("Load From JolieCat Project", () =>
            {
                var texturePath = JolieProjectReader.ExtractActiveLayerTexture(dialog.FileName, GetTextureCacheDirectory());
                nodeViewModel.SetDiffuseTexture(texturePath);
                _textureSources[nodeViewModel.UnderlyingNode] = new TextureSource(dialog.FileName, IsJolieProject: true);
                _renderer.Refresh();
            });
        }

        private void PlanarProjectionButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            ApplyUVProjection(UVProjectionMode.Planar);
        }

        private void BoxProjectionButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            ApplyUVProjection(UVProjectionMode.Box);
        }

        private void SphericalProjectionButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            ApplyUVProjection(UVProjectionMode.Spherical);
        }

        private void ApplyUVProjection(UVProjectionMode mode)
        {
            if (_sceneViewModel.SelectedNode is not { } nodeViewModel) return;

            nodeViewModel.ApplyUVProjection(mode);
            _renderer.Refresh();
        }

        /// <summary>The Material Inspector's "View UV Map..." button - opens a fresh,
        /// non-modal <see cref="UVVisualizerWindow"/> (see its own remarks) for the
        /// selected node's current mesh/diffuse texture. Owned by this window so it
        /// minimizes/closes together with it, but otherwise doesn't block interacting
        /// with the main viewport at all. A no-op for a node with no mesh (nothing to
        /// show UVs for).</summary>
        private void ViewUVMapButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            if (_sceneViewModel.SelectedNode is not { } nodeViewModel) return;
            if (nodeViewModel.UnderlyingNode.Mesh is not { } mesh) return;

            var window = new UVVisualizerWindow(mesh, mesh.Material?.DiffuseTexturePath, nodeViewModel.Name) { Owner = this };
            window.Show();
        }

        /// <summary>The Material Inspector's "Add Slot" button - Multi-Material
        /// Support's own <see cref="NodeViewModel.AddMaterialSlot"/>.</summary>
        private void AddMaterialSlotButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            _sceneViewModel.SelectedNode?.AddMaterialSlot();
        }

        /// <summary>Each Material Slot row's own "Remove" button - the clicked
        /// <see cref="Button"/>'s own <c>DataContext</c> (from its enclosing
        /// <c>DataTemplate</c>) IS the <see cref="MaterialSlotViewModel"/> to remove, the
        /// same pattern <see cref="RemoveModifierButton_Click"/> already uses.</summary>
        private void RemoveMaterialSlotButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            if (sender is not FrameworkElement { DataContext: MaterialSlotViewModel slotViewModel }) return;
            _sceneViewModel.SelectedNode?.RemoveMaterialSlot(slotViewModel);
        }

        private void AddCurvePointButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            _sceneViewModel.SelectedNode?.AddCurvePoint();
        }

        /// <summary>Each Curve control point row's own "Remove" button - the clicked
        /// <see cref="Button"/>'s own <c>DataContext</c> (from its enclosing
        /// <c>DataTemplate</c>) IS the <see cref="CurvePointViewModel"/> to remove, the
        /// same pattern <see cref="RemoveModifierButton_Click"/>/<see cref="RemoveMaterialSlotButton_Click"/>
        /// already use.</summary>
        private void RemoveCurvePointButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            if (sender is not FrameworkElement { DataContext: CurvePointViewModel pointViewModel }) return;
            _sceneViewModel.SelectedNode?.RemoveCurvePoint(pointViewModel);
        }

        private void SetBoneRestPoseButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            _sceneViewModel.SelectedNode?.SetBoneRestPose();
        }

        /// <summary>Each Material Slot row's own "Assign" button - points whichever
        /// single face is currently fully selected (Edit Mode, Face component) at this
        /// row's own slot, via <see cref="MeshEditSession.AssignMaterialSlotToSelectedFace"/>,
        /// wrapped into an undoable, recorded <see cref="MeshEditCommand"/> the same way
        /// <see cref="ExtrudeButton_Click"/> already is. Reports back rather than
        /// silently doing nothing if no single whole face is currently selected.</summary>
        private void AssignMaterialSlotButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            if (sender is not FrameworkElement { DataContext: MaterialSlotViewModel slotViewModel }) return;

            var session = _renderer.EditSession;
            if (session.Target is not { } node) return;

            TryRun("Assign Material Slot", () =>
            {
                var command = MeshEditCommandFactory.Capture(node, "Assign Material Slot",
                    () => session.AssignMaterialSlotToSelectedFace(slotViewModel.SlotIndex),
                    onChanged: () => _renderer.Refresh());

                if (command is null)
                {
                    MessageBox.Show(this,
                        "Select a single whole face (Edit Mode, Face component) to assign this material slot to.",
                        "JolieCat3D", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                _commandHistory.Record(command);
                _renderer.Refresh();
            });
        }

        /// <summary>Where extracted <c>.jolie</c> layer textures are cached - a
        /// subfolder of the temp directory, not the project file's own folder (which
        /// may not even be writable, or may be a location JolieCat 2D itself watches).</summary>
        private static string GetTextureCacheDirectory() => Path.Combine(Path.GetTempPath(), "JolieCat3D", "JolieTextureCache");

        /// <summary>File > Watch JolieCat Workspace... - picks a folder and starts a
        /// <see cref="JolieWorkspaceWatcher"/> on it. Every subsequent
        /// <see cref="JolieWorkspaceWatcher.AssetChanged"/> is handled by
        /// <see cref="HandleWorkspaceAssetChanged"/> - the actual "automatically detect,
        /// load, or refresh" bridge in action. The PREVIOUS watch (if any) is only
        /// stopped/disposed once the NEW one has successfully started - constructing or
        /// starting a <see cref="JolieWorkspaceWatcher"/> can throw (the chosen folder
        /// was deleted/unmounted between the picker dialog and this call, a permissions
        /// error, ...), and a failed attempt to switch folders should never leave the
        /// user with NO active watch at all when they already had a working one; see
        /// <see cref="TryRun"/> for how that failure itself is reported.</summary>
        private void WatchWorkspaceMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;

            var dialog = new OpenFolderDialog { Title = "Watch JolieCat Workspace Folder" };
            if (dialog.ShowDialog(this) != true) return;

            TryRun("Watch JolieCat Workspace", () =>
            {
                var watcher = new JolieWorkspaceWatcher(dialog.FolderName);
                // FileSystemWatcher raises its events on a ThreadPool thread, never this
                // window's own dispatcher thread - every access to _textureSources, a
                // NodeViewModel, or _renderer from the handler MUST be marshaled back via
                // Dispatcher.Invoke first, or it risks a cross-thread WPF access exception
                // (or, for the plain Dictionary read, a race with the UI thread).
                watcher.AssetChanged += (_, args) => Dispatcher.Invoke(() => HandleWorkspaceAssetChanged(args));

                try
                {
                    watcher.Start();
                }
                catch
                {
                    watcher.Dispose();
                    throw;
                }

                _workspaceWatcher?.Dispose();
                _workspaceWatcher = watcher;

                Title = $"JolieCat3D - {(_currentFilePath is null ? "Untitled" : Path.GetFileName(_currentFilePath))} [watching {dialog.FolderName}]";
                RefreshLiveSyncStatus();
            });
        }

        /// <summary>The Status Bar's "Watch.../Stop" button - the same
        /// <see cref="WatchWorkspaceMenuItem_Click"/> folder-picker flow while nothing is
        /// currently watched, or <see cref="StopWatchingWorkspace"/> to end an active one -
        /// a single button doing whichever of the two currently makes sense, the same
        /// "one control, current state decides which action it performs" shape a
        /// Play/Pause transport button already uses in this window.</summary>
        private void LiveSyncToggleButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;

            if (_workspaceWatcher is not null) StopWatchingWorkspace();
            else WatchWorkspaceMenuItem_Click(sender, e);
        }

        /// <summary>Ends the current <see cref="JolieWorkspaceWatcher"/> (if any) - a
        /// no-op if nothing is currently being watched. <see cref="_textureSources"/> is
        /// deliberately left untouched: stopping Live Sync only stops AUTOMATIC reloads
        /// going forward, it doesn't forget which file each node's texture came from (a
        /// later "Watch..." on the same folder should resume tracking exactly where it
        /// left off).</summary>
        private void StopWatchingWorkspace()
        {
            if (_workspaceWatcher is null) return;

            _workspaceWatcher.Dispose();
            _workspaceWatcher = null;

            Title = $"JolieCat3D - {(_currentFilePath is null ? "Untitled" : Path.GetFileName(_currentFilePath))}";
            RefreshLiveSyncStatus();
        }

        /// <summary>Syncs the Status Bar's own Live Sync indicator (the colored dot,
        /// status text, and the Watch.../Stop button's own label) with whether
        /// <see cref="_workspaceWatcher"/> is currently set - called after anything
        /// changes it (starting a watch, stopping one).</summary>
        private void RefreshLiveSyncStatus()
        {
            var isWatching = _workspaceWatcher is not null;

            LiveSyncIndicatorEllipse.Fill = isWatching
                ? (Brush)FindResource("ActiveIndicatorBrush")
                : (Brush)FindResource("BorderBrush");
            LiveSyncStatusText.Text = isWatching
                ? $"Live Sync: watching {_workspaceWatcher!.WorkspacePath}"
                : "Live Sync: Off";
            LiveSyncToggleButton.Content = isWatching ? "Stop" : "Watch...";
        }

        /// <summary>Reloads every node whose currently-tracked texture source (see
        /// <see cref="_textureSources"/>) is the exact file <paramref name="args"/>
        /// reports changed - a <c>.jolie</c> project re-saved, or a plain image
        /// re-exported, by JolieCat 2D. Silently does nothing for a changed file no
        /// node is currently tracking (the common case - most workspace activity has
        /// nothing to do with whatever's loaded into this scene right now).</summary>
        private void HandleWorkspaceAssetChanged(WorkspaceAssetChangedEventArgs args)
        {
            foreach (var (node, source) in _textureSources)
            {
                if (!string.Equals(source.SourcePath, args.FilePath, StringComparison.OrdinalIgnoreCase)) continue;
                ReloadNodeTexture(node, source);
            }
        }

        private void ReloadNodeTexture(Node node, TextureSource source)
        {
            if (_sceneViewModel.FindViewModel(node) is not { } nodeViewModel) return;

            TryRun("Refresh Texture", () =>
            {
                var texturePath = source.IsJolieProject
                    ? JolieProjectReader.ExtractActiveLayerTexture(source.SourcePath, GetTextureCacheDirectory())
                    : source.SourcePath;

                nodeViewModel.SetDiffuseTexture(texturePath);
                _renderer.Refresh();
            });
        }

        /// <summary>
        /// A small, self-contained scene proving every layer of the pipeline this window
        /// exists to verify actually connects end to end: a <see cref="JolieCat3D.Core"/>
        /// <see cref="Mesh"/> (from <see cref="Primitives.CreateCube"/>), a
        /// <see cref="Material"/>, a parent/child <see cref="Node"/> hierarchy (proving
        /// <see cref="Node.GetWorldTransform"/>'s composition, not just one node's own
        /// local transform), assembled into a <see cref="Scene3D"/> and handed to
        /// <see cref="Scene3DRenderer.Render"/> - the exact same call any future
        /// real-content loader in this project would make.
        /// </summary>
        private static Scene3D BuildDemoScene()
        {
            var scene = new Scene3D("Demo Scene");

            // Brand accent colors, not arbitrary/primary ones - the same hex the 2D
            // JolieCat.UI editor's own AccentBrush/ActiveIndicatorBrush use, so a demo
            // mesh in this viewport reads as part of the same product family rather
            // than a generic 3D-engine placeholder cube.
            var goldMaterial = new Material("Gold")
            {
                DiffuseColor = Color4.FromBytes(0xC2, 0x9B, 0x58), // JolieCat AccentBrush
                SpecularColor = new Color4(1f, 1f, 1f),
                SpecularPower = 40.0,
            };

            var parentCube = Primitives.CreateCube(1.5f, "ParentCube");
            parentCube.Material = goldMaterial;

            var parentNode = new Node("Parent")
            {
                Mesh = parentCube,
                // A small constant spin, just so the parent cube isn't perfectly
                // axis-aligned - makes it obvious at a glance that LocalRotation is
                // actually being applied, not silently ignored.
                LocalRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 6f),
            };
            scene.AddRootNode(parentNode);

            var emeraldMaterial = new Material("Emerald")
            {
                DiffuseColor = Color4.FromBytes(0x5D, 0xA4, 0x53), // JolieCat ActiveIndicatorBrush
                SpecularColor = new Color4(0.8f, 0.8f, 0.8f),
                SpecularPower = 60.0,
            };

            var childCube = Primitives.CreateCube(0.5f, "ChildCube");
            childCube.Material = emeraldMaterial;

            // Offset and scaled relative to the parent - orbiting the demo scene should
            // show this cube riding along with the parent's own position/rotation, not
            // sitting fixed in world space, proving Node.GetWorldTransform's parent/child
            // composition (see SceneGraphBuilder) rather than just one flat node list.
            var childNode = new Node("Child")
            {
                Mesh = childCube,
                LocalPosition = new Vector3(2.0f, 0.75f, 0f),
                LocalScale = new Vector3(0.75f, 0.75f, 0.75f),
            };
            parentNode.AddChild(childNode);

            var groundMaterial = new Material("Ground")
            {
                DiffuseColor = Color4.FromBytes(0x6B, 0x67, 0x70), // muted gray-brown, matches the brand palette's Panels & Toolbars tone
                SpecularColor = Color4.Black,
            };

            var groundMesh = Primitives.CreatePlane(8f, 8f, "Ground");
            groundMesh.Material = groundMaterial;

            var groundNode = new Node("GroundPlane")
            {
                Mesh = groundMesh,
                LocalPosition = new Vector3(0f, -1.5f, 0f),
            };
            scene.AddRootNode(groundNode);

            return scene;
        }
    }
}
