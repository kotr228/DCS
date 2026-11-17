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
    /// <summary>Box select, modelling operators and multi-object selection.</summary>
    public partial class MainWindow
    {
        // ================= Box select (edit mode) =================

        private Point? _boxSelectStart;

        private void BeginBoxSelect(Point start)
        {
            _boxSelectStart = start;
            Viewport.CaptureMouse();
        }

        private void UpdateBoxSelect(Point current)
        {
            if (_boxSelectStart is not { } start) return;
            Viewport.SetSelectionRectangle(new Rect(start, current));
        }

        /// <summary>Shift adds, Ctrl removes, otherwise the box replaces the selection.
        /// A click without dragging on empty space deselects (unless Shift is held).</summary>
        private void EndBoxSelect(Point end)
        {
            if (_boxSelectStart is not { } start) return;
            _boxSelectStart = null;
            Viewport.SetSelectionRectangle(null);
            if (Viewport.IsMouseCaptured) Viewport.ReleaseMouseCapture();

            if (!IsEditMode)
            {
                var objectRect = new Rect(start, end);
                if (objectRect.Width < 3 && objectRect.Height < 3)
                {
                    if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) SelectNode(null);
                }
                else BoxSelectObjects(objectRect, Keyboard.Modifiers);
                return;
            }

            var session = _renderer.EditSession;
            if (session.Target is not { } node) return;
            var mods = Keyboard.Modifiers;
            var rect = new Rect(start, end);
            if (rect.Width < 3 && rect.Height < 3)
            {
                if (!mods.HasFlag(ModifierKeys.Shift)) session.Clear();
            }
            else
            {
                var inside = ComponentHitTester.VerticesInRect(Viewport, node, rect);
                if (!mods.HasFlag(ModifierKeys.Shift) && !mods.HasFlag(ModifierKeys.Control)) session.Clear();
                session.ModifySelection(inside, subtract: mods.HasFlag(ModifierKeys.Control));
            }
            _renderer.RefreshComponentOverlay();
            _componentGizmo.Attach(session);
        }

        // ================= Modelling operators =================

        /// <summary>Runs a mesh operator on the edit-mode selection as one undo step, then
        /// selects its result. With <paramref name="thenGrab"/> the result follows the mouse
        /// (Blender's "extrude and move").</summary>
        private void RunEditOperator(string name, Func<Mesh, IReadOnlySet<int>, IReadOnlySet<int>?> op, string failureHint, bool thenGrab = false)
        {
            var session = _renderer.EditSession;
            if (session.Target is not { Mesh: not null } node) return;
            var selection = session.SelectedVertexIndices.ToHashSet();
            IReadOnlySet<int>? result = null;

            TryRun(name, () =>
            {
                var command = MeshEditCommandFactory.Capture(node, name, () =>
                {
                    result = op(node.Mesh!, selection);
                    return result is not null;
                }, onChanged: () =>
                {
                    _renderer.Refresh();
                    _componentGizmo.Attach(session);
                });

                if (command is null)
                {
                    Viewport.ShowMessage(failureHint);
                    return;
                }

                _commandHistory.Record(command);
                session.SetSelection(result!);
                _renderer.Refresh();
                _componentGizmo.Attach(session);
                if (thenGrab && Viewport.IsMouseOver) _componentGizmo.BeginModal(Mouse.GetPosition(Viewport));
            });
        }

        private void ExtrudeRegion() => RunEditOperator("Extrude Region", (m, sel) => MeshOperators.ExtrudeRegion(m, sel), "Select one or more faces to extrude.", thenGrab: true);
        private void InsetFaces() => RunEditOperator("Inset Faces", (m, sel) => MeshOperators.InsetFaces(m, sel, 0.2f), "Select one or more faces to inset.");
        private void MergeAtCenter() => RunEditOperator("Merge at Center", (m, sel) => MeshOperators.MergeAtCenter(m, sel), "Select at least two vertices to merge.");
        private void FillFace() => RunEditOperator("Fill", (m, sel) => MeshOperators.Fill(m, sel), "Select at least three vertices to make a face.");
        /// <summary>P: Separate Selection - the selected faces become a new object (same
        /// transform, modifiers and material), one undo step.</summary>
        private void SeparateSelection()
        {
            var session = _renderer.EditSession;
            if (session.Target is not { Mesh: not null } node) return;
            var selection = session.SelectedVertexIndices.ToHashSet();
            TryRun("Separate", () =>
            {
                Mesh? part = null;
                var meshCommand = MeshEditCommandFactory.Capture(node, "Separate", () =>
                {
                    part = MeshFaceOperators.Separate(node.Mesh!, selection);
                    return part is not null;
                }, onChanged: () =>
                {
                    _renderer.Refresh();
                    _componentGizmo.Attach(session);
                });
                if (meshCommand is null || part is null)
                {
                    Viewport.ShowMessage("Select one or more faces to separate.");
                    return;
                }
                var piece = new Node(node.Name + ".001")
                {
                    Mesh = part,
                    LocalPosition = node.LocalPosition,
                    LocalRotation = node.LocalRotation,
                    LocalScale = node.LocalScale,
                };
                foreach (var modifier in node.Modifiers) piece.Modifiers.Add(modifier.Clone());
                var addCommand = new AddNodeCommand(_currentScene, node.Parent, piece, "Separate", OnSceneStructureChanged);
                addCommand.Execute();
                _commandHistory.Record(new CompositeCommand("Separate", new IUndoableCommand[] { meshCommand, addCommand }));
                session.SetSelection(Array.Empty<int>());
                _renderer.Refresh();
                _componentGizmo.Attach(session);
                // Like Blender, stay in Edit Mode on the original object.
                if (!IsEditMode) { SelectNode(node); SelectModeByTag("Edit"); }
                Viewport.ShowMessage($"Separated {part.Polygons.Count} face(s) into {piece.Name}");
            });
        }

        private void PokeFaces() => RunEditOperator("Poke Faces", (m, sel) => MeshFaceOperators.Poke(m, sel), "Select one or more faces to poke.");
        private void TriangulateFaces() => RunEditOperator("Triangulate Faces", (m, sel) => MeshFaceOperators.Triangulate(m, sel), "Select quads or n-gons to triangulate.");
        private void TrisToQuads() => RunEditOperator("Tris to Quads", (m, sel) => MeshFaceOperators.TrisToQuads(m, sel), "Select neighbouring triangles that can form flat, convex quads.");
        private void DissolveFaces() => RunEditOperator("Dissolve Faces", (m, sel) => MeshFaceOperators.DissolveFaces(m, sel), "Select two or more connected faces (without holes) to dissolve.");

        private void SelectAllComponents(bool select)
        {
            var session = _renderer.EditSession;
            if (select) session.SelectAll(); else session.Clear();
            _renderer.RefreshComponentOverlay();
            _componentGizmo.Attach(session);
        }

        private void InvertComponentSelection()
        {
            _renderer.EditSession.InvertSelection();
            _renderer.RefreshComponentOverlay();
            _componentGizmo.Attach(_renderer.EditSession);
        }

        private void SelectLinkedComponents()
        {
            var session = _renderer.EditSession;
            if (session.Target?.Mesh is not { } mesh || !session.HasSelection) return;
            session.SetSelection(MeshOperators.SelectLinked(mesh, session.SelectedVertexIndices.ToHashSet()));
            _renderer.RefreshComponentOverlay();
            _componentGizmo.Attach(session);
        }

        /// <summary>Records an object-level change to a node AND its direct children (whose
        /// transforms some operators compensate) as one undo step.</summary>
        private void RunObjectOperator(string name, Func<Node, bool> op)
        {
            if (_sceneViewModel.SelectedNode?.UnderlyingNode is not { } node) return;
            CommitPendingEdit();
            var affected = new[] { node }.Concat(node.Children).ToList();
            var before = affected.Select(n => NodeState.Capture(n)).ToList();
            var changed = false;
            TryRun(name, () => changed = op(node));
            if (!changed) return;
            var commands = affected.Select((n, i) => (IUndoableCommand)new NodeStateCommand(n, before[i], NodeState.Capture(n, before[i]), name, () => OnNodeStateRestored(n)));
            _commandHistory.Record(new CompositeCommand(name, commands));
            _renderer.Refresh();
            _sceneViewModel.FindViewModel(node)?.ReloadFromCore();
        }

        private void ApplyAllTransforms() => RunObjectOperator("Apply All Transforms", node => { MeshOperators.ApplyTransform(node); return true; });
        private void OriginToGeometry() => RunObjectOperator("Origin to Geometry", MeshOperators.OriginToGeometry);

        private void ApplyTransformsMenuItem_Click(object sender, RoutedEventArgs e) { if (_isInitialized) ApplyAllTransforms(); }
        private void OriginToGeometryMenuItem_Click(object sender, RoutedEventArgs e) { if (_isInitialized) OriginToGeometry(); }
        private void SelectAllMenuItem_Click(object sender, RoutedEventArgs e) { if (_isInitialized && IsEditMode) SelectAllComponents(true); }
        private void InvertSelectionMenuItem_Click(object sender, RoutedEventArgs e) { if (_isInitialized && IsEditMode) InvertComponentSelection(); }
        private void SelectLinkedMenuItem_Click(object sender, RoutedEventArgs e) { if (_isInitialized && IsEditMode) SelectLinkedComponents(); }
        private void ExtrudeRegionMenuItem_Click(object sender, RoutedEventArgs e) { if (_isInitialized && IsEditMode) ExtrudeRegion(); }
        private void InsetMenuItem_Click(object sender, RoutedEventArgs e) { if (_isInitialized && IsEditMode) InsetFaces(); }
        private void MergeMenuItem_Click(object sender, RoutedEventArgs e) { if (_isInitialized && IsEditMode) MergeAtCenter(); }
        private void FillMenuItem_Click(object sender, RoutedEventArgs e) { if (_isInitialized && IsEditMode) FillFace(); }
        private void SeparateMenuItem_Click(object sender, RoutedEventArgs e) { if (_isInitialized && IsEditMode) SeparateSelection(); }
        private void PokeMenuItem_Click(object sender, RoutedEventArgs e) { if (_isInitialized && IsEditMode) PokeFaces(); }
        private void TriangulateFacesMenuItem_Click(object sender, RoutedEventArgs e) { if (_isInitialized && IsEditMode) TriangulateFaces(); }
        private void TrisToQuadsMenuItem_Click(object sender, RoutedEventArgs e) { if (_isInitialized && IsEditMode) TrisToQuads(); }
        private void DissolveFacesMenuItem_Click(object sender, RoutedEventArgs e) { if (_isInitialized && IsEditMode) DissolveFaces(); }

        // ================= Multi-object selection =================

        // Every selected object, the active one (Inspector / gizmo target) last.
        private readonly List<Node> _objectSelection = new();
        private bool _keepGroupOnSelect;

        /// <summary>Makes <paramref name="active"/> the active object without dropping the rest of the group.</summary>
        private void SelectKeepingGroup(Node? active)
        {
            _keepGroupOnSelect = true;
            try { SelectNode(active); }
            finally { _keepGroupOnSelect = false; }
        }

        /// <summary>Shift+click: add an object, make an already-selected one active, or
        /// remove the active one (Blender behaviour).</summary>
        private void ToggleInSelection(Node? node)
        {
            if (node is null) return;
            var active = _sceneViewModel.SelectedNode?.UnderlyingNode;
            if (_objectSelection.Contains(node))
            {
                if (ReferenceEquals(node, active))
                {
                    _objectSelection.Remove(node);
                    SelectKeepingGroup(_objectSelection.LastOrDefault());
                    return;
                }
                _objectSelection.Remove(node);
            }
            _objectSelection.Add(node);
            SelectKeepingGroup(node);
        }

        private void SelectAllObjects(bool select)
        {
            if (!select) { SelectNode(null); return; }
            var active = _sceneViewModel.SelectedNode?.UnderlyingNode;
            _objectSelection.Clear();
            _objectSelection.AddRange(_currentScene.Traverse().Where(n => !n.IsHidden && !ReferenceEquals(n, active)));
            if (active is not null) _objectSelection.Add(active);
            SelectKeepingGroup(_objectSelection.LastOrDefault());
        }

        /// <summary>Objects whose origin or any (first few thousand) vertex projects into <paramref name="rect"/>.</summary>
        private void BoxSelectObjects(Rect rect, ModifierKeys mods)
        {
            var hits = new List<Node>();
            foreach (var node in _currentScene.Traverse())
            {
                if (node.IsHidden) continue;
                if (Viewport.WorldToScreen(node.GetWorldPosition()) is { } origin && rect.Contains(origin)) { hits.Add(node); continue; }
                if (node.Mesh is not { } mesh) continue;
                var world = node.GetWorldTransform();
                var step = Math.Max(1, mesh.Vertices.Count / 4000);
                for (var i = 0; i < mesh.Vertices.Count; i += step)
                {
                    if (Viewport.WorldToScreen(System.Numerics.Vector3.Transform(mesh.Vertices[i].Position, world)) is { } p && rect.Contains(p))
                    {
                        hits.Add(node);
                        break;
                    }
                }
            }

            var active = _sceneViewModel.SelectedNode?.UnderlyingNode;
            if (mods.HasFlag(ModifierKeys.Control))
            {
                foreach (var h in hits) _objectSelection.Remove(h);
                SelectKeepingGroup(_objectSelection.Contains(active!) ? active : _objectSelection.LastOrDefault());
                return;
            }
            if (!mods.HasFlag(ModifierKeys.Shift)) _objectSelection.Clear();
            foreach (var h in hits) if (!_objectSelection.Contains(h)) _objectSelection.Add(h);
            var newActive = active is not null && _objectSelection.Contains(active) ? active : hits.LastOrDefault();
            if (newActive is not null) { _objectSelection.Remove(newActive); _objectSelection.Add(newActive); }
            SelectKeepingGroup(newActive);
        }

        /// <summary>The selected objects that don't have a selected ancestor (they move/delete with it).</summary>
        private List<Node> TopLevelSelection()
        {
            var set = _objectSelection.ToHashSet();
            bool HasSelectedAncestor(Node n)
            {
                for (var p = n.Parent; p is not null; p = p.Parent) if (set.Contains(p)) return true;
                return false;
            }
            return _objectSelection.Where(n => !HasSelectedAncestor(n)).ToList();
        }

        private void DeleteSelectedObjects()
        {
            var targets = TopLevelSelection();
            if (targets.Count == 0) return;
            TryRun("Delete", () =>
            {
                var commands = targets.Select(n => (IUndoableCommand)DeleteNodeCommandFactory.Create(_currentScene, n, _timeline, onChanged: OnSceneStructureChanged)).ToList();
                _commandHistory.Execute(new CompositeCommand(targets.Count == 1 ? "Delete" : $"Delete {targets.Count} Objects", commands));
                foreach (var n in targets) foreach (var removed in n.Traverse()) _textureSources.Remove(removed);
                SelectNode(null);
            });
        }

        private void DuplicateSelectedObjects()
        {
            var targets = TopLevelSelection();
            if (targets.Count == 0) return;
            TryRun("Duplicate", () =>
            {
                var commands = targets.Select(n => DuplicateNodeCommandFactory.Create(_currentScene, n, _timeline, onChanged: OnSceneStructureChanged)).ToList();
                _commandHistory.Execute(new CompositeCommand("Duplicate", commands));
                _objectSelection.Clear();
                _objectSelection.AddRange(commands.Select(c => c.Clone));
                SelectKeepingGroup(_objectSelection.LastOrDefault());
            });
        }

        /// <summary>Alt+D: Duplicate Linked - copies that share their source's mesh data (and
        /// so its edits), like Blender. Modifiers are copied per object.</summary>
        private void DuplicateLinkedObjects()
        {
            var targets = TopLevelSelection();
            if (targets.Count == 0) return;
            TryRun("Duplicate Linked", () =>
            {
                var copies = new List<Node>();
                var commands = new List<IUndoableCommand>();
                foreach (var source in targets)
                {
                    var copy = source.Clone();
                    // Re-point every copied node at the original's mesh (shared data).
                    foreach (var (original, duplicate) in source.Traverse().Zip(copy.Traverse()))
                        if (original.Mesh is not null) duplicate.Mesh = original.Mesh;
                    copies.Add(copy);
                    commands.Add(new AddNodeCommand(_currentScene, source.Parent, copy, "Duplicate Linked", OnSceneStructureChanged));
                }
                _commandHistory.Execute(new CompositeCommand("Duplicate Linked", commands));
                _objectSelection.Clear();
                _objectSelection.AddRange(copies);
                SelectKeepingGroup(_objectSelection.LastOrDefault());
            });
        }

        private void DuplicateLinkedMenuItem_Click(object sender, RoutedEventArgs e) { if (_isInitialized && !IsEditMode) DuplicateLinkedObjects(); }

        /// <summary>Ctrl+J: joins every other selected mesh object into the active one.</summary>
        private void JoinSelectedObjects()
        {
            if (_sceneViewModel.SelectedNode?.UnderlyingNode is not { Mesh: not null } active)
            {
                Viewport.ShowMessage("Join needs an active mesh object.");
                return;
            }
            var activeLineage = active.Traverse().ToHashSet();
            for (var p = active.Parent; p is not null; p = p.Parent) activeLineage.Add(p);
            var sources = TopLevelSelection().Where(n => !activeLineage.Contains(n) && n.Mesh is not null && n.Children.Count == 0).ToList();
            if (sources.Count == 0)
            {
                Viewport.ShowMessage("Select other mesh objects (without children), then the target last.");
                return;
            }

            TryRun("Join", () =>
            {
                var activeWorld = active.GetWorldTransform();
                var meshCommand = MeshEditCommandFactory.Capture(active, "Join", () =>
                {
                    foreach (var source in sources)
                        MeshOperators.JoinInto(active.Mesh!, activeWorld, source.Mesh!, source.GetWorldTransform());
                    return true;
                }, onChanged: () => _renderer.Refresh());
                if (meshCommand is null) return;

                var deletes = sources.Select(n => DeleteNodeCommandFactory.Create(_currentScene, n, _timeline, onChanged: OnSceneStructureChanged)).ToList();
                foreach (var d in deletes) d.Execute();
                _commandHistory.Record(new CompositeCommand("Join", new IUndoableCommand[] { meshCommand }.Concat(deletes)));
                SelectNode(active);
                _sceneViewModel.FindViewModel(active)?.ReloadFromCore();
            });
        }
    }
}
