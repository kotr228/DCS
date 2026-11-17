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
    /// <summary>Final render, hide/reveal, game asset helpers (LODs, colliders, scatter, symmetrize).</summary>
    public partial class MainWindow
    {
        // ================= Voxel remesh =================

        private void VoxelRemesh()
        {
            if (!_isInitialized || IsEditMode || _sceneViewModel.SelectedNode?.UnderlyingNode is not { Mesh: { } mesh } node)
            {
                Viewport.ShowMessage("Voxel Remesh: select a mesh object (Object Mode).");
                return;
            }
            var extent = mesh.Vertices.Select(v => v.Position).Aggregate(System.Numerics.Vector3.Max) - mesh.Vertices.Select(v => v.Position).Aggregate(System.Numerics.Vector3.Min);
            var suggested = MathF.Max(extent.X, MathF.Max(extent.Y, extent.Z)) / 50f;
            var dialog = new TextPromptWindow("Voxel Remesh", "Voxel size (smaller = more detail):", suggested.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture)) { Owner = this };
            if (dialog.ShowDialog() != true || dialog.Result is null) return;
            if (!float.TryParse(dialog.Result.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var voxel) || voxel <= 0)
            {
                Viewport.ShowMessage("Voxel size must be a positive number.");
                return;
            }
            TryRun("Voxel Remesh", () =>
            {
                var before = NodeState.Capture(node);
                var result = VoxelRemesher.Remesh(mesh, voxel);
                if (result is null)
                {
                    Viewport.ShowMessage("Voxel Remesh: the mesh has no closed volume.");
                    return;
                }
                node.Mesh = result;
                var after = NodeState.Capture(node, before);
                void Refresh()
                {
                    _sceneViewModel.FindViewModel(node)?.ReloadFromCore();
                    _renderer.Refresh();
                }
                _commandHistory.Record(new NodeStateCommand(node, before, after, "Voxel Remesh", Refresh));
                Refresh();
                Viewport.ShowMessage($"Remeshed: {result.Polygons.Count:N0} quads");
            });
        }

        private void VoxelRemeshMenuItem_Click(object sender, RoutedEventArgs e) => VoxelRemesh();

        // ================= Outliner visibility eye =================

        private void OutlinerEye_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!_isInitialized || sender is not FrameworkElement { DataContext: NodeViewModel vm }) return;
            e.Handled = true; // don't select the row
            var node = vm.UnderlyingNode;
            _commandHistory.Execute(new SetHiddenCommand(new[] { node }, hidden: !node.IsHidden, OnHiddenChanged));
        }

        // ================= Bake constraints =================

        /// <summary>Bakes every constraint's effect over the whole timeline into keyframes of
        /// the active action and removes the constraints - one undo step.</summary>
        private void BakeConstraints()
        {
            if (!_isInitialized) return;
            var constrained = _currentScene.Traverse().Where(n => n.Constraints.Count > 0).ToList();
            if (constrained.Count == 0)
            {
                Viewport.ShowMessage("Nothing to bake: no object has constraints.");
                return;
            }
            TryRun("Bake Constraints", () =>
            {
                var nodes = _currentScene.Traverse().ToList();
                var trackBefore = nodes.ToDictionary(n => n, n => KeyframeTrackCommand.Snapshot(_timeline, n));
                var stateBefore = constrained.ToDictionary(n => n, n => NodeState.Capture(n));

                var result = ConstraintBaker.Bake(_currentScene, _timeline, 0, _timeline.TotalFrames);

                void Refresh()
                {
                    _timeline.Apply();
                    foreach (var n in _currentScene.Traverse()) _sceneViewModel.FindViewModel(n)?.ReloadFromCore();
                    _renderer.Refresh();
                    RefreshDopesheet();
                }
                var commands = new List<IUndoableCommand>();
                foreach (var node in nodes)
                {
                    var after = KeyframeTrackCommand.Snapshot(_timeline, node);
                    if (!KeyframeTrackCommand.SameContent(trackBefore[node], after))
                        commands.Add(new KeyframeTrackCommand(() => _timeline, node, trackBefore[node], after, "Bake Constraints", Refresh));
                }
                foreach (var node in constrained)
                    commands.Add(new NodeStateCommand(node, stateBefore[node], NodeState.Capture(node, stateBefore[node]), "Bake Constraints", Refresh));
                _commandHistory.Record(new CompositeCommand("Bake Constraints", commands));
                Refresh();
                Viewport.ShowMessage($"Baked {result.BakedNodes.Count} objects over {result.Frames} frames; constraints removed");
            });
        }

        private void BakeConstraintsMenuItem_Click(object sender, RoutedEventArgs e) => BakeConstraints();

        // ================= Final render (F12) =================

        private FinalRenderOptions _renderOptions = new(1280, 256, 4, 1f, 1f);

        /// <summary>Path-traced still from the active scene camera (or the viewport view),
        /// at the viewport aspect ratio. <paramref name="askSettings"/> shows the options first.</summary>
        private void RenderImage(bool askSettings = false)
        {
            if (!_isInitialized) return;
            if (askSettings)
            {
                var dialog = new RenderSettingsWindow(_renderOptions) { Owner = this };
                if (dialog.ShowDialog() != true || dialog.Result is not { } chosen) return;
                _renderOptions = chosen;
            }
            var options = _renderOptions;
            var camera = Viewport.Camera.Clone();
            if (_currentScene.ActiveCamera is { } cameraNode) camera.MatchSceneCamera(cameraNode);
            var aspect = Math.Max(0.2, Viewport.ActualWidth / Math.Max(1, Viewport.ActualHeight));
            var width = options.Width;
            var height = Math.Max(1, (int)Math.Round(width / aspect));
            TryRun("Render", () =>
            {
                var settings = new PathTracerSettings { MaxBounces = options.Bounces, SkyStrength = options.SkyStrength, Exposure = options.Exposure };
                ApplyDepthOfField(settings);
                var tracer = new PathTracer(_currentScene, camera, width, height, settings);
                var window = new RenderWindow(tracer, options.Samples,
                    $"Render - {_documentName}{(_currentScene.ActiveCamera is { } c ? " (" + c.Name + ")" : "")}") { Owner = this };
                window.Show();
            });
        }

        private void RenderSettingsMenuItem_Click(object sender, RoutedEventArgs e) => RenderImage(askSettings: true);

        /// <summary>Depth of field comes from the active scene camera, when it has it on.</summary>
        private void ApplyDepthOfField(PathTracerSettings settings)
        {
            if (_currentScene.ActiveCamera?.Camera is not { DepthOfField: true } camera) return;
            settings.ApertureRadius = camera.ApertureRadius();
            settings.FocusDistance = camera.FocusDistance;
        }

        private CancellationTokenSource? _animationRenderCancel;

        /// <summary>Path-traced PNG sequence of the whole timeline (one tracer per frame,
        /// denoised), using the Render Settings dialog's options. Esc cancels.</summary>
        private async void RenderAnimationPathTracedMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized || _animationRenderCancel is not null) return;
            var settingsDialog = new RenderSettingsWindow(_renderOptions) { Owner = this, Title = "Render Animation Settings" };
            if (settingsDialog.ShowDialog() != true || settingsDialog.Result is not { } options) return;
            _renderOptions = options;
            var dialog = new SaveFileDialog { Filter = "PNG Image Sequence (*.png)|*.png", Title = "Render Animation (Path Traced)", FileName = "Frame.png" };
            if (dialog.ShowDialog(this) != true) return;

            var directory = Path.GetDirectoryName(dialog.FileName) ?? ".";
            var baseName = Path.GetFileNameWithoutExtension(dialog.FileName);
            var aspect = Math.Max(0.2, Viewport.ActualWidth / Math.Max(1, Viewport.ActualHeight));
            var height = Math.Max(1, (int)Math.Round(options.Width / aspect));
            var originalTime = _timeline.CurrentTime;
            _timeline.Pause();
            _animationRenderCancel = new CancellationTokenSource();
            var token = _animationRenderCancel.Token;
            var frames = _timeline.TotalFrames + 1;
            var written = 0;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                for (var frame = 0; frame < frames && !token.IsCancellationRequested; frame++)
                {
                    _timeline.CurrentFrame = frame;
                    _timeline.Apply();
                    var camera = Viewport.Camera.Clone();
                    if (_currentScene.ActiveCamera is { } cameraNode) camera.MatchSceneCamera(cameraNode);
                    var frameSettings = new PathTracerSettings { MaxBounces = options.Bounces, SkyStrength = options.SkyStrength, Exposure = options.Exposure };
                    ApplyDepthOfField(frameSettings);
                    var tracer = new PathTracer(_currentScene, camera, options.Width, height, frameSettings);
                    await Task.Run(() =>
                    {
                        for (var s = 0; s < options.Samples && !token.IsCancellationRequested; s++) tracer.RenderPass(token);
                    });
                    if (token.IsCancellationRequested) break;
                    var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                    encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(tracer.ToBitmap(denoise: true)));
                    using (var stream = File.Create(Path.Combine(directory, $"{baseName}{frame:0000}.png"))) encoder.Save(stream);
                    written++;
                    var eta = TimeSpan.FromSeconds(clock.Elapsed.TotalSeconds / written * (frames - written));
                    Viewport.ShowMessage($"Rendering frame {frame + 1}/{frames}  (about {eta:mm\\:ss} left, Esc cancels)");
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Render Animation", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _animationRenderCancel = null;
                _timeline.SetTime(originalTime);
                _timeline.Apply();
                _renderer.Refresh();
                RefreshAnimationUI();
            }
            Viewport.ShowMessage(token.IsCancellationRequested
                ? $"Render cancelled after {written} frame(s)."
                : $"Rendered {written} frame(s) to {directory} in {clock.Elapsed:mm\\:ss}.");
        }

        private void RenderImageMenuItem_Click(object sender, RoutedEventArgs e) => RenderImage();

        private void JoinMenuItem_Click(object sender, RoutedEventArgs e) { if (_isInitialized && !IsEditMode) JoinSelectedObjects(); }

        // ================= Hide / reveal (H, Shift+H, Alt+H) =================

        private void OnHiddenChanged()
        {
            foreach (var node in _currentScene.Traverse()) _sceneViewModel.FindViewModel(node)?.RefreshHidden();
            _renderer.Refresh();
            MarkChanged();
        }

        private void HideObjects(bool selected)
        {
            var chosen = _objectSelection.ToHashSet();
            var targets = selected
                ? _objectSelection.SelectMany(n => n.Traverse()).Distinct().Where(n => !n.IsHidden).ToList()
                : _currentScene.Traverse().Where(n => !n.IsHidden && !chosen.Contains(n) && !n.Traverse().Any(chosen.Contains)).ToList();
            if (targets.Count == 0) return;
            _commandHistory.Execute(new SetHiddenCommand(targets, hidden: true, OnHiddenChanged));
            if (selected) SelectNode(null);
            Viewport.ShowMessage($"Hidden: {targets.Count}  (Alt+H reveals)");
        }

        private void RevealHidden()
        {
            var hidden = _currentScene.Traverse().Where(n => n.IsHidden).ToList();
            if (hidden.Count == 0) return;
            _commandHistory.Execute(new SetHiddenCommand(hidden, hidden: false, OnHiddenChanged));
            Viewport.ShowMessage($"Revealed: {hidden.Count}");
        }

        private void HideSelectedMenuItem_Click(object sender, RoutedEventArgs e) { if (_isInitialized && !IsEditMode) HideObjects(selected: true); }
        private void HideUnselectedMenuItem_Click(object sender, RoutedEventArgs e) { if (_isInitialized && !IsEditMode) HideObjects(selected: false); }
        private void RevealHiddenMenuItem_Click(object sender, RoutedEventArgs e) { if (_isInitialized && !IsEditMode) RevealHidden(); }

        // ================= Game asset helpers: LODs and colliders =================

        /// <summary>Adds Name_LOD1..3 siblings (same transform, modifiers + Decimate 50/25/12.5%),
        /// hidden so they don't overlap the original in the viewport.</summary>
        private void GenerateLods()
        {
            if (_sceneViewModel.SelectedNode?.UnderlyingNode is not { Mesh: not null } source || IsEditMode)
            {
                Viewport.ShowMessage("Select a mesh object to generate LODs for.");
                return;
            }
            var baseName = System.Text.RegularExpressions.Regex.Replace(source.Name, "_LOD\\d+$", "");
            var commands = new List<IUndoableCommand>();
            var ratios = new[] { 0.5f, 0.25f, 0.125f };
            for (var i = 0; i < ratios.Length; i++)
            {
                var lod = new Node($"{baseName}_LOD{i + 1}")
                {
                    LocalPosition = source.LocalPosition,
                    LocalRotation = source.LocalRotation,
                    LocalScale = source.LocalScale,
                    Mesh = source.Mesh!.Clone(),
                    SkinBinding = source.SkinBinding?.Clone(),
                    IsHidden = true,
                };
                foreach (var modifier in source.Modifiers) lod.Modifiers.Add(modifier.Clone());
                lod.Modifiers.Add(new Core.Modifiers.DecimateModifier { Ratio = ratios[i] });
                commands.Add(new AddNodeCommand(_currentScene, source.Parent, lod, "Generate LODs", OnSceneStructureChanged));
            }
            _commandHistory.Execute(new CompositeCommand("Generate LODs", commands));
            SelectNode(source);
            Viewport.ShowMessage($"Added {baseName}_LOD1..3 (hidden - Alt+H to compare)");
        }

        /// <summary>Adds a hidden UCX_Name convex hull (max 32 vertices) of the object as drawn.</summary>
        private void GenerateConvexCollider()
        {
            if (_sceneViewModel.SelectedNode?.UnderlyingNode is not { Mesh: not null } source || IsEditMode)
            {
                Viewport.ShowMessage("Select a mesh object to generate a collider for.");
                return;
            }
            var drawn = Core.Modifiers.ModifierStack.Evaluate(source.Mesh!, source.Modifiers, source);
            var hull = ConvexHull.Build(drawn.Vertices.Select(v => v.Position), maxVertices: 32, name: "UCX_" + source.Name);
            if (hull is null)
            {
                Viewport.ShowMessage("Collider: the mesh is flat - no volume to wrap.");
                return;
            }
            hull.Material = new Core.Materials.Material("Collider") { DiffuseColor = new Core.Numerics.Color4(0.2f, 0.9f, 0.4f), Opacity = 0.35f };
            var collider = new Node("UCX_" + source.Name)
            {
                LocalPosition = source.LocalPosition,
                LocalRotation = source.LocalRotation,
                LocalScale = source.LocalScale,
                Mesh = hull,
                IsHidden = true,
            };
            _commandHistory.Execute(new AddNodeCommand(_currentScene, source.Parent, collider, "Generate Collider", OnSceneStructureChanged));
            SelectNode(source);
            var corners = hull.Vertices.Select(v => MeshOperators.Key(v.Position)).Distinct().Count();
            Viewport.ShowMessage($"Added {collider.Name}: {corners} vertices (hidden - Alt+H to see)");
        }

        /// <summary>Scatter: copies of the selected objects (picked at random) placed on the
        /// active object's surface, grouped under a new "Scatter" empty. Copies share their
        /// source's mesh (like Blender's linked duplicates), so thousands stay light.</summary>
        private void ScatterSelectedOnActive()
        {
            if (IsEditMode || _sceneViewModel.SelectedNode?.UnderlyingNode is not { Mesh: not null } surface)
            {
                Viewport.ShowMessage("Scatter: select the objects to scatter, then the surface object last.");
                return;
            }
            var sources = TopLevelSelection().Where(n => !ReferenceEquals(n, surface) && n.Mesh is not null).ToList();
            if (sources.Count == 0)
            {
                Viewport.ShowMessage("Scatter: select the objects to scatter, then the surface object last.");
                return;
            }
            var dialog = new ScatterWindow { Owner = this };
            if (dialog.ShowDialog() != true || dialog.Result is not { } o) return;

            TryRun("Scatter", () =>
            {
                var drawn = Core.Modifiers.ModifierStack.Evaluate(surface.Mesh!, surface.Modifiers, surface);
                var points = SurfaceSampler.Sample(drawn, surface.GetWorldTransform(), o.Count, o.Seed, o.MinDistance);
                var rng = new Random(o.Seed * 7919 + 13);
                var group = new Node($"Scatter_{surface.Name}");
                foreach (var (position, normal) in points)
                {
                    var source = sources[rng.Next(sources.Count)];
                    var up = o.AlignToNormal ? normal : System.Numerics.Vector3.UnitY;
                    var align = ShortestArc(System.Numerics.Vector3.UnitY, up);
                    var spin = o.RandomRotation ? System.Numerics.Quaternion.CreateFromAxisAngle(System.Numerics.Vector3.UnitY, rng.NextSingle() * MathF.PI * 2) : System.Numerics.Quaternion.Identity;
                    var scale = o.ScaleMin + rng.NextSingle() * (o.ScaleMax - o.ScaleMin);
                    var instance = new Node(source.Name)
                    {
                        Mesh = source.Mesh, // shared, like linked duplicates
                        LocalPosition = position,
                        LocalRotation = System.Numerics.Quaternion.Normalize(align * spin * source.GetWorldRotation()),
                        LocalScale = source.LocalScale * scale,
                    };
                    foreach (var modifier in source.Modifiers) instance.Modifiers.Add(modifier.Clone());
                    group.AddChild(instance);
                }
                if (group.Children.Count == 0)
                {
                    Viewport.ShowMessage("Scatter: the surface has no area.");
                    return;
                }
                _commandHistory.Execute(new AddNodeCommand(_currentScene, null, group, "Scatter", OnSceneStructureChanged));
                SelectNode(group);
                Viewport.ShowMessage($"Scattered {group.Children.Count} instances on {surface.Name}");
            });

            static System.Numerics.Quaternion ShortestArc(System.Numerics.Vector3 from, System.Numerics.Vector3 to)
            {
                var d = System.Numerics.Vector3.Dot(from, to);
                if (d > 0.99999f) return System.Numerics.Quaternion.Identity;
                if (d < -0.99999f) return System.Numerics.Quaternion.CreateFromAxisAngle(System.Numerics.Vector3.UnitX, MathF.PI);
                var axis = System.Numerics.Vector3.Normalize(System.Numerics.Vector3.Cross(from, to));
                return System.Numerics.Quaternion.CreateFromAxisAngle(axis, MathF.Acos(Math.Clamp(d, -1f, 1f)));
            }
        }

        /// <summary>Mesh > Symmetrize on the active mesh (object or edit mode), undoable.</summary>
        private void Symmetrize(bool positiveToNegative)
        {
            if (!_isInitialized) return;
            var node = IsEditMode ? _renderer.EditSession.Target : _sceneViewModel.SelectedNode?.UnderlyingNode;
            if (node?.Mesh is null)
            {
                Viewport.ShowMessage("Symmetrize needs a mesh object.");
                return;
            }
            TryRun("Symmetrize", () =>
            {
                var command = MeshEditCommandFactory.Capture(node, "Symmetrize", () =>
                {
                    MeshSymmetry.Symmetrize(node.Mesh!, 0, positiveToNegative);
                    return true;
                }, onChanged: () => _renderer.Refresh());
                if (command is null) return;
                _commandHistory.Record(command);
                if (IsEditMode) _renderer.EditSession.SetSelection(new HashSet<int>());
                _renderer.Refresh();
                Viewport.ShowMessage(positiveToNegative ? "Symmetrized +X to -X" : "Symmetrized -X to +X");
            });
        }

        private void SymmetrizePositiveMenuItem_Click(object sender, RoutedEventArgs e) => Symmetrize(true);
        private void SymmetrizeNegativeMenuItem_Click(object sender, RoutedEventArgs e) => Symmetrize(false);

        private void ScatterMenuItem_Click(object sender, RoutedEventArgs e) { if (_isInitialized) ScatterSelectedOnActive(); }

        private void GenerateLodsMenuItem_Click(object sender, RoutedEventArgs e) { if (_isInitialized) GenerateLods(); }
        private void GenerateColliderMenuItem_Click(object sender, RoutedEventArgs e) { if (_isInitialized) GenerateConvexCollider(); }
        private void SelectAllObjectsMenuItem_Click(object sender, RoutedEventArgs e) { if (_isInitialized && !IsEditMode) SelectAllObjects(true); }
    }
}
