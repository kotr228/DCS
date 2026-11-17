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
    /// <summary>UV editor, edit-mode buttons, modifiers/shape keys/constraints panels.</summary>
    public partial class MainWindow
    {
        // ================= UV Editor =================

        private UVEditorWindow? _uvEditor;

        private void OpenUvEditor()
        {
            if (_uvEditor is not null) { _uvEditor.Activate(); return; }
            _uvEditor = new UVEditorWindow(
                () => _renderer.EditSession.Target ?? _sceneViewModel.SelectedNode?.UnderlyingNode,
                () => _renderer.EditSession.Target is not null ? _renderer.EditSession.SelectedVertexIndices : Array.Empty<int>(),
                (node, before, description) =>
                {
                    CommitPendingEdit();
                    _commandHistory.Record(new MeshEditCommand(node, before, node.Mesh!, description, onChanged: () =>
                    {
                        _renderer.Refresh();
                        _uvEditor?.Redraw();
                    }));
                },
                () => _renderer.Refresh())
            { Owner = this };
            _uvEditor.Closed += (_, _) => _uvEditor = null;
            _uvEditor.Show();
        }

        private void UvEditorMenuItem_Click(object sender, RoutedEventArgs e) { if (_isInitialized) OpenUvEditor(); }

        /// <summary>One Graph Editor gesture = one undo step (keyframes before -> now).</summary>
        private void CommitGraphEdit(Node node, AnimationTrack? before, string description)
        {
            var after = KeyframeTrackCommand.Snapshot(_timeline, node);
            _commandHistory.Record(new KeyframeTrackCommand(() => _timeline, node, before, after, description, () =>
            {
                PreviewAnimation();
                _graphEditor?.Redraw();
            }));
            RefreshDopesheet();
        }

        /// <summary>Re-poses the scene for the current frame after keys changed.</summary>
        private void PreviewAnimation()
        {
            _timeline.Apply();
            _sceneViewModel.SelectedNode?.SyncFromCore();
            _renderer.Refresh();
            RefreshDopesheet();
        }

        private void SetAnimationTime(double seconds)
        {
            _timeline.SetTime(seconds);
            PreviewAnimation();
            RefreshAnimationUI();
        }

        /// <summary>I: key location/rotation/scale of every selected object at the current frame.</summary>
        private void InsertKeyframesForSelection()
        {
            var nodes = _objectSelection.Count > 0 ? _objectSelection.ToList() : new List<Node>();
            if (nodes.Count == 0) { Viewport.ShowMessage("Select an object to keyframe."); return; }
            var interpolation = BezierInterpolationButton.IsChecked == true ? InterpolationMode.Bezier : InterpolationMode.Linear;
            foreach (var node in nodes)
                RecordKeyframeEdit(node, "Insert Keyframe", () => _timeline.GetOrCreateTrack(node).AddKeyframeFromCurrentTransform(_timeline.CurrentTime, interpolation));
            RefreshDopesheet();
            _graphEditor?.Redraw();
            Viewport.ShowMessage($"Keyframe inserted at frame {_timeline.CurrentFrame:0} ({nodes.Count} object(s))");
        }

        /// <summary>Alt+I: remove the selected objects' keyframes at the current frame.</summary>
        private void DeleteKeyframesForSelection()
        {
            foreach (var node in _objectSelection.ToList())
            {
                if (!_timeline.TryGetTrack(node, out var track) || track is null) continue;
                var time = _timeline.CurrentTime;
                RecordKeyframeEdit(node, "Delete Keyframe", () => track.RemoveKeyframe(time));
            }
            RefreshDopesheet();
            _graphEditor?.Redraw();
        }

        /// <summary>Shift+C: 3D cursor back to the world origin and the whole scene framed.</summary>
        private void ResetCursorAndFrame()
        {
            Viewport.CursorPosition = System.Numerics.Vector3.Zero;
            Viewport.ZoomToFit(_currentScene);
        }

        private void CursorToSelectedMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            CursorToSelected();
        }

        private void CursorToSelected()
        {
            if (IsEditMode && _renderer.EditSession.GetSelectionWorldCentroid() is { } c) Viewport.CursorPosition = c;
            else if (_objectSelection.Count > 0)
                Viewport.CursorPosition = _objectSelection.Aggregate(System.Numerics.Vector3.Zero, (sum, n) => sum + n.GetWorldPosition()) / _objectSelection.Count;
            else if (_sceneViewModel.SelectedNode?.UnderlyingNode is { } n) Viewport.CursorPosition = n.GetWorldPosition();
        }

        /// <summary>Shift+E (Edit Mode): creases the selected edges for Subdivision Surface,
        /// or clears them when they are all creased already. Undoable.</summary>
        private void ToggleCrease()
        {
            var session = _renderer.EditSession;
            if (session.Target is not { Mesh: not null } node) return;
            var crease = !session.SelectionFullyCreased();
            var changed = 0;
            var command = MeshEditCommandFactory.Capture(node, crease ? "Mark Crease" : "Clear Crease", () =>
            {
                changed = session.SetCreaseOnSelection(crease);
                return changed > 0;
            }, onChanged: () => _renderer.Refresh());
            if (command is null)
            {
                Viewport.ShowMessage("Crease: select edges (two or more connected vertices) first.");
                return;
            }
            _commandHistory.Record(command);
            _renderer.Refresh();
            Viewport.ShowMessage(crease ? $"Creased {changed} edge(s) - sharp under Subdivision Surface" : $"Cleared crease on {changed} edge(s)");
        }

        /// <summary>Shift+S (Blender's snap menu): cursor and selection snapping.</summary>
        private void ShowSnapMenu()
        {
            var menu = new ContextMenu
            {
                PlacementTarget = Viewport,
                Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint,
            };
            menu.Items.Add(new MenuItem { Header = "Snap", IsEnabled = false, FontWeight = FontWeights.SemiBold });
            void Item(string header, Action action)
            {
                var item = new MenuItem { Header = header };
                item.Click += (_, _) => TryRun(header, action);
                menu.Items.Add(item);
            }
            Item("Selection to Cursor", SelectionToCursor);
            Item("Cursor to Selected", CursorToSelected);
            Item("Cursor to Active", () => { if (_sceneViewModel.SelectedNode?.UnderlyingNode is { } n) Viewport.CursorPosition = n.GetWorldPosition(); });
            Item("Cursor to World Origin", () => Viewport.CursorPosition = System.Numerics.Vector3.Zero);
            menu.IsOpen = true;
        }

        /// <summary>Moves the selected objects (object mode) or the selected vertices' centre
        /// (edit mode) onto the 3D cursor. Undoable.</summary>
        private void SelectionToCursor()
        {
            var cursor = Viewport.CursorPosition;
            if (IsEditMode)
            {
                var session = _renderer.EditSession;
                if (session.Target is not { Mesh: { } mesh } node || session.GetSelectionWorldCentroid() is not { } centroid) return;
                if (!System.Numerics.Matrix4x4.Invert(node.GetWorldTransform(), out var toLocal)) return;
                var delta = System.Numerics.Vector3.Transform(cursor, toLocal) - System.Numerics.Vector3.Transform(centroid, toLocal);
                var selected = session.SelectedVertexIndices.ToList();
                var command = MeshEditCommandFactory.Capture(node, "Selection to Cursor", () =>
                {
                    foreach (var i in selected) mesh.SetVertexPosition(i, mesh.Vertices[i].Position + delta);
                    return selected.Count > 0;
                }, onChanged: () => _renderer.Refresh());
                if (command is not null) _commandHistory.Record(command);
                _renderer.Refresh();
                return;
            }

            var commands = new List<IUndoableCommand>();
            foreach (var node in TopLevelSelection())
            {
                var before = (node.LocalPosition, node.LocalRotation, node.LocalScale);
                var parentWorld = node.Parent?.GetWorldTransform() ?? System.Numerics.Matrix4x4.Identity;
                if (!System.Numerics.Matrix4x4.Invert(parentWorld, out var toParent)) continue;
                node.LocalPosition = System.Numerics.Vector3.Transform(cursor, toParent);
                var after = (node.LocalPosition, node.LocalRotation, node.LocalScale);
                commands.Add(new TransformNodeCommand(node, before, after, "Selection to Cursor", onChanged: () =>
                {
                    _renderer.Refresh();
                    _gizmo.Refresh();
                    _sceneViewModel.FindViewModel(node)?.SyncFromCore();
                }));
                _sceneViewModel.FindViewModel(node)?.SyncFromCore();
            }
            if (commands.Count == 0) return;
            _commandHistory.Record(new CompositeCommand("Selection to Cursor", commands));
            _renderer.Refresh();
            _gizmo.Refresh();
        }

        /// <summary>Ctrl+R over an edge: cuts the ring of quads across it; moving the mouse
        /// slides the new loop along the ring, click confirms.</summary>
        private void BeginModalLoopCut()
        {
            var session = _renderer.EditSession;
            if (session.Target is not { Mesh: { } mesh } node || !Viewport.IsMouseOver) return;
            var mouse = Mouse.GetPosition(Viewport);
            if (ComponentHitTester.HitTestEdge(Viewport, node, mouse) is not { } edge)
            {
                Viewport.ShowMessage("Loop Cut: point at an edge, then press Ctrl+R.");
                return;
            }
            if (MeshTopologyOperators.LoopCut(mesh.Clone(), edge.A, edge.B) is null)
            {
                Viewport.ShowMessage("No ring of quads crosses this edge.");
                return;
            }

            var world = node.GetWorldTransform();
            var sa = Viewport.WorldToScreen(System.Numerics.Vector3.Transform(mesh.Vertices[edge.A].Position, world)) ?? mouse;
            var sb = Viewport.WorldToScreen(System.Numerics.Vector3.Transform(mesh.Vertices[edge.B].Position, world)) ?? mouse;
            var axis = sb - sa;
            _modalMeshOp = new ModalMeshOperation
            {
                Node = node,
                Original = mesh,
                Selection = session.SelectedVertexIndices.ToHashSet(),
                Name = "Loop Cut",
                Apply = (m, _, value) => MeshTopologyOperators.LoopCut(m, edge.A, edge.B, value),
                ValueFromMouse = p => axis.LengthSquared < 1 ? 0.5f
                    : (float)Math.Clamp(System.Windows.Vector.Multiply(p - sa, axis) / axis.LengthSquared, 0.02, 0.98),
                Describe = v => $"Loop Cut: {v:P0} along the edge",
            };
            UpdateModalMeshOperation(mouse);
        }

        private void UpdateModalMeshOperation(Point mouse)
        {
            if (_modalMeshOp is not { } op) return;
            var value = op.ValueFromMouse(mouse);
            var working = op.Original.Clone();
            var result = op.Apply(working, op.Selection, value);
            if (result is null) return;
            op.Node.Mesh = working;
            _renderer.EditSession.SetSelection(result);
            _renderer.Refresh();
            Viewport.ShowMessage($"{op.Describe(value)}   (click to confirm, Esc to cancel)");
        }

        private void ConfirmModalMeshOperation()
        {
            if (_modalMeshOp is not { } op) return;
            _modalMeshOp = null;
            var session = _renderer.EditSession;
            if (op.Node.Mesh is { } after && !ReferenceEquals(after, op.Original))
            {
                _commandHistory.Record(new MeshEditCommand(op.Node, op.Original, after, op.Name, onChanged: () =>
                {
                    _renderer.Refresh();
                    _componentGizmo.Attach(session);
                }));
            }
            _renderer.Refresh();
            _componentGizmo.Attach(session);
        }

        private void CancelModalMeshOperation()
        {
            if (_modalMeshOp is not { } op) return;
            _modalMeshOp = null;
            op.Node.Mesh = op.Original;
            _renderer.EditSession.SetSelection(op.Selection);
            _renderer.Refresh();
            _componentGizmo.Attach(_renderer.EditSession);
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
        /// <see cref="_commandHistory"/>, then re-renders (<c>viewport.Refresh</c>
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
            else DeleteSelectedObjects();
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
            else DuplicateSelectedObjects();
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
                return;
            }

            if (e.Key == Key.Escape && IsGizmoDragging)
            {
                CancelGizmoDrag();
                e.Handled = true;
                return;
            }

            if (HandleBlenderKeys(e))
            {
                e.Handled = true;
                return;
            }

            if (_renderer.HandleKey(e.Key, Keyboard.Modifiers))
            {
                e.Handled = true;
            }
        }

        /// <summary>The Modifiers panel's "Add Mirror"/"Add Subsurf" buttons - append a
        /// new, default-settings modifier to the selected node's own stack.
        /// <see cref="NodeViewModel.AddMirrorModifier"/>/<see cref="NodeViewModel.AddSubdivisionSurfaceModifier"/>
        /// already call this view model's own "something changed" callback themselves
        /// (the same one wired to <see cref="SceneViewModel.SceneChanged"/> in this
        /// window's own constructor), so no explicit <c>viewport.Refresh</c>
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

        private void AddShrinkwrapModifierButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            _sceneViewModel.SelectedNode?.AddShrinkwrapModifier();
        }

        private void AddCurveDeformModifierButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            _sceneViewModel.SelectedNode?.AddCurveDeformModifier();
        }

        private void AddWeightedNormalModifierButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            _sceneViewModel.SelectedNode?.AddWeightedNormalModifier();
        }

        private void AddBevelModifierButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            _sceneViewModel.SelectedNode?.AddBevelModifier();
        }

        /// <summary>Displace / Smooth / Triangulate / Weld / Simple Deform (button Tag = type name).</summary>
        private void AddParameterizedModifierButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized || sender is not FrameworkElement { Tag: string type }) return;
            if (Core.Modifiers.ParameterizedModifiers.Factories.TryGetValue(type, out var factory))
                _sceneViewModel.SelectedNode?.AddModifier(factory());
        }

        private void AddDecimateModifierButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            _sceneViewModel.SelectedNode?.AddDecimateModifier();
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

        private void AddShapeBasisButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            _sceneViewModel.SelectedNode?.AddShapeBasis();
            Viewport.ShowMessage("Basis stored. Now change the mesh (Edit Mode / Sculpt), then Save Shape Key.");
        }

        private void SaveShapeKeyButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            if (IsEditMode) SetEditorMode("Object"); // leave Edit Mode so the edit is committed
            if (_sceneViewModel.SelectedNode?.SaveShapeKey() != true)
                Viewport.ShowMessage("Nothing to save: change the mesh shape after Add Basis (same vertex count).");
            _renderer.Refresh();
        }

        private void RemoveShapeKeyButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            if (sender is FrameworkElement { DataContext: ShapeKeyViewModel row }) _sceneViewModel.SelectedNode?.RemoveShapeKey(row);
        }

        /// <summary>Keys the shape key's current value at the current frame (property F-curve "shape:Name").</summary>
        private void KeyShapeKeyButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized || sender is not FrameworkElement { DataContext: ShapeKeyViewModel row }) return;
            CommitPendingEdit();
            var node = row.Node;
            RecordKeyframeEdit(node, "Insert Shape Key Keyframe", () =>
                _timeline.GetOrCreateTrack(node).PropertyCurve(AnimationTrack.ShapeKeyPrefix + row.Name)
                    .Insert(_timeline.CurrentTime, row.Key.Value, InterpolationMode.Bezier));
            RefreshDopesheet();
            _graphEditor?.Redraw();
            Viewport.ShowMessage($"{row.Name} = {row.Key.Value:0.00} keyed at frame {_timeline.CurrentFrame:0}");
        }

        private void AddIKConstraintButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            _sceneViewModel.SelectedNode?.AddInverseKinematicsConstraint();
        }

        /// <summary>Copy / Limit constraints (button Tag = type name).</summary>
        private void AddParameterizedConstraintButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized || sender is not FrameworkElement { Tag: string type }) return;
            if (Core.Constraints.ParameterizedConstraints.Factories.TryGetValue(type, out var factory))
                _sceneViewModel.SelectedNode?.AddConstraint(factory());
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
        /// <summary>A modifier card's "Apply": bakes that modifier into the base mesh (as if it
        /// were first in the stack, like Blender) and removes it. Undoable.</summary>
        private void ApplyModifierButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized || IsEditMode) return;
            if (sender is not FrameworkElement { DataContext: ModifierViewModelBase modifierViewModel }) return;
            if (_sceneViewModel.SelectedNode?.UnderlyingNode is not { Mesh: { } mesh } node) return;
            var modifier = modifierViewModel.Underlying;
            TryRun("Apply Modifier", () =>
            {
                var before = NodeState.Capture(node);
                var result = modifier.Apply(mesh, node);
                if (ReferenceEquals(result, mesh)) result = mesh.Clone();
                result.Material ??= mesh.Material;
                node.Mesh = result;
                node.Modifiers.Remove(modifier);
                var after = NodeState.Capture(node, before);
                void Refresh()
                {
                    _sceneViewModel.FindViewModel(node)?.ReloadFromCore();
                    _renderer.Refresh();
                }
                _commandHistory.Record(new NodeStateCommand(node, before, after, "Apply " + modifier.Name, Refresh));
                Refresh();
                Viewport.ShowMessage($"Applied {modifier.Name}: {result.GetRenderFaces().Count():N0} triangles");
            });
        }

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
            RecordKeyframeEdit(node, "Insert Keyframe",
                () => _timeline.GetOrCreateTrack(node).AddKeyframeFromCurrentTransform(_timeline.CurrentTime, interpolation));
            RefreshDopesheet();
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
    }
}
