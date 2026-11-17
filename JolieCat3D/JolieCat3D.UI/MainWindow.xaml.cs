using System.IO;
using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using JolieCat3D.Core.Camera;
using JolieCat3D.Core.Geometry;
using JolieCat3D.Core.Materials;
using JolieCat3D.Core.Numerics;
using JolieCat3D.Core.Scene;
using JolieCat3D.Core.Sculpting;
using JolieCat3D.Core.Skinning;
using JolieCat3D.Engine.Editing;
using JolieCat3D.Engine.Gizmos;
using JolieCat3D.Engine.Lighting;
using JolieCat3D.Engine.Rendering;
using JolieCat3D.Engine.Viewport;
using JolieCat3D.Service;
using JolieCat3D.Service.Animation;
using JolieCat3D.Service.Commands;
using JolieCat3D.Service.Export;
using JolieCat3D.Service.Interop;
using JolieCat3D.Service.Project;
using JolieCat3D.UI.ViewModels;
using Microsoft.Win32;

namespace JolieCat3D.UI
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        /// <summary>The Tier 2 Viewport Header's own Interaction Mode combo box's
        /// current selection ("Object", "Edit", "WeightPaint", "VertexPaint", "Sculpt",
        /// or "TexturePaint" - the same Tag strings <see cref="ModeComboBox_SelectionChanged"/>
        /// already reads off each <see cref="ComboBoxItem"/>) - a real WPF
        /// <see cref="DependencyProperty"/> (not a plain field) specifically so the Left
        /// Vertical Toolbar's own per-mode sections can each bind a <c>DataTrigger</c>
        /// against it directly in XAML (<c>{Binding CurrentEditorMode, RelativeSource=
        /// {RelativeSource AncestorType=Window}}</c>) to show/hide themselves - the
        /// task's own "use XAML DataTriggers based on the Interaction Mode to dynamically
        /// swap the contents of this left toolbar" ask - rather than every section
        /// needing its own imperative Visibility assignment in code-behind the way the
        /// 4 paint-mode toolbars used to.</summary>
        public static readonly DependencyProperty CurrentEditorModeProperty =
            DependencyProperty.Register(nameof(CurrentEditorMode), typeof(string), typeof(MainWindow), new PropertyMetadata("Object"));

        public string CurrentEditorMode
        {
            get => (string)GetValue(CurrentEditorModeProperty);
            set => SetValue(CurrentEditorModeProperty, value);
        }

        // "3D Models (*.obj;*.stl;*.glb;*.gltf)" first, so it's the default choice in
        // both dialogs; the format-specific entries after it are what actually determine
        // each format's own default *extension* SaveFileDialog appends when a user types
        // a bare name with no extension at all.
        private const string FileDialogFilter =
            $"{MeshFileService.AnyMeshFilter}|{MeshFileService.ObjFilter}|{MeshFileService.StlFilter}|{MeshFileService.GltfFilter}";

        // Open accepts projects and plain meshes; Save/Save As write projects only.
        private const string OpenDialogFilter =
            $"All supported (*.jolie3d;*.obj;*.stl;*.glb;*.gltf)|*.jolie3d;*.obj;*.stl;*.glb;*.gltf|{Jolie3DProjectSerializer.FileFilter}|{FileDialogFilter}";

        private readonly EditorViewport _renderer;
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
        private AnimationTimeline _timeline = new();
        private readonly DispatcherTimer _playbackTimer;
        private bool _isUpdatingAnimationUI;
        private bool _isUpdatingCameraViewUI;

        /// <summary>The Dopesheet's own currently-selected keyframe marker time (seconds,
        /// matching <see cref="Keyframe.Time"/> exactly) - null with nothing selected, in
        /// which case <see cref="DeleteKeyframeButton"/> is disabled. Cleared whenever the
        /// selected node itself changes (see <see cref="SelectNode"/>) - a keyframe time
        /// selected against one node's own <see cref="AnimationTrack"/> is meaningless
        /// once a DIFFERENT node's track is what the Dopesheet is now showing.</summary>
        private double? _selectedKeyframeTime;

        /// <summary>True from a Dopesheet marker's own <c>MouseLeftButtonDown</c> (mouse
        /// capture acquired) until its matching <c>MouseLeftButtonUp</c> (capture
        /// released) - guards <see cref="RefreshDopesheet"/> against tearing down the
        /// very marker currently being dragged out from under its own live mouse capture
        /// (every marker is a brand new <see cref="System.Windows.Shapes.Polygon"/>
        /// instance each time <see cref="RefreshDopesheet"/> runs - see its own "rebuild,
        /// don't patch in place" remarks - so rebuilding mid-drag would silently end the
        /// gesture, the SAME hazard <see cref="Gizmos.TransformGizmo.Rebuild"/>'s own
        /// remarks already describe for its manipulators, reachable here via
        /// <see cref="RefreshAnimationUI"/>'s own ~60Hz <see cref="OnPlaybackTick"/> call
        /// firing WHILE a keyframe drag happens to also be in progress).</summary>
        private bool _isDraggingKeyframeMarker;

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
            // this isn't just baked into viewport's/LightingSettings' own defaults.
            _renderer = Viewport;
            _renderer.Lighting = ViewportTheme.CreateDarkThemeLighting();
            _gizmo = new TransformGizmo(Viewport) { SceneNodes = () => _currentScene.Traverse() };
            _componentGizmo = new ComponentGizmo(Viewport);

            // The whole object selection moves with the gizmo; one undo step per gesture.
            _gizmo.AdditionalTargets = () => _objectSelection;
            _gizmo.MultiTransformCommitted += (_, args) =>
            {
                var commands = args.Changes.Select(c => (IUndoableCommand)new TransformNodeCommand(c.Target, c.Before, c.After, "Transform", onChanged: () =>
                {
                    _renderer.Refresh();
                    _sceneViewModel.FindViewModel(c.Target)?.SyncFromCore();
                })).ToList();
                commands.AddRange(AutoKeyCommands(args.Changes.Select(c => c.Target)));
                _commandHistory.Record(new CompositeCommand("Transform Objects", commands));
            };

            // Gizmos draw into the viewport's overlay every frame.
            _renderer.BuildOverlay += list =>
            {
                if (IsEditMode) _componentGizmo.Draw(list);
                else if (!IsPaintOrSculptMode) _gizmo.Draw(list);
                DrawMotionPath(list);
            };
            _commandHistory.Changed += (_, _) => InvalidateMotionPath();
            _renderer.BrushRadiusProvider = () =>
                IsSculptMode ? _renderer.SculptSession.BrushRadius
                : IsWeightPaintMode ? _renderer.WeightPaintSession.BrushRadius
                : IsVertexPaintMode ? _renderer.VertexPaintSession.BrushRadius
                : null;

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
            // viewport.OnPilotedCameraChanged) - resync the Properties Inspector
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
                var keys = AutoKeyCommands(new[] { args.Target });
                _commandHistory.Record(keys.Count == 0 ? command : new CompositeCommand("Transform Object", new IUndoableCommand[] { command }.Concat(keys)));
            };

            // A component-gizmo drag mutates the target mesh's own vertex positions
            // directly (see MeshEditSession.ApplyTranslation) - Refresh() re-renders the
            // scene from that updated Core.Geometry.Mesh AND rebuilds the marker overlay
            // (viewport.Refresh already calls RefreshComponentOverlay itself), so
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
            // Pending Inspector edits are committed first, so Ctrl+Z always undoes the
            // most recent change.
            CommandBindings.Add(new CommandBinding(ApplicationCommands.Undo,
                (_, _) => { CommitPendingEdit(); _commandHistory.Undo(); _shaderEditor?.Rebuild(); },
                (_, e) => e.CanExecute = _commandHistory.CanUndo || _pendingEditDescription is not null));
            CommandBindings.Add(new CommandBinding(ApplicationCommands.Redo,
                (_, _) => { CommitPendingEdit(); _commandHistory.Redo(); _shaderEditor?.Rebuild(); },
                (_, e) => e.CanExecute = _commandHistory.CanRedo));

            // Generic undo for everything that goes through a NodeViewModel (Inspector
            // fields, modifiers, constraints, materials, curves, UV projection, ...).
            _sceneViewModel.SceneChanged += (_, _) => ScheduleEditCommit("Edit Properties");
            _editCommitTimer.Tick += (_, _) => CommitPendingEdit();
            _commandHistory.Changed += (_, _) => { if (!_isCommittingEdit) ResetEditBaseline(); };
            _commandHistory.Changed += (_, _) => { _graphEditor?.Redraw(); _uvEditor?.Redraw(); };
            _sceneViewModel.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName != nameof(SceneViewModel.SelectedNode)) return;
                CommitPendingEdit(); // still targets the previously selected node
                ResetEditBaseline();
            };

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

            // Unsaved-changes hints: any refresh, undo-stack change or inspector edit.
            _renderer.Refreshed += (_, _) => MarkChanged();
            _commandHistory.Changed += (_, _) => MarkChanged();
            _sceneViewModel.SceneChanged += (_, _) => MarkChanged();
            _dirtyCheckTimer.Tick += (_, _) => { _dirtyCheckTimer.Stop(); HasRealUnsavedChanges(); };

            PreviewMouseDown += OnWindowPreviewMouseDown;
            PreviewMouseWheel += (_, e) =>
            {
                if (_modalMeshOp?.OnWheel is not { } wheel) return;
                wheel(Math.Sign(e.Delta));
                UpdateModalMeshOperation(Mouse.GetPosition(Viewport));
                e.Handled = true;
            };

            Closing += (_, e) =>
            {
                if (!ConfirmDiscardIfNeeded("closing")) e.Cancel = true;
            };

            LoadScene(BuildDemoScene(), filePath: null);
            RefreshAnimationUI();
            RefreshLiveSyncStatus();
            RebuildRecentMenu();

            // Opening a .jolie3d from Explorer / the command line.
            var args = Environment.GetCommandLineArgs();
            if (args.Length > 1 && File.Exists(args[1])) OpenFile(args[1]);

            // Only past this point is every field this window's own event handlers touch
            // actually assigned - see _isInitialized's own remarks.
            _isInitialized = true;
        }

        // ================= File menu =================

        /// <summary>Replaces the whole current scene - the Outliner, viewport, gizmo,
        /// and selection all reset together, and <paramref name="filePath"/> becomes
        /// what <see cref="SaveScene"/> writes back to (null, for a scene that isn't
        /// backed by a file yet - New, or the built-in demo scene at startup).</summary>
        private void LoadScene(Scene3D scene, string? filePath, AnimationTimeline? timeline = null, string? displayName = null)
        {
            _currentScene = scene;
            _currentFilePath = filePath;
            _documentName = displayName ?? (filePath is null ? "Untitled" : Path.GetFileName(filePath));

            _timeline.Stop();
            _timeline = timeline ?? new AnimationTimeline();

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

            if (_isInitialized) RefreshAnimationUI();
            MarkSaved();
        }

        // ================= Unsaved-changes tracking =================

        // _savedSnapshot is a hash of the project data at the last save/load. Edits only
        // raise the cheap _hasUnsavedChanges hint; whether the scene really differs is
        // decided by re-hashing (debounced for the title, immediately before a prompt),
        // so an undo back to the saved state clears the "*" again.
        private byte[]? _savedSnapshot;
        private bool _hasUnsavedChanges;
        private string _documentName = "Untitled";
        private readonly DispatcherTimer _dirtyCheckTimer = new() { Interval = TimeSpan.FromMilliseconds(800) };

        private byte[] ComputeSnapshot()
        {
            var data = Jolie3DProjectSerializer.BuildData(_currentScene, _timeline);
            return System.Security.Cryptography.SHA256.HashData(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(data));
        }

        private void MarkSaved()
        {
            try { _savedSnapshot = ComputeSnapshot(); }
            catch (Exception) { _savedSnapshot = null; }
            _hasUnsavedChanges = false;
            _dirtyCheckTimer.Stop();
            UpdateTitle();
        }

        private void MarkChanged()
        {
            if (!_hasUnsavedChanges)
            {
                _hasUnsavedChanges = true;
                UpdateTitle();
            }
            _dirtyCheckTimer.Stop();
            _dirtyCheckTimer.Start();
        }

        /// <summary>True only if the scene really differs from the last saved/loaded state.</summary>
        private bool HasRealUnsavedChanges()
        {
            if (!_hasUnsavedChanges) return false;

            try
            {
                if (_savedSnapshot is not null && ComputeSnapshot().AsSpan().SequenceEqual(_savedSnapshot))
                {
                    _hasUnsavedChanges = false;
                    UpdateTitle();
                    return false;
                }
            }
            catch (Exception)
            {
                // Can't hash it - assume it changed, so the user still gets asked.
            }
            return true;
        }

        private void UpdateTitle() =>
            Title = $"JolieCat3D - {_documentName}{(_hasUnsavedChanges ? " *" : string.Empty)}";

        private void NewScene()
        {
            if (!ConfirmDiscardIfNeeded("starting a new scene")) return;
            LoadScene(new Scene3D("Untitled"), filePath: null);
        }

        private void OpenScene()
        {
            if (!ConfirmDiscardIfNeeded("opening another file")) return;

            var dialog = new OpenFileDialog { Filter = OpenDialogFilter, Title = "Open" };
            if (dialog.ShowDialog(this) != true) return;

            OpenFile(dialog.FileName);
        }

        /// <summary>A .jolie3d opens as the project itself; a mesh file (OBJ/STL/glTF)
        /// opens as a new unsaved project, so Save never silently overwrites the
        /// original model with a lossy export.</summary>
        private void OpenFile(string path)
        {
            TryRun("Open", () =>
            {
                if (Jolie3DProjectSerializer.IsProjectFile(path))
                {
                    var (scene, timeline) = Jolie3DProjectSerializer.Load(path);
                    LoadScene(scene, path, timeline);
                    AddRecentFile(path);
                }
                else
                {
                    var (importedScene, importedTimeline) = MeshFileService.ImportSceneWithAnimation(path);
                    LoadScene(importedScene, filePath: null, importedTimeline,
                        displayName: Path.GetFileNameWithoutExtension(path) + Jolie3DProjectSerializer.FileExtension);
                    // Not saved as a project yet - treat it as a change so closing asks.
                    _savedSnapshot = null;
                    MarkChanged();
                }
            });
        }

        private bool SaveScene()
        {
            if (_currentFilePath is null) return SaveSceneAs();
            return SaveProjectTo(_currentFilePath);
        }

        private bool SaveSceneAs()
        {
            var dialog = new SaveFileDialog
            {
                Filter = Jolie3DProjectSerializer.FileFilter,
                DefaultExt = Jolie3DProjectSerializer.FileExtension,
                Title = "Save Project As",
                FileName = Path.GetFileNameWithoutExtension(_currentFilePath ?? _documentName),
            };
            if (dialog.ShowDialog(this) != true) return false;

            return SaveProjectTo(dialog.FileName);
        }

        private bool SaveProjectTo(string path)
        {
            var ok = false;
            TryRun("Save", () =>
            {
                Jolie3DProjectSerializer.Save(_currentScene, _timeline, path);
                _currentFilePath = path;
                _documentName = Path.GetFileName(path);
                MarkSaved();
                AddRecentFile(path);
                ok = true;
            });
            return ok;
        }

        // ================= Recent files =================

        private static readonly string RecentFilesPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "JolieCat3D", "recent.txt");

        private static List<string> LoadRecentFiles()
        {
            try { return File.Exists(RecentFilesPath) ? File.ReadAllLines(RecentFilesPath).Where(l => l.Length > 0).ToList() : new(); }
            catch (Exception) { return new(); }
        }

        private void AddRecentFile(string path)
        {
            var list = LoadRecentFiles();
            list.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
            list.Insert(0, path);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(RecentFilesPath)!);
                File.WriteAllLines(RecentFilesPath, list.Take(10));
            }
            catch (Exception) { /* recent list is a convenience only */ }
            RebuildRecentMenu();
        }

        private void RebuildRecentMenu()
        {
            RecentFilesMenuItem.Items.Clear();
            var list = LoadRecentFiles();
            foreach (var path in list)
            {
                var item = new MenuItem { Header = path.Replace("_", "__") };
                item.Click += (_, _) =>
                {
                    if (!File.Exists(path)) { MessageBox.Show(this, $"File not found:\n{path}", "JolieCat3D"); return; }
                    if (ConfirmDiscardIfNeeded("opening another file")) OpenFile(path);
                };
                RecentFilesMenuItem.Items.Add(item);
            }
            RecentFilesMenuItem.IsEnabled = list.Count > 0;
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
                var (imported, importedTimeline) = MeshFileService.ImportSceneWithAnimation(dialog.FileName);
                var roots = imported.RootNodes.ToList();
                foreach (var root in roots) imported.RemoveRootNode(root);

                var commands = roots.Select(root => (IUndoableCommand)new AddNodeCommand(_currentScene, null, root, "Import", OnSceneStructureChanged)).ToList();
                if (importedTimeline is not null)
                    commands.Add(new MergeActionsCommand(() => _timeline, importedTimeline, OnActionsChanged));
                var command = new CompositeCommand("Import", commands);
                _commandHistory.Execute(command);
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

        private void AddTextMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            var dialog = new AddTextWindow { Owner = this };
            if (dialog.ShowDialog() != true || dialog.Result is not { } options) return;
            TryRun("Add Text", () =>
            {
                var mesh = Engine.Text.TextOutline.BuildMesh(options.Text, options.Font, options.Height, options.Depth, options.Bold, options.Italic);
                if (mesh.Vertices.Count == 0)
                {
                    Viewport.ShowMessage("That text has no visible glyphs in this font.");
                    return;
                }
                AddNodeToScene(new Node(mesh.Name) { Mesh = mesh });
            });
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
        /// "active"/"set as active" step needed. <see cref="Node.Mesh"/> is set
        /// immediately from <see cref="LightData.GenerateMesh"/> (the same "visible the
        /// instant it's added" convention <see cref="AddCurveMenuItem_Click"/> already
        /// follows for its own auto-generated mesh) - without it the new light would be
        /// entirely invisible in the viewport (selectable only via the small fixed
        /// hit-test box every mesh-less node already falls back to), the task's own
        /// "must have visual proxy geometry... so they can be selected and translated/
        /// rotated using the standard Gizmos" ask.</summary>
        private void AddLightMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            var light = new LightData();
            AddNodeToScene(new Node("Light") { Light = light, Mesh = light.GenerateMesh() });
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
            bone.Mesh = bone.Bone!.GenerateMesh();
            _commandHistory.Execute(new AddNodeCommand(_currentScene, parent, bone, "Add Bone", OnSceneStructureChanged));
            bone.Bone.CaptureRestPose(bone); // needs the node already in the hierarchy
            SelectNode(bone);
        }

        /// <summary>Adds <paramref name="node"/> as a new root of <see cref="_currentScene"/>
        /// and refreshes the Outliner/viewport immediately - the shared plumbing every
        /// File > Scene > "Add ..." handler above uses, so a freshly added primitive/
        /// camera/light node is instantly visible in the Outliner (<see cref="SceneViewModel.Load"/>
        /// rebuilds the whole tree), selectable via <c>viewport.HitTest</c>
        /// (which reads straight from <see cref="_currentScene"/>/<c>Editing.MeshEditSession</c>'s
        /// own live state, nothing cached to go stale), and rendered
        /// (<c>viewport.Render</c>) all in one call - never zooming to fit,
        /// so adding a second object doesn't yank the camera away from whatever the user
        /// was already looking at.</summary>
        private void AddNodeToScene(Node node)
        {
            node.LocalPosition += Viewport.CursorPosition; // Blender: new objects at the 3D cursor
            _commandHistory.Execute(new AddNodeCommand(_currentScene, null, node, $"Add {node.Name}", OnSceneStructureChanged));
            SelectNode(node);
        }

        /// <summary>Shared refresh after a node was added/removed/moved (also on undo/redo).</summary>
        private void OnSceneStructureChanged()
        {
            var selected = _sceneViewModel.SelectedNode?.UnderlyingNode;
            _sceneViewModel.Load(_currentScene);
            _renderer.Render(_currentScene, zoomToFit: false);

            // Keep the selection only if that node is still in the scene.
            SelectNode(selected is not null && _currentScene.Traverse().Contains(selected) ? selected : null);
        }

        // ================= Generic edit undo =================

        // Baseline = snapshot of the selected node taken after the last recorded change.
        // When a view-model edit happens we wait for a short pause (so a slider drag or
        // typing becomes one step), then record baseline -> current as one command.
        private NodeState? _editBaseline;
        private Node? _editBaselineNode;
        private string? _pendingEditDescription;
        private bool _isCommittingEdit;
        private bool _isRestoringNodeState;
        private readonly DispatcherTimer _editCommitTimer = new() { Interval = TimeSpan.FromMilliseconds(600) };

        private void ResetEditBaseline()
        {
            _pendingEditDescription = null;
            _editCommitTimer.Stop();
            _editBaselineNode = _sceneViewModel.SelectedNode?.UnderlyingNode;
            _editBaseline = _editBaselineNode is null ? null : NodeState.Capture(_editBaselineNode, _editBaseline);
        }

        private void ScheduleEditCommit(string description)
        {
            if (_isRestoringNodeState || _editBaselineNode is null) return;
            _pendingEditDescription ??= description;
            _editCommitTimer.Stop();
            _editCommitTimer.Start();
        }

        /// <summary>Records whatever changed on the baseline node since the last commit.</summary>
        private void CommitPendingEdit(string? descriptionOverride = null)
        {
            _editCommitTimer.Stop();
            var description = descriptionOverride ?? _pendingEditDescription;
            _pendingEditDescription = null;
            if (description is null || _editBaselineNode is not { } node || _editBaseline is not { } before) return;

            var after = NodeState.Capture(node, before);
            if (after.ContentEquals(before)) return; // nothing actually changed
            _isCommittingEdit = true;
            try
            {
                _commandHistory.Record(new NodeStateCommand(node, before, after, description, () => OnNodeStateRestored(node)));
            }
            finally
            {
                _isCommittingEdit = false;
            }
            _editBaseline = after;
        }

        /// <summary>Runs a keyframe edit on <paramref name="node"/>'s track and records it
        /// as one undo step (skipped if nothing changed).</summary>
        private void RecordKeyframeEdit(Node node, string description, Action edit)
        {
            if (BuildKeyframeEdit(node, description, edit) is { } command) _commandHistory.Record(command);
        }

        /// <summary>Runs <paramref name="edit"/> on the node's track and returns the undo
        /// command for it (null when nothing changed), without recording it.</summary>
        private IUndoableCommand? BuildKeyframeEdit(Node node, string description, Action edit)
        {
            var before = KeyframeTrackCommand.Snapshot(_timeline, node);
            edit();
            var after = KeyframeTrackCommand.Snapshot(_timeline, node);
            if (KeyframeTrackCommand.SameContent(before, after)) return null;

            return new KeyframeTrackCommand(() => _timeline, node, before, after, description, () =>
            {
                _timeline.Apply();
                _sceneViewModel.FindViewModel(node)?.SyncFromCore();
                _renderer.Refresh();
                _selectedKeyframeTime = null;
                RefreshDopesheet();
            });
        }

        // ================= Motion paths =================

        private MotionPath.Result? _motionPath;
        private Node? _motionPathNode;
        private bool _motionPathDirty = true;

        private void MotionPathCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            InvalidateMotionPath();
        }

        private void InvalidateMotionPath()
        {
            _motionPathDirty = true;
            if (MotionPathCheckBox?.IsChecked == true) _renderer.Refresh();
        }

        private void DrawMotionPath(OverlayDrawList list)
        {
            if (MotionPathCheckBox.IsChecked != true || _timeline.IsPlaying) return;
            var node = _sceneViewModel.SelectedNode?.UnderlyingNode;
            if (node is null) return;
            if (_motionPathDirty || !ReferenceEquals(node, _motionPathNode))
            {
                _motionPathNode = node;
                _motionPathDirty = false;
                _motionPath = MotionPath.Compute(_currentScene, _timeline, node, 0, _timeline.TotalFrames);
            }
            if (_motionPath is not { Positions.Count: > 1 } path) return;
            var lineColor = new Core.Numerics.Color4(1f, 0.6f, 0.2f);
            list.AddPolyline(path.Positions, lineColor, 1.5f, layer: OverlayLayer.OnTop);
            for (var i = 0; i < path.Positions.Count; i++) list.AddPoint(path.Positions[i], lineColor, 3f, OverlayLayer.OnTop);
            foreach (var frame in path.KeyFrames)
            {
                var index = frame - path.StartFrame;
                if (index >= 0 && index < path.Positions.Count) list.AddPoint(path.Positions[index], new Core.Numerics.Color4(1f, 1f, 1f), 7f, OverlayLayer.OnTop);
            }
        }

        // ================= Auto Keying =================

        private bool _autoKey;

        private void AutoKeyButton_Changed(object sender, RoutedEventArgs e)
        {
            _autoKey = AutoKeyButton.IsChecked == true;
            Viewport.ShowMessage(_autoKey ? "Auto Keying on: transforms insert keyframes" : "Auto Keying off");
        }

        /// <summary>With Auto Keying on, keys <paramref name="nodes"/> at the current frame and
        /// returns the commands (to be grouped with the transform into one undo step).</summary>
        private List<IUndoableCommand> AutoKeyCommands(IEnumerable<Node> nodes)
        {
            var result = new List<IUndoableCommand>();
            if (!_autoKey) return result;
            var interpolation = BezierInterpolationButton.IsChecked == true ? InterpolationMode.Bezier : InterpolationMode.Linear;
            foreach (var node in nodes.Distinct())
                if (BuildKeyframeEdit(node, "Auto Key", () => _timeline.GetOrCreateTrack(node).AddKeyframeFromCurrentTransform(_timeline.CurrentTime, interpolation)) is { } command)
                    result.Add(command);
            if (result.Count > 0) RefreshDopesheet();
            return result;
        }

        private void OnNodeStateRestored(Node node)
        {
            _isRestoringNodeState = true;
            try
            {
                _renderer.Refresh();
                _gizmo.Refresh();
                _sceneViewModel.FindViewModel(node)?.ReloadFromCore();
            }
            finally
            {
                _isRestoringNodeState = false;
            }
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
        /// turned on/off (see <c>viewport.EnterActiveCameraView</c>/
        /// <c>viewport.ExitActiveCameraView</c>), also reachable via Numpad
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
            var optionsDialog = new GltfExportWindow(_objectSelection.Count > 0) { Owner = this };
            if (optionsDialog.ShowDialog() != true) return;
            var options = new GltfExportOptions
            {
                SelectedRoots = optionsDialog.SelectedOnly ? TopLevelSelection() : null,
                IncludeHidden = optionsDialog.IncludeHidden,
                IncludeAnimation = optionsDialog.IncludeAnimation,
            };

            var dialog = new SaveFileDialog
            {
                Filter = "glTF Binary (*.glb)|*.glb|glTF (*.gltf)|*.gltf",
                Title = "Export GLB/glTF",
                FileName = Path.GetFileNameWithoutExtension(_currentFilePath) is { Length: > 0 } baseName ? baseName + ".glb" : "Untitled.glb",
            };
            if (dialog.ShowDialog(this) != true) return;

            TryRun("Export GLB/glTF", () => GltfExporter.Export(_currentScene, _timeline, dialog.FileName, options));
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
                Filter = "Skybox (HDR panorama or one of 6 face images)|*.hdr;*.jpg;*.jpeg;*.png;*.bmp|HDR panorama (*.hdr)|*.hdr|Skybox face image (e.g. cube_f.jpg)|*.jpg;*.jpeg;*.png;*.bmp",
                Title = "Set Skybox - an equirectangular .hdr, or any ONE of a cube's 6 face images",
            };
            if (dialog.ShowDialog(this) != true) return;

            TryRun("Set Skybox", () =>
            {
                if (EnvironmentSettings.IsPanorama(dialog.FileName))
                {
                    // Validate now rather than silently falling back to the gradient later.
                    var probe = Core.Materials.HdrImage.LoadHdr(dialog.FileName);
                    _currentScene.Environment = new EnvironmentSettings
                    {
                        SkyboxSource = dialog.FileName,
                        SkyboxRotation = _currentScene.Environment?.SkyboxRotation ?? 0f,
                        SkyboxStrength = _currentScene.Environment?.SkyboxStrength ?? 1f,
                    };
                    _renderer.Render(_currentScene, zoomToFit: false);
                    Viewport.ShowMessage($"HDR environment {probe.Width}x{probe.Height} set (lights Material Preview, Rendered and F12).");
                    return;
                }
                if (DeriveSkyboxPrefix(dialog.FileName) is not { } prefix)
                {
                    MessageBox.Show(this,
                        "The chosen file doesn't look like one of a skybox's own 6 named faces "
                        + "(expected a name ending in _f/_b/_l/_r/_u/_d before its extension, e.g. 'cube_f.jpg').",
                        "JolieCat3D", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                _currentScene.Environment = new EnvironmentSettings { SkyboxSource = prefix, SkyboxStrength = _currentScene.Environment?.SkyboxStrength ?? 1f };
                _renderer.Render(_currentScene, zoomToFit: false);
            });
        }

        /// <summary>Add > "Skybox Rotation..." - turns an HDR panorama about +Y.</summary>
        private void SkyboxRotationMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            if (_currentScene.Environment is not { } environment || !EnvironmentSettings.IsPanorama(environment.SkyboxSource))
            {
                Viewport.ShowMessage("Rotation applies to an HDR (.hdr) panorama skybox - set one first.");
                return;
            }
            var dialog = new TextPromptWindow("Skybox Rotation", "Rotation about Y (degrees):",
                environment.SkyboxRotation.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)) { Owner = this };
            if (dialog.ShowDialog() != true || dialog.Result is null) return;
            if (!float.TryParse(dialog.Result.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var degrees) || !float.IsFinite(degrees))
            {
                Viewport.ShowMessage("Enter a number of degrees.");
                return;
            }
            environment.SkyboxRotation = degrees % 360f;
            _renderer.Render(_currentScene, zoomToFit: false);
        }

        /// <summary>Add > "Skybox Strength..." - Blender's World Strength: scales the
        /// world texture's light and background in the viewport and the final render.</summary>
        private void SkyboxStrengthMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            if (_currentScene.Environment is not { } environment)
            {
                Viewport.ShowMessage("Set a skybox or HDR panorama first.");
                return;
            }
            var dialog = new TextPromptWindow("Skybox Strength", "World light strength (1 = as in the image):",
                environment.SkyboxStrength.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)) { Owner = this };
            if (dialog.ShowDialog() != true || dialog.Result is null) return;
            if (!float.TryParse(dialog.Result.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var strength)
                || !float.IsFinite(strength) || strength < 0)
            {
                Viewport.ShowMessage("Enter a number of 0 or more.");
                return;
            }
            environment.SkyboxStrength = strength;
            _renderer.Render(_currentScene, zoomToFit: false);
        }

        /// <summary>Add > "Clear Skybox" - back to no environment at all
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
        /// viewport's own projection (see <c>CameraFraming.SetPerspective</c>/
        /// <c>CameraFraming.SetOrthographic</c>), preserving the current vantage
        /// point exactly - only the PROJECTION changes.</summary>
        private void CameraProjectionButton_Checked(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            if (sender is not RadioButton { Tag: string modeName }) return;

            _renderer.SetOrthographic(modeName == "Orthographic");
        }

        /// <summary>The Camera toolbar's Top/Bottom/Front/Back/Left/Right buttons -
        /// aligns the viewport to that preset (see <c>CameraFraming.AlignToPreset</c>),
        /// framed on the currently selected node when one exists, or the whole scene
        /// otherwise - the same "prefer the selection, fall back to the whole scene"
        /// precedent <c>CameraFraming</c>'s own <c>ZoomToFit</c> overloads already
        /// establish.</summary>
        private void ViewPresetButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            if (sender is not Button { Tag: string presetName }) return;
            if (!Enum.TryParse<ViewPreset>(presetName, out var preset)) return;

            _renderer.AlignToPreset(preset, _sceneViewModel.SelectedNode?.UnderlyingNode, frame: true);
        }

        /// <summary>The Camera toolbar's "Grid" checkbox - shows/hides the reference grid
        /// (<c>viewport.ShowGrid</c>).</summary>
        private void ShowGridCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            _renderer.ShowGrid = ShowGridCheckBox.IsChecked == true;
        }

        /// <summary>The Camera toolbar's "Shadows" checkbox - <c>viewport.ShowShadows</c>.</summary>
        private void WireframeOverlayCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            _renderer.WireframeOverlay = WireframeOverlayCheckBox.IsChecked == true;
        }

        private void ShowShadowsCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            _renderer.ShowShadows = ShowShadowsCheckBox.IsChecked == true;
        }

        /// <summary>The Camera toolbar's "Anti-Aliasing" checkbox - <c>viewport.AntiAliasingEnabled</c>.</summary>
        private void AntiAliasingCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            _renderer.AntiAliasingEnabled = AntiAliasingCheckBox.IsChecked == true;
        }

        /// <summary>The Camera toolbar's "SSAO" checkbox - <c>viewport.ShowAmbientOcclusion</c>.</summary>
        private void SsaoCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            _renderer.ShowAmbientOcclusion = SsaoCheckBox.IsChecked == true;
        }

        /// <summary>The Camera toolbar's "Bloom" checkbox - shows/hides
        /// <c>BloomOverlayRectangle</c> (see its own XAML remarks). Nothing here
        /// touches <c>viewport</c> at all: Bloom, unlike Shadows/SSAO/
        /// Anti-Aliasing, is a pure 2D compositing effect layered OVER the already-
        /// rendered viewport, not anything added to the 3D scene itself.</summary>
        private void BloomCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            _renderer.BloomEnabled = BloomCheckBox.IsChecked == true;
        }

        /// <summary>The Camera toolbar's grid size field - keeps the reference grid
        /// visual (<c>viewport.GridSize</c>) AND both gizmos' own
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

        /// <summary>Tier 1's own "_Window" menu - toggles between <see cref="WindowState.Normal"/>
        /// and <see cref="WindowState.Maximized"/>, the same "Toggle Window Fullscreen"
        /// entry Blender's own Window menu has - a small, genuinely useful entry rather
        /// than an empty placeholder menu.</summary>
        private void ToggleFullScreenMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }

        /// <summary>Tier 1's own "_Help" menu - a plain informational dialog, matching
        /// Blender's own Help menu having an "About Blender" entry.</summary>
        private void AboutMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            MessageBox.Show(this, "JolieCat3D\nA Blender-inspired 3D modeling/animation tool with a DirectX 11 viewport.",
                "About JolieCat3D", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            SystemCommands.MinimizeWindow(this);
        }

        /// <summary>Tier 1's own title bar (<c>WindowStyle="None"</c> + <c>WindowChrome</c>
        /// with <c>CaptionHeight="30"</c> - see MainWindow.xaml's own remarks) already
        /// drives dragging and double-click-to-maximize/restore natively for the rest of
        /// that row, entirely at the OS/non-client level - this Click handler is only
        /// ever reached from an actual click on this button itself.
        /// <see cref="MainWindow_StateChanged"/> keeps the button's own glyph in sync
        /// however WindowState actually changed - this click, a title-bar double-click,
        /// or an OS-level Aero Snap (Win+Up/drag-to-top).</summary>
        private void MaximizeRestoreButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            if (WindowState == WindowState.Maximized)
                SystemCommands.RestoreWindow(this);
            else
                SystemCommands.MaximizeWindow(this);
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            SystemCommands.CloseWindow(this);
        }

        /// <summary>Keeps the custom title bar's own Maximize/Restore glyph (Segoe MDL2
        /// Assets - see MainWindow.xaml's own remarks) in sync with WindowState however it
        /// actually changed, not just <see cref="MaximizeRestoreButton_Click"/>'s own
        /// click; also owns the actual fix for the classic WindowChrome +
        /// WindowStyle="None" maximize-over-the-taskbar bug (the Timeline/Status Bar
        /// rendering underneath it instead of above). Two earlier attempts at this same
        /// fix lived entirely in MainWindow.xaml - a Window.Padding Style Trigger, then a
        /// Grid.Margin Binding reading that Padding back - and neither actually worked,
        /// because nothing in this app's own layout ever gave a maximized WindowStyle="None"
        /// window a reason to stay within the monitor's own WORK AREA (screen bounds minus
        /// the taskbar) rather than the full screen bounds. Clamping MaxHeight/MaxWidth to
        /// SystemParameters.WorkArea here is what actually, physically prevents the window
        /// from ever occupying the space the taskbar sits in, regardless of any Window
        /// template/WindowChrome ambiguity - restored back to PositiveInfinity when leaving
        /// Maximized so the window is freely resizable by hand again afterward. Note:
        /// SystemParameters.WorkArea reflects the PRIMARY monitor only - a real
        /// multi-monitor fix would need the WorkingArea of whichever screen this window is
        /// actually on (via WindowInteropHelper), not attempted here.
        ///
        /// Deliberately does NOT also set BorderThickness (an earlier version of this
        /// fix did, meaning to compensate for the standard hidden resize border a
        /// maximized window normally gets inset by) - a Border ALWAYS reserves its own
        /// BorderThickness as inset layout space for its content regardless of whether
        /// BorderBrush actually paints anything there, so setting BorderThickness="7"
        /// shrank the root Grid's own visible content 7px further inward on every side
        /// than the already-correctly-sized (MaxHeight/MaxWidth-clamped) window frame
        /// around it, producing a dead, unpainted collar - most visible along the
        /// bottom/right, since Tier 1 (this window's own top-left-anchored title bar)
        /// left no equivalent gap to notice on top/left. MaxHeight/MaxWidth alone is a
        /// complete fix; nothing here needs a compensating border inset at all.</summary>
        private void MainWindow_StateChanged(object? sender, EventArgs e)
        {
            var isMaximized = WindowState == WindowState.Maximized;
            MaximizeRestoreGlyph.Text = isMaximized ? "" : "";
            MaximizeRestoreButton.ToolTip = isMaximized ? "Restore" : "Maximize";

            MaxHeight = isMaximized ? SystemParameters.WorkArea.Height : double.PositiveInfinity;
            MaxWidth = isMaximized ? SystemParameters.WorkArea.Width : double.PositiveInfinity;
        }

        /// <summary>Tier 1's own "Workspaces" tab row - a flat RadioButton strip that
        /// (for now - see <see cref="MainWindow.xaml"/>'s own remarks on why this is a
        /// real, working shortcut rather than a full separate-UI-layout system yet) just
        /// switches <see cref="ModeComboBox"/> to whichever Interaction Mode that
        /// workspace corresponds to, reusing every one of <see cref="ModeComboBox_SelectionChanged"/>'s
        /// own existing validation (a workspace whose mode needs a mesh-bearing node
        /// selected bounces back to Object Mode exactly like picking that same mode from
        /// <see cref="ModeComboBox"/> directly already does).</summary>
        private void WorkspaceButton_Checked(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            if (sender is not RadioButton { Tag: string modeName }) return;
            SetEditorMode(modeName);
        }

        /// <summary>No dirty/unsaved-changes tracking exists yet (every scene edit -
        /// gizmo drag, Properties field, Import - would need to flip a flag this project
        /// doesn't have), so this always just asks - a harmless extra confirmation on an
        /// already-saved scene, and a real save against silently discarding one that
        /// isn't. Skipped entirely for a brand-new, still-Untitled scene with no file of
        /// its own (nothing meaningful to lose).</summary>
        private bool ConfirmDiscardIfNeeded(string action)
        {
            if (!HasRealUnsavedChanges()) return true;

            var result = MessageBox.Show(this,
                $"Save changes to {_documentName} before {action}?",
                "JolieCat3D", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning, MessageBoxResult.Yes);
            return result switch
            {
                MessageBoxResult.Yes => SaveScene(),
                MessageBoxResult.No => true,
                _ => false,
            };
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
        /// alone can't be relied on for that (see <c>GizmoHitTester</c>'s own
        /// remarks on why a mesh occluding the manipulator's hit-test geometry can stop the
        /// click ever reaching it at all).
        /// </summary>
        private void Viewport_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!_isInitialized) return;

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

            if (IsTexturePaintMode)
            {
                _isPaintingTexture = true;
                HandleTexturePaintClick(position);
                return;
            }

            if (TryBeginGizmoDrag(position, e))
            {
                e.Handled = true;
                return;
            }

            if (IsEditMode)
            {
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt) && TrySelectEdgeLoop(position)) return;
                if (!HandleComponentClick(position, additive: Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)))
                    BeginBoxSelect(position);
                return;
            }

            var hitNode = _renderer.HitTest(position);
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                ToggleInSelection(hitNode);
                return;
            }
            if (hitNode is null)
            {
                BeginBoxSelect(position); // click deselects, drag box-selects
                return;
            }
            SelectNode(hitNode);
        }

        /// <summary>Starts a drag when the click lands on a gizmo handle.</summary>
        private bool TryBeginGizmoDrag(Point position, MouseButtonEventArgs sourceArgs)
        {
            var started = IsEditMode ? _componentGizmo.TryBeginDrag(position) : _gizmo.TryBeginDrag(position);
            if (started) Viewport.CaptureMouse();
            return started;
        }

        private bool IsGizmoDragging => _gizmo.IsDragging || _componentGizmo.IsDragging;

        private bool IsModalTransform => _gizmo.IsModal || _componentGizmo.IsModal;

        /// <summary>During G/R/S: left click confirms, right click cancels (Blender behaviour).
        /// Runs in the tunnelling phase so the viewport doesn't start orbiting on that right click.</summary>
        private void OnWindowPreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (!_isInitialized) return;

            if (_modalMeshOp is not null)
            {
                if (e.ChangedButton == MouseButton.Left) ConfirmModalMeshOperation();
                else if (e.ChangedButton == MouseButton.Right) CancelModalMeshOperation();
                e.Handled = true;
                return;
            }

            if (_isKnifeActive && Viewport.IsMouseOver)
            {
                if (e.ChangedButton == MouseButton.Left) KnifeClick(e.GetPosition(Viewport));
                else if (e.ChangedButton == MouseButton.Right) EndKnife();
                e.Handled = true;
                return;
            }

            if (!IsModalTransform) return;
            if (e.ChangedButton == MouseButton.Left) EndGizmoDrag();
            else if (e.ChangedButton == MouseButton.Right) CancelGizmoDrag();
            e.Handled = true;
        }

        private void CancelGizmoDrag()
        {
            if (_gizmo.IsDragging) _gizmo.CancelDrag();
            if (_componentGizmo.IsDragging) _componentGizmo.CancelDrag();
            if (Viewport.IsMouseCaptured) Viewport.ReleaseMouseCapture();
            _renderer.Refresh();
        }

        /// <summary>Blender keyboard shortcuts for the viewport. Returns true if handled.</summary>
        private bool HandleBlenderKeys(KeyEventArgs e)
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            var mods = Keyboard.Modifiers;
            var mouse = Mouse.GetPosition(Viewport);

            if (_modalMeshOp is not null)
            {
                if (key == Key.Escape) CancelModalMeshOperation();
                else if (key is Key.Enter or Key.Space) ConfirmModalMeshOperation();
                return true;
            }

            if (_isKnifeActive && key == Key.Escape)
            {
                EndKnife();
                return true;
            }

            if (IsModalTransform)
            {
                switch (key)
                {
                    case Key.X: ConstrainModal(0, mouse); return true;
                    case Key.Y: ConstrainModal(1, mouse); return true;
                    case Key.Z: ConstrainModal(2, mouse); return true;
                    case Key.Enter: case Key.Space: EndGizmoDrag(); return true;
                }
                return false;
            }

            if (IsEditMode && (Viewport.IsMouseOver || Viewport.IsKeyboardFocusWithin))
            {
                switch (key)
                {
                    case Key.A when mods == ModifierKeys.None: SelectAllComponents(true); return true;
                    case Key.A when mods == ModifierKeys.Alt: SelectAllComponents(false); return true;
                    case Key.I when mods == ModifierKeys.Control: InvertComponentSelection(); return true;
                    case Key.L when mods is ModifierKeys.None or ModifierKeys.Control: SelectLinkedComponents(); return true;
                    case Key.E when mods == ModifierKeys.None: ExtrudeRegion(); return true;
                    case Key.I when mods == ModifierKeys.None: BeginModalInset(); return true;
                    case Key.K when mods == ModifierKeys.None: BeginKnife(); return true;
                    case Key.R when mods == ModifierKeys.Control: BeginModalLoopCut(); return true;
                    case Key.B when mods == ModifierKeys.Control: BeginModalBevel(); return true;
                    case Key.E when mods == ModifierKeys.Alt: BeginModalExtrudeAlongNormals(); return true;
                    case Key.M when mods == ModifierKeys.None: MergeAtCenter(); return true;
                    case Key.F when mods == ModifierKeys.None: FillFace(); return true;
                    case Key.T when mods == ModifierKeys.Control: TriangulateFaces(); return true;
                    case Key.P when mods == ModifierKeys.None: SeparateSelection(); return true;
                    case Key.J when mods == ModifierKeys.Alt: TrisToQuads(); return true;
                    case Key.X when mods == ModifierKeys.Control: DissolveFaces(); return true;
                    case Key.X when mods == ModifierKeys.None: DeleteSelected(); return true;
                }
            }

            if (!IsEditMode && !IsPaintOrSculptMode && (Viewport.IsMouseOver || Viewport.IsKeyboardFocusWithin))
            {
                switch (key)
                {
                    case Key.A when mods == ModifierKeys.None: SelectAllObjects(true); return true;
                    case Key.A when mods == ModifierKeys.Alt: SelectAllObjects(false); return true;
                    case Key.J when mods == ModifierKeys.Control: JoinSelectedObjects(); return true;
                    case Key.H when mods == ModifierKeys.None: HideObjects(selected: true); return true;
                    case Key.H when mods == ModifierKeys.Shift: HideObjects(selected: false); return true;
                    case Key.H when mods == ModifierKeys.Alt: RevealHidden(); return true;
                    case Key.D when mods == ModifierKeys.Shift:
                        DuplicateSelectedObjects();
                        if (_gizmo.BeginModal(GizmoMode.Translate, mouse)) Viewport.Focus();
                        return true;
                    case Key.D when mods == ModifierKeys.Alt:
                        DuplicateLinkedObjects();
                        if (_gizmo.BeginModal(GizmoMode.Translate, mouse)) Viewport.Focus();
                        return true;
                }
            }

            if (key == Key.Escape && _animationRenderCancel is { } renderCancel)
            {
                renderCancel.Cancel();
                return true;
            }

            if (key == Key.F12 && mods == ModifierKeys.None)
            {
                RenderImage();
                return true;
            }

            if (key == Key.F6 && mods == ModifierKeys.Shift)
            {
                OpenGraphEditor();
                return true;
            }

            if (key == Key.F3 && mods == ModifierKeys.Shift)
            {
                OpenShaderEditor();
                return true;
            }

            if (!IsEditMode && !IsPaintOrSculptMode && Viewport.IsMouseOver)
            {
                if (key == Key.I && mods == ModifierKeys.None) { InsertKeyframesForSelection(); return true; }
                if (key == Key.I && mods == ModifierKeys.Alt) { DeleteKeyframesForSelection(); return true; }
            }

            if (key == Key.C && mods == ModifierKeys.Shift && Viewport.IsMouseOver)
            {
                ResetCursorAndFrame();
                return true;
            }

            if (!IsEditMode && key == Key.A && mods == ModifierKeys.Control)
            {
                ApplyAllTransforms();
                return true;
            }

            if (!IsEditMode && key == Key.X && mods == ModifierKeys.None && Viewport.IsMouseOver)
            {
                DeleteSelected();
                return true;
            }

            if (IsEditMode && key == Key.E && mods == ModifierKeys.Shift && Viewport.IsMouseOver)
            {
                ToggleCrease();
                return true;
            }

            if (key == Key.S && mods == ModifierKeys.Shift && Viewport.IsMouseOver && !IsPaintOrSculptMode)
            {
                ShowSnapMenu();
                return true;
            }

            if (mods != ModifierKeys.None) return false;

            switch (key)
            {
                case Key.Tab:
                    SelectModeByTag(CurrentEditorMode == "Edit" ? "Object" : "Edit");
                    return true;
                case Key.D1 when IsEditMode: VertexModeButton.IsChecked = true; return true;
                case Key.D2 when IsEditMode: EdgeModeButton.IsChecked = true; return true;
                case Key.D3 when IsEditMode: FaceModeButton.IsChecked = true; return true;
                case Key.G or Key.R or Key.S when Viewport.IsMouseOver && !IsPaintOrSculptMode:
                    if (IsEditMode)
                    {
                        if (key == Key.G && _componentGizmo.BeginModal(mouse)) Viewport.Focus();
                        return true;
                    }
                    var mode = key == Key.G ? GizmoMode.Translate : key == Key.R ? GizmoMode.Rotate : GizmoMode.Scale;
                    if (_gizmo.BeginModal(mode, mouse)) Viewport.Focus();
                    return true;
            }
            return false;
        }

        private void ConstrainModal(int axis, Point mouse)
        {
            if (_gizmo.IsModal) _gizmo.ConstrainModal(axis, mouse);
            else if (_componentGizmo.IsModal) _componentGizmo.ConstrainModal(axis, mouse);
            _renderer.Refresh();
        }

        private void SelectModeByTag(string tag)
        {
            foreach (var item in ModeComboBox.Items)
            {
                if (item is ComboBoxItem { Tag: string t } comboItem && t == tag)
                {
                    ModeComboBox.SelectedItem = comboItem;
                    return;
                }
            }
        }

        private bool IsPaintOrSculptMode => IsWeightPaintMode || IsVertexPaintMode || IsSculptMode || IsTexturePaintMode;

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
            var command = new ReparentNodeCommand(_currentScene, node, newParent, "Parent Object", OnSceneStructureChanged);
            if (!command.TryExecute()) return;
            _commandHistory.Record(command);
            SelectNode(node);
        }

        /// <summary>True once <see cref="CurrentEditorMode"/> is "Edit" (rather than
        /// "Object"/"WeightPaint"/...) - what every Edit-Mode-vs-everything-else branch
        /// in this window reads.</summary>
        private bool IsEditMode => CurrentEditorMode == "Edit";

        /// <summary>True once <see cref="CurrentEditorMode"/> is "WeightPaint" - what
        /// <see cref="Viewport_MouseLeftButtonDown"/>/<see cref="Viewport_MouseMove"/>
        /// read to route a viewport click/drag to the brush instead of selection/Edit
        /// Mode.</summary>
        private bool IsWeightPaintMode => CurrentEditorMode == "WeightPaint";

        /// <summary>True once <see cref="CurrentEditorMode"/> is "VertexPaint" - the
        /// same role <see cref="IsWeightPaintMode"/> plays for Weight Paint mode.</summary>
        private bool IsVertexPaintMode => CurrentEditorMode == "VertexPaint";

        /// <summary>True once <see cref="CurrentEditorMode"/> is "Sculpt" - the same
        /// role <see cref="IsWeightPaintMode"/> plays for Weight Paint mode.</summary>
        private bool IsSculptMode => CurrentEditorMode == "Sculpt";

        /// <summary>True once <see cref="CurrentEditorMode"/> is "TexturePaint" - the
        /// same role <see cref="IsWeightPaintMode"/> plays for Weight Paint mode.</summary>
        private bool IsTexturePaintMode => CurrentEditorMode == "TexturePaint";

        /// <summary>Sets <see cref="ModeComboBox"/>'s own selection to whichever
        /// <see cref="ComboBoxItem"/> carries <paramref name="modeName"/> as its own Tag -
        /// the replacement for the old "just check a different RadioButton" bounce-back
        /// (<c>ObjectModeButton.IsChecked = true</c>) every mode-validation failure below
        /// uses, now that Interaction Mode is a single <see cref="ModeComboBox"/> instead
        /// of a RadioButton group. Setting <see cref="Selector.SelectedItem"/> raises
        /// <see cref="ModeComboBox_SelectionChanged"/> synchronously, the exact same
        /// reentrant-bounce-back shape the old RadioButton.IsChecked setter already
        /// had.</summary>
        private void SetEditorMode(string modeName)
        {
            foreach (var item in ModeComboBox.Items.OfType<ComboBoxItem>())
            {
                if (item.Tag as string != modeName) continue;
                ModeComboBox.SelectedItem = item;
                return;
            }
        }

        private void SelectNode(Node? node)
        {
            if (!_keepGroupOnSelect)
            {
                _objectSelection.Clear();
                if (node is not null) _objectSelection.Add(node);
            }
            _renderer.SetSelection(_objectSelection, node);
            _graphEditor?.Redraw();
            _uvEditor?.Redraw();
            foreach (var sceneNode in _currentScene.Traverse())
                if (_sceneViewModel.FindViewModel(sceneNode) is { } vm) vm.IsInSelection = _objectSelection.Contains(sceneNode);
            _sceneViewModel.SelectedNode = _sceneViewModel.FindViewModel(node);
            _selectedKeyframeTime = null;
            RefreshDopesheet();

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
                    SetEditorMode("Object");
                }
                return;
            }

            if (IsVertexPaintMode)
            {
                if (node?.Mesh is not null) _renderer.EnterVertexPaintMode(node);
                else SetEditorMode("Object");
                return;
            }

            if (IsSculptMode)
            {
                if (node?.Mesh is not null) _renderer.EnterSculptMode(node);
                else SetEditorMode("Object");
                return;
            }

            if (IsTexturePaintMode)
            {
                if (node?.Mesh is not null) _renderer.EnterTexturePaintMode(node);
                else SetEditorMode("Object");
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
                SetEditorMode("Object");
            }
        }

        private void GizmoModeButton_Checked(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            if (sender is not RadioButton { Tag: string modeName }) return;
            if (Enum.TryParse<GizmoMode>(modeName, out var mode)) _gizmo.Mode = mode;
        }

        /// <summary>The Tier 2 header's own Transform Orientation combo (Global/Local) -
        /// sets BOTH gizmos' own <see cref="TransformGizmo.Space"/>/<see cref="ComponentGizmo.Space"/>
        /// together (only one of the two is ever attached/visible at a time - see
        /// <see cref="SelectNode"/>/<see cref="ModeComboBox_SelectionChanged"/> - but keeping
        /// both in sync means the choice carries over correctly whichever one the user
        /// switches to next, rather than each silently reverting to Global).</summary>
        private void TransformSpaceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isInitialized) return;
            if (TransformSpaceComboBox.SelectedItem is not ComboBoxItem { Tag: string spaceName }) return;
            if (!Enum.TryParse<TransformSpace>(spaceName, out var space)) return;

            _gizmo.Space = space;
            _componentGizmo.Space = space;
        }

        /// <summary>The viewport shading toolbar - see <see cref="ShadingMode"/>'s own
        /// remarks for what each of the 4 modes actually changes. Setting
        /// <c>viewport.ShadingMode</c> re-renders immediately on its own,
        /// the same "setting the mode applies it too" shape <see cref="GizmoModeButton_Checked"/>
        /// already uses for <see cref="TransformGizmo.Mode"/>.</summary>
        private void ShadingModeButton_Checked(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            if (sender is not RadioButton { Tag: string modeName }) return;
            if (Enum.TryParse<ShadingMode>(modeName, out var mode)) _renderer.ShadingMode = mode;
        }

        /// <summary>The Tier 2 Viewport Header's own Interaction Mode <see cref="ModeComboBox"/> -
        /// switches between Object Mode (the existing whole-node Translate/Rotate/Scale
        /// gizmo) and Edit Mode (per-component selection and <see cref="ComponentGizmo"/>),
        /// among others, requiring a mesh-bearing node (and, for Weight Paint, one
        /// already bound to an Armature) already selected before a mode that needs one
        /// can actually be entered (bouncing back to Object Mode - <see cref="SetEditorMode"/> -
        /// with a message otherwise). <see cref="CurrentEditorMode"/> is updated FIRST,
        /// before anything else here runs, so every <c>DataTrigger</c> bound to it (the
        /// Left Vertical Toolbar's own per-mode sections - see that property's own
        /// remarks) and every <c>IsXxxMode</c> getter this method's own logic below reads
        /// are already showing the NEW mode's own state by the time any of it
        /// executes.</summary>
        private void ModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isInitialized) return;
            if (ModeComboBox.SelectedItem is not ComboBoxItem { Tag: string modeName }) return;
            CurrentEditorMode = modeName;

            var enteringEditMode = modeName == "Edit";
            var enteringWeightPaintMode = modeName == "WeightPaint";
            var enteringVertexPaintMode = modeName == "VertexPaint";
            var enteringSculptMode = modeName == "Sculpt";
            var enteringTexturePaintMode = modeName == "TexturePaint";

            VertexModeButton.IsEnabled = enteringEditMode;
            EdgeModeButton.IsEnabled = enteringEditMode;
            FaceModeButton.IsEnabled = enteringEditMode;
            ProportionalEditingCheckBox.IsEnabled = enteringEditMode;
            MirrorXCheckBox.IsEnabled = enteringEditMode;
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
            AffectOnlyOriginCheckBox.IsEnabled = !enteringEditMode && !enteringWeightPaintMode && !enteringVertexPaintMode && !enteringSculptMode && !enteringTexturePaintMode;

            // The 4 paint-mode toolbars' own Visibility is no longer set here at all -
            // each one's Left Vertical Toolbar section now carries a Style with its own
            // DataTrigger against CurrentEditorMode (set above), which this method would
            // otherwise silently override: a LOCAL value (an imperative
            // "toolbar.Visibility = ..." assignment, exactly like this used to be) beats
            // a Style Trigger in WPF's own property-value precedence, so leaving an
            // assignment like that in here would make the corresponding DataTrigger
            // dead code.

            // Leaving Weight/Vertex/Texture Paint or Sculpt mode (for any of the other
            // modes) always detaches its own session first, exactly like leaving Edit
            // Mode below - never left silently active (and still eating viewport clicks)
            // once its own toolbar is hidden.
            if (!enteringWeightPaintMode) _renderer.ExitWeightPaintMode();
            if (!enteringVertexPaintMode) _renderer.ExitVertexPaintMode();
            if (!enteringSculptMode) _renderer.ExitSculptMode();
            if (!enteringTexturePaintMode) _renderer.ExitTexturePaintMode();

            if (enteringEditMode)
            {
                if (_sceneViewModel.SelectedNode?.UnderlyingNode is not { Mesh: not null } node)
                {
                    MessageBox.Show(this, "Select an object with a mesh before entering Edit Mode.",
                        "JolieCat3D", MessageBoxButton.OK, MessageBoxImage.Information);
                    SetEditorMode("Object");
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
                    SetEditorMode("Object");
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
                    SetEditorMode("Object");
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
                    SetEditorMode("Object");
                    return;
                }

                _gizmo.Attach(null);
                _componentGizmo.Attach(null);
                _renderer.EnterSculptMode(node);
            }
            else if (enteringTexturePaintMode)
            {
                if (_sceneViewModel.SelectedNode?.UnderlyingNode is not { Mesh: not null } node)
                {
                    MessageBox.Show(this, "Select an object with a mesh before entering Texture Paint mode.",
                        "JolieCat3D", MessageBoxButton.OK, MessageBoxImage.Information);
                    SetEditorMode("Object");
                    return;
                }

                _gizmo.Attach(null);
                _componentGizmo.Attach(null);
                _renderer.EnterTexturePaintMode(node);
            }
            else
            {
                _componentGizmo.Attach(null);
                _renderer.ExitEditMode();
                _gizmo.Attach(_sceneViewModel.SelectedNode?.UnderlyingNode);
            }
        }

        /// <summary>Rebuilds <see cref="ActiveBoneComboBox"/>'s own item list from
        /// whatever <c>viewport.WeightPaintSession</c>'s current
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

        /// <summary>Weight Paint toolbar: Mirror / Smooth / Normalize (undoable).</summary>
        private void WeightToolButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized || sender is not FrameworkElement { Tag: string tool }) return;
            if (_renderer.WeightPaintSession.Target is not { Mesh: { } mesh, SkinBinding: { } binding } node)
            {
                Viewport.ShowMessage("Weight tools need a skinned mesh in Weight Paint mode.");
                return;
            }
            var report = "";
            var command = MeshEditCommandFactory.Capture(node, tool + " Weights", () =>
            {
                switch (tool)
                {
                    case "Mirror":
                        var mirrored = Core.Skinning.WeightTools.MirrorX(mesh, binding.Bones);
                        report = $"Mirrored weights onto {mirrored} vertices";
                        return mirrored > 0;
                    case "Smooth":
                        Core.Skinning.WeightTools.Smooth(mesh, iterations: 2);
                        report = "Smoothed weights";
                        return true;
                    default:
                        var normalized = Core.Skinning.WeightTools.Normalize(mesh);
                        report = $"Normalized {normalized} vertices";
                        return normalized > 0;
                }
            }, onChanged: () => _renderer.Refresh());
            if (command is null)
            {
                Viewport.ShowMessage(tool == "Mirror" ? "Nothing mirrored: no vertices at matching -X positions." : "Weights already normalized.");
                return;
            }
            _commandHistory.Record(command);
            _renderer.Refresh();
            Viewport.ShowMessage(report);
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

        /// <summary>Texture Paint mode's own target-color picker - the same plain
        /// "#RRGGBB" hex field convention <see cref="VertexPaintColorTextBox_TextChanged"/>
        /// already uses.</summary>
        private void TexturePaintColorTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isInitialized) return;
            if (sender is not TextBox { Text: var text }) return;
            if (System.Windows.Media.ColorConverter.ConvertFromString(text) is not System.Windows.Media.Color parsed) return;

            _renderer.TexturePaintSession.Color = new Color4(parsed.ScR, parsed.ScG, parsed.ScB, parsed.ScA);
        }

        private void TexturePaintModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isInitialized || sender is not ComboBox box) return;
            _renderer.TexturePaintSession.Mode = (Core.Materials.TexturePaintMode)Math.Max(0, box.SelectedIndex);
        }

        private void TextureBrushRadiusSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_isInitialized) return;
            _renderer.TexturePaintSession.BrushRadius = (float)e.NewValue;
        }

        private void TextureBrushStrengthSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_isInitialized) return;
            _renderer.TexturePaintSession.Strength = (float)e.NewValue;
        }

        /// <summary>Texture Paint mode's own mouse-down: paints one brush tick right
        /// where the click landed, then starts <see cref="_isPaintingTexture"/> so
        /// <see cref="Viewport_MouseMove"/> keeps painting for the rest of the drag - the
        /// same shape <see cref="HandleVertexPaintClick"/>/<see cref="_isPaintingVertexColors"/>
        /// already have. A miss (the ray doesn't land on <see cref="Target"/>'s own
        /// geometry at all - <see cref="TexturePaintSession.RaycastUVHitPoint"/> returns
        /// null) is simply ignored, same as every other brush's own miss.</summary>
        private bool _isPaintingTexture;

        private void HandleTexturePaintClick(Point position)
        {
            if (_renderer.TexturePaintSession.RaycastUVHitPoint(Viewport, position) is not { } uv) return;
            _renderer.TexturePaintSession.PaintAt(uv);
            _renderer.Refresh();
        }

        private void Viewport_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isInitialized) return;

            if (_modalMeshOp is not null)
            {
                UpdateModalMeshOperation(e.GetPosition(Viewport));
                return;
            }

            if (_isKnifeActive)
            {
                if (_knifeStart is { } knifeStart) Viewport.SetGuideLine(knifeStart, e.GetPosition(Viewport));
                return;
            }

            if (IsGizmoDragging)
            {
                if (!IsModalTransform && e.LeftButton != MouseButtonState.Pressed) { EndGizmoDrag(); return; }
                if (_gizmo.IsDragging) _gizmo.UpdateDrag(e.GetPosition(Viewport));
                else _componentGizmo.UpdateDrag(e.GetPosition(Viewport));
                return;
            }

            if (_boxSelectStart is not null)
            {
                if (e.LeftButton == MouseButtonState.Pressed) UpdateBoxSelect(e.GetPosition(Viewport));
                else EndBoxSelect(e.GetPosition(Viewport));
                return;
            }

            if (IsEditMode) _componentGizmo.UpdateHover(e.GetPosition(Viewport));
            else _gizmo.UpdateHover(e.GetPosition(Viewport));

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
                return;
            }

            if (_isPaintingTexture)
            {
                if (e.LeftButton == MouseButtonState.Pressed) HandleTexturePaintClick(e.GetPosition(Viewport));
                else _isPaintingTexture = false;
            }
        }

        private void EndGizmoDrag()
        {
            if (_gizmo.IsDragging) _gizmo.EndDrag();
            if (_componentGizmo.IsDragging) _componentGizmo.EndDrag();
            if (Viewport.IsMouseCaptured) Viewport.ReleaseMouseCapture();
            _renderer.Refresh();
        }

        private void Viewport_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (IsGizmoDragging)
            {
                EndGizmoDrag();
                e.Handled = true;
                return;
            }

            if (_boxSelectStart is not null)
            {
                EndBoxSelect(e.GetPosition(Viewport));
                e.Handled = true;
                return;
            }

            // One undo step per brush stroke.
            string? strokeName =
                _isPaintingWeights ? "Weight Paint" :
                _isPaintingVertexColors ? "Vertex Paint" :
                _isPaintingTexture ? "Texture Paint" :
                _isSculpting ? "Sculpt" : null;

            _isPaintingWeights = false;
            _isPaintingVertexColors = false;
            _isPaintingTexture = false;
            if (_isSculpting)
            {
                _isSculpting = false;
                _renderer.SculptSession.EndStroke();
            }

            if (strokeName is not null) CommitPendingEdit(strokeName);
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
        private void MirrorXCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            _renderer.EditSession.MirrorX = MirrorXCheckBox.IsChecked == true;
        }

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
        /// <summary>Selects the component under <paramref name="position"/>. Returns false when
        /// nothing was hit, so the caller can start a box selection instead.</summary>
        private bool HandleComponentClick(Point position, bool additive)
        {
            var session = _renderer.EditSession;
            if (session.Target is not { } node) return false;
            var hit = true;

            switch (session.ComponentMode)
            {
                case ComponentType.Vertex:
                    if (ComponentHitTester.HitTestVertex(Viewport, node, position) is { } vertexIndex)
                        session.SelectVertex(vertexIndex, additive);
                    else hit = false;
                    break;

                case ComponentType.Edge:
                    if (ComponentHitTester.HitTestEdge(Viewport, node, position) is { } edge)
                        session.SelectEdge(edge.A, edge.B, additive);
                    else hit = false;
                    break;

                case ComponentType.Face:
                    if (ComponentHitTester.HitTestFace(Viewport, node, position) is { } face)
                        session.SelectFace(face.Indices, additive);
                    else hit = false;
                    break;
            }

            _renderer.RefreshComponentOverlay();
            _componentGizmo.Attach(session);
            return hit;
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
        // ================= Actions (animation clips) =================

        private void RefreshActionList()
        {
            var actions = _timeline.Actions.ToList();
            if (!ActionComboBox.Items.Cast<object>().SequenceEqual(actions))
            {
                ActionComboBox.Items.Clear();
                foreach (var a in actions) ActionComboBox.Items.Add(a);
            }
            else
            {
                ActionComboBox.Items.Refresh(); // names may have changed
            }
            ActionComboBox.SelectedItem = _timeline.ActiveAction;
        }

        /// <summary>After the action list or active action changed: re-pose the scene from the
        /// now-active clip and refresh everything that shows keys.</summary>
        private void OnActionsChanged()
        {
            _timeline.Apply();
            _sceneViewModel.SelectedNode?.SyncFromCore();
            _renderer.Refresh();
            _selectedKeyframeTime = null;
            RefreshAnimationUI();
            MarkChanged();
        }

        private void ActionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isInitialized || _isUpdatingAnimationUI) return;
            if (ActionComboBox.SelectedItem is not AnimationAction action || ReferenceEquals(action, _timeline.ActiveAction)) return;
            _timeline.ActiveAction = action;
            OnActionsChanged();
            Viewport.ShowMessage($"Active action: {action.Name}");
        }

        private void AddActionCore(bool copy)
        {
            var action = _timeline.AddAction(copy ? _timeline.ActiveAction.Name : "Action", copy);
            _timeline.RemoveAction(action); // the command re-adds it, keeping undo/redo symmetric
            _commandHistory.Execute(new ActionListCommand(() => _timeline, action, isAdd: true, OnActionsChanged));
            Viewport.ShowMessage($"Added action {action.Name}");
        }

        private void NewActionButton_Click(object sender, RoutedEventArgs e) => AddActionCore(copy: false);

        private void DuplicateActionButton_Click(object sender, RoutedEventArgs e) => AddActionCore(copy: true);

        private void RenameActionButton_Click(object sender, RoutedEventArgs e)
        {
            var action = _timeline.ActiveAction;
            var dialog = new TextPromptWindow("Rename Action", "Action name:", action.Name) { Owner = this };
            if (dialog.ShowDialog() != true || dialog.Result is null || dialog.Result == action.Name) return;
            var name = _timeline.UniqueActionName(dialog.Result);
            _commandHistory.Execute(new RenameActionCommand(action, action.Name, name, OnActionsChanged));
        }

        private void DeleteActionButton_Click(object sender, RoutedEventArgs e)
        {
            if (_timeline.Actions.Count <= 1)
            {
                Viewport.ShowMessage("The last action can't be deleted.");
                return;
            }
            var name = _timeline.ActiveAction.Name;
            _commandHistory.Execute(new ActionListCommand(() => _timeline, _timeline.ActiveAction, isAdd: false, OnActionsChanged));
            Viewport.ShowMessage($"Deleted action {name}");
        }

        private void RefreshAnimationUI()
        {
            _graphEditor?.Redraw(); // playhead follows playback/scrubbing
            _nlaEditor?.Redraw();
            _isUpdatingAnimationUI = true;
            try
            {
                FrameScrubber.Maximum = _timeline.TotalFrames;
                FrameScrubber.Value = _timeline.CurrentFrame;
                FrameLabel.Text = $"{_timeline.CurrentFrame:0} / {_timeline.TotalFrames}";
                PlayPauseButton.Content = _timeline.IsPlaying ? "Pause" : "Play";
                RefreshActionList();
                RefreshDopesheet();
            }
            finally
            {
                _isUpdatingAnimationUI = false;
            }
        }

        /// <summary>
        /// The Dopesheet: redraws <see cref="DopesheetCanvas"/> from scratch (see
        /// <c>Rendering.Lighting.SceneLightingFactory.CreateSceneLights</c>'s own "rebuild,
        /// don't try to patch in place" precedent - the same reasoning applies here, just
        /// for a handful of small marker shapes instead of WPF lights) with one small
        /// diamond marker per keyframe on the currently selected node's own
        /// <see cref="AnimationTrack"/> - null/no track at all (nothing selected, or the
        /// selected node has never been keyframed) simply leaves the canvas empty. Each
        /// marker's own X position is <c>(keyframe time in frames / TotalFrames) *
        /// DopesheetCanvas.ActualWidth</c> - the same frame-to-pixel mapping
        /// <see cref="FrameScrubber"/>'s own Minimum=0/Maximum=TotalFrames range already
        /// implies for its own thumb, so a marker always lines up with the SAME frame
        /// <see cref="FrameScrubber"/> would show that keyframe's own time at. Called
        /// after anything that could change WHICH keyframes exist, where they sit, or
        /// which one is selected - a node selection change (<see cref="SelectNode"/>), and
        /// every <see cref="RefreshAnimationUI"/> call (a tick, Play/Pause/Stop, a scrub,
        /// an FPS edit - FPS specifically changes every keyframe's own FRAME position even
        /// though its own TIME in seconds didn't move at all).
        /// </summary>
        private void RefreshDopesheet()
        {
            // Never tear down markers while one of them is actively being dragged - see
            // _isDraggingKeyframeMarker's own remarks. The drag's own MouseLeftButtonUp
            // handler calls this again itself the instant the drag actually ends, so
            // skipping a redraw here is never a "stuck stale Dopesheet", only a
            // deferred one.
            if (_isDraggingKeyframeMarker) return;

            DopesheetCanvas.Children.Clear();
            DeleteKeyframeButton.IsEnabled = false;
            _selectedMarkerVisual = null;

            if (_sceneViewModel.SelectedNode?.UnderlyingNode is not { } node) return;
            if (!_timeline.TryGetTrack(node, out var track) || track is null) return;

            // DopesheetCanvas hasn't necessarily been laid out yet the very first time
            // this runs (e.g. from the constructor, before the window's first Loaded) -
            // ActualWidth reads 0 then, which would collapse every marker onto the same
            // X=0 point. Falls back to the SAME nominal 280 the containing Grid/
            // FrameScrubber are both authored at (see MainWindow.xaml), so markers still
            // land at roughly the right spot even pre-layout, rather than all stacking
            // at the very left edge.
            var canvasWidth = DopesheetCanvas.ActualWidth > 0 ? DopesheetCanvas.ActualWidth : 280;

            foreach (var keyframe in track.Keyframes)
            {
                var frame = keyframe.Time * _timeline.FrameRate;
                var x = _timeline.TotalFrames > 0 ? frame / _timeline.TotalFrames * canvasWidth : 0;
                var isSelected = _selectedKeyframeTime is { } selectedTime && Math.Abs(selectedTime - keyframe.Time) < 1e-6;

                var marker = CreateKeyframeMarker(keyframe.Time, isSelected);
                Canvas.SetLeft(marker, x - 4); // 4 = half the marker's own 8px width, centering it on the frame position
                Canvas.SetTop(marker, 1);
                DopesheetCanvas.Children.Add(marker);

                if (isSelected)
                {
                    DeleteKeyframeButton.IsEnabled = true;
                    _selectedMarkerVisual = marker;
                }
            }
        }

        /// <summary>Builds one Dopesheet marker - a small 8x8 diamond, gold while
        /// selected (matching this window's own established "gold = active/selected"
        /// convention - see <see cref="MainWindow.xaml"/>'s own <c>GizmoModeButtonStyle</c>
        /// remarks) or a neutral gray otherwise - carrying its own keyframe
        /// <paramref name="time"/> on <see cref="System.Windows.FrameworkElement.Tag"/>
        /// so its own mouse handlers below know WHICH keyframe they're
        /// selecting/dragging/retiming without any separate marker-to-time lookup
        /// table.</summary>
        private System.Windows.Shapes.Polygon CreateKeyframeMarker(double time, bool isSelected)
        {
            var marker = new System.Windows.Shapes.Polygon
            {
                Points = new PointCollection { new Point(4, 0), new Point(8, 4), new Point(4, 8), new Point(0, 4) },
                Fill = isSelected ? Brushes.Gold : Brushes.LightGray,
                Stroke = Brushes.Black,
                StrokeThickness = 0.5,
                Tag = time,
                Cursor = System.Windows.Input.Cursors.SizeWE,
            };

            marker.MouseLeftButtonDown += DopesheetMarker_MouseLeftButtonDown;
            marker.MouseMove += DopesheetMarker_MouseMove;
            marker.MouseLeftButtonUp += DopesheetMarker_MouseLeftButtonUp;

            return marker;
        }

        /// <summary>The Dopesheet marker <see cref="System.Windows.Shapes.Polygon"/>
        /// instance last selected via <see cref="DopesheetMarker_MouseLeftButtonDown"/> -
        /// kept in sync with (never a second source of truth beyond) <see cref="_selectedKeyframeTime"/>,
        /// purely so a mouse-down can highlight the CLICKED marker directly (a plain
        /// <see cref="System.Windows.Shapes.Shape.Fill"/> mutation) without a full
        /// <see cref="RefreshDopesheet"/> rebuild destroying the very marker that just
        /// captured the mouse - see <see cref="_isDraggingKeyframeMarker"/>'s own remarks
        /// for why that would be a real hazard, not just a wasted rebuild. Reset to
        /// whichever marker <see cref="RefreshDopesheet"/> itself judges selected on
        /// every ordinary (non-drag) rebuild, so it never goes stale relative to
        /// <see cref="_selectedKeyframeTime"/>.</summary>
        private System.Windows.Shapes.Polygon? _selectedMarkerVisual;

        /// <summary>A marker's own mouse-down: SELECTS it (enabling <see cref="DeleteKeyframeButton"/>
        /// and highlighting it gold, via a direct <see cref="System.Windows.Shapes.Shape.Fill"/>
        /// mutation on both the newly- and previously-selected marker - NOT a
        /// <see cref="RefreshDopesheet"/> call, which would immediately destroy the very
        /// marker this method is about to capture the mouse on) and captures the mouse on
        /// itself so <see cref="DopesheetMarker_MouseMove"/> keeps dragging it for the
        /// rest of the gesture - a plain click with no further movement still counts as a
        /// selection either way, exactly like every other "click selects, drag also
        /// moves" component picker in this project (e.g. <c>ComponentHitTester</c>'s own
        /// vertex/edge/face selection).</summary>
        private void DopesheetMarker_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not System.Windows.Shapes.Polygon marker) return;

            if (_selectedMarkerVisual is { } previous) previous.Fill = Brushes.LightGray;
            _selectedKeyframeTime = (double)marker.Tag;
            _selectedMarkerVisual = marker;
            marker.Fill = Brushes.Gold;
            DeleteKeyframeButton.IsEnabled = true;

            _isDraggingKeyframeMarker = true;
            marker.CaptureMouse();
            e.Handled = true;
        }

        /// <summary>While a marker holds its own mouse capture (see
        /// <see cref="DopesheetMarker_MouseLeftButtonDown"/>), live-updates its OWN visual
        /// X position to track the cursor - a purely cosmetic, non-committing preview;
        /// the actual <see cref="AnimationTrack.RetimeKeyframe"/> call only happens once,
        /// in <see cref="DopesheetMarker_MouseLeftButtonUp"/>, when the drag actually
        /// ends.</summary>
        private void DopesheetMarker_MouseMove(object sender, MouseEventArgs e)
        {
            if (sender is not System.Windows.Shapes.Polygon marker) return;
            if (!marker.IsMouseCaptured || e.LeftButton != MouseButtonState.Pressed) return;

            var canvasWidth = DopesheetCanvas.ActualWidth;
            if (canvasWidth <= 0) return;

            var x = Math.Clamp(e.GetPosition(DopesheetCanvas).X, 0, canvasWidth);
            Canvas.SetLeft(marker, x - 4);
        }

        /// <summary>Ends the drag - releases capture, then commits the actual retime
        /// (<see cref="AnimationTrack.RetimeKeyframe"/>) from wherever the cursor ended up,
        /// converting its own final pixel X back into a frame (the inverse of
        /// <see cref="RefreshDopesheet"/>'s own frame-to-pixel mapping) and then seconds.
        /// A plain click with no real movement retimes the keyframe onto (very nearly)
        /// its own already-current time - <see cref="AnimationTrack.RetimeKeyframe"/>'s
        /// own tolerance-based replace-in-place handles that as a harmless no-op-shaped
        /// move, not a special case this method needs of its own. Re-renders/re-applies
        /// the timeline afterward (a retime can change what's CURRENTLY displayed, if the
        /// scrubber's own position happens to sit between the keyframe's old and new
        /// time) and always rebuilds the Dopesheet fresh, whether the retime actually
        /// changed anything or not.</summary>
        private void DopesheetMarker_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is not System.Windows.Shapes.Polygon marker) return;
            if (!marker.IsMouseCaptured) return;

            marker.ReleaseMouseCapture();
            _isDraggingKeyframeMarker = false;
            e.Handled = true;

            if (_sceneViewModel.SelectedNode?.UnderlyingNode is { } node &&
                _timeline.TryGetTrack(node, out var track) && track is not null)
            {
                var canvasWidth = DopesheetCanvas.ActualWidth;
                if (canvasWidth > 0)
                {
                    var oldTime = (double)marker.Tag;
                    var x = Math.Clamp(e.GetPosition(DopesheetCanvas).X, 0, canvasWidth);
                    var frame = x / canvasWidth * _timeline.TotalFrames;
                    var newTime = frame / _timeline.FrameRate;

                    var moved = false;
                    RecordKeyframeEdit(node, "Move Keyframe", () => moved = track.RetimeKeyframe(oldTime, newTime));
                    if (moved)
                    {
                        _selectedKeyframeTime = newTime;
                        _timeline.Apply();
                        _sceneViewModel.SelectedNode?.SyncFromCore();
                        _renderer.Refresh();
                    }
                }
            }

            RefreshDopesheet();
        }

        /// <summary>The Dopesheet's own "Delete" button - removes whichever keyframe
        /// <see cref="_selectedKeyframeTime"/> currently points at (see
        /// <see cref="DopesheetMarker_MouseLeftButtonDown"/>) via <see cref="AnimationTrack.RemoveKeyframe"/>.
        /// A no-op with nothing selected (the button is disabled then anyway - see
        /// <see cref="RefreshDopesheet"/>), or if the selected node's own track has
        /// since disappeared entirely.</summary>
        private void DeleteKeyframeButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            if (_selectedKeyframeTime is not { } time) return;
            if (_sceneViewModel.SelectedNode?.UnderlyingNode is not { } node) return;
            if (!_timeline.TryGetTrack(node, out var track) || track is null) return;

            RecordKeyframeEdit(node, "Delete Keyframe", () => track.RemoveKeyframe(time));
            _selectedKeyframeTime = null;
            _timeline.Apply();
            _sceneViewModel.SelectedNode?.SyncFromCore();
            _renderer.Refresh();
            RefreshDopesheet();
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
        private void LoadEmissionMapButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized || _sceneViewModel.SelectedNode is not { } nodeViewModel) return;
            var dialog = new OpenFileDialog
            {
                Filter = "Images (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|All files (*.*)|*.*",
                Title = "Load Emission Map",
            };
            if (dialog.ShowDialog(this) != true) return;
            TryRun("Load Emission Map", () =>
            {
                nodeViewModel.SetEmissiveTexture(dialog.FileName);
                _renderer.Refresh();
            });
        }

        private void ClearEmissionMapButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized || _sceneViewModel.SelectedNode is not { } nodeViewModel) return;
            nodeViewModel.SetEmissiveTexture(null);
            _renderer.Refresh();
        }

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
        /// <c>viewport.Render</c> - the exact same call any future
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
