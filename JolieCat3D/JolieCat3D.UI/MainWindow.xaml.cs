using System.IO;
using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using JolieCat3D.Core.Geometry;
using JolieCat3D.Core.Materials;
using JolieCat3D.Core.Numerics;
using JolieCat3D.Core.Scene;
using JolieCat3D.Engine.Editing;
using JolieCat3D.Engine.Gizmos;
using JolieCat3D.Engine.Rendering;
using JolieCat3D.Service;
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

        private Scene3D _currentScene = new("Untitled");
        private string? _currentFilePath;

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
            _gizmo = new TransformGizmo(Viewport);
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

            // A component-gizmo drag mutates the target mesh's own vertex positions
            // directly (see MeshEditSession.ApplyTranslation) - Refresh() re-renders the
            // scene from that updated Core.Geometry.Mesh AND rebuilds the marker overlay
            // (Scene3DRenderer.Refresh already calls RefreshComponentOverlay itself), so
            // both the mesh on screen and its vertex/edge/face dots catch up to the drag
            // together - the "immediate GPU geometry update" Edit Mode needs.
            _componentGizmo.EditApplied += (_, _) => _renderer.Refresh();

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

            LoadScene(BuildDemoScene(), filePath: null);
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

        /// <summary>Writes the current scene out to a file without adopting it as "the"
        /// working file the way Save/Save As do - <see cref="_currentFilePath"/> (and
        /// the window title) are left exactly as they were, so a one-off "send a copy as
        /// STL" doesn't silently redirect a later Ctrl+S away from the OBJ someone's
        /// actually been editing.</summary>
        private void ExportMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog { Filter = FileDialogFilter, Title = "Export", FileName = _currentFilePath ?? "Untitled.obj" };
            if (dialog.ShowDialog(this) != true) return;

            TryRun("Export", () => MeshFileService.ExportScene(_currentScene, dialog.FileName));
        }

        private void ExitMenuItem_Click(object sender, RoutedEventArgs e) => Close();

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

        /// <summary>
        /// Click-to-select in the viewport - in Object Mode, a whole node (see
        /// <see cref="SelectNode"/>); in Edit Mode, a single mesh component (see
        /// <see cref="HandleComponentClick"/>) of whichever node Edit Mode is currently
        /// targeting. Either way, only ever reached for a click the active gizmo's own
        /// manipulator handles didn't already consume themselves (WPF's routed
        /// MouseLeftButtonDown only bubbles here unhandled - a manipulator marks its own
        /// mouse-down Handled the moment it starts a drag), so dragging a gizmo handle
        /// never gets misread as "clicked empty space, deselect/clear" partway through
        /// the gesture.
        /// </summary>
        private void Viewport_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var position = e.GetPosition(Viewport);

            if (IsEditMode)
            {
                HandleComponentClick(position, additive: Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
                return;
            }

            var hitNode = _renderer.HitTest(position);
            SelectNode(hitNode);
        }

        /// <summary>Selection made from the Scene Outliner instead of a viewport click -
        /// the same <see cref="SelectNode"/> path either way, so the renderer's highlight,
        /// the gizmo, and the Properties Inspector all stay in sync regardless of which
        /// one the user actually clicked.</summary>
        private void SceneTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e) =>
            SelectNode((e.NewValue as NodeViewModel)?.UnderlyingNode);

        /// <summary>True once <see cref="EditModeButton"/> (rather than
        /// <see cref="ObjectModeButton"/>) is the checked radio button - what every
        /// Edit-Mode-vs-Object-Mode branch in this window reads.</summary>
        private bool IsEditMode => EditModeButton.IsChecked == true;

        private void SelectNode(Node? node)
        {
            _renderer.Select(node);
            _sceneViewModel.SelectedNode = _sceneViewModel.FindViewModel(node);

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
            if (sender is not RadioButton { Tag: string modeName }) return;
            if (Enum.TryParse<GizmoMode>(modeName, out var mode)) _gizmo.Mode = mode;
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
            if (sender is not RadioButton { Tag: string modeName }) return;
            var enteringEditMode = modeName == "Edit";

            VertexModeButton.IsEnabled = enteringEditMode;
            EdgeModeButton.IsEnabled = enteringEditMode;
            FaceModeButton.IsEnabled = enteringEditMode;

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
            else
            {
                _componentGizmo.Attach(null);
                _renderer.ExitEditMode();
                _gizmo.Attach(_sceneViewModel.SelectedNode?.UnderlyingNode);
            }
        }

        private void ComponentModeButton_Checked(object sender, RoutedEventArgs e)
        {
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

        /// <summary>The Properties panel's "Load Texture..." button - loads an image
        /// (typically something exported from <c>JolieCat</c>'s own 2D editor workspace)
        /// through <see cref="TwoDAssetBridge"/> and adopts it as the selected node's
        /// material's whole-image diffuse texture (see <see cref="NodeViewModel.SetDiffuseTexture"/>).
        /// Disabled implicitly by the same <c>HasMaterial</c> visibility the rest of the
        /// Material section already uses - this can only ever be clicked with a
        /// mesh+material actually selected.</summary>
        private void LoadTextureButton_Click(object sender, RoutedEventArgs e)
        {
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
