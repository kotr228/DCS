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
    /// <summary>Edge loops, bridge, knife, inset, bevel, spin/screw and other modal mesh tools.</summary>
    public partial class MainWindow
    {
        // ================= Edge loops, bridge, knife, interactive inset =================

        /// <summary>Alt+click (Shift adds): selects the edge loop under the cursor.</summary>
        private bool TrySelectEdgeLoop(Point position)
        {
            var session = _renderer.EditSession;
            if (session.Target is not { Mesh: { } mesh } node) return false;
            if (ComponentHitTester.HitTestEdge(Viewport, node, position) is not { } edge) return false;
            var loop = MeshTopologyOperators.SelectEdgeLoop(mesh, edge.A, edge.B);
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) session.ModifySelection(loop, subtract: false);
            else session.SetSelection(loop);
            _renderer.RefreshComponentOverlay();
            _componentGizmo.Attach(session);
            return true;
        }

        private void BridgeEdgeLoops() =>
            RunEditOperator("Bridge Edge Loops", (m, sel) => MeshTopologyOperators.BridgeEdgeLoops(m, sel),
                "Select two open boundary loops with the same number of vertices.");

        private void BridgeMenuItem_Click(object sender, RoutedEventArgs e) { if (_isInitialized && IsEditMode) BridgeEdgeLoops(); }
        private void KnifeMenuItem_Click(object sender, RoutedEventArgs e) { if (_isInitialized && IsEditMode) BeginKnife(); }

        // ---- Knife (K): click the start, click the end; Esc / right click cancels ----

        private bool _isKnifeActive;
        private Point? _knifeStart;

        private void BeginKnife()
        {
            if (_renderer.EditSession.Target is null) return;
            _isKnifeActive = true;
            _knifeStart = null;
            Viewport.ShowMessage("Knife: click the start and end of the cut (Esc cancels)");
        }

        private void KnifeClick(Point position)
        {
            if (_knifeStart is not { } start)
            {
                _knifeStart = position;
                return;
            }

            EndKnife();
            if ((position - start).Length < 3) return;
            var session = _renderer.EditSession;
            if (session.Target is not { } node) return;

            var ray0 = Viewport.ScreenToRay(start);
            var ray1 = Viewport.ScreenToRay(position);
            var a = ray0.Origin;
            var b = ray0.Origin + ray0.Direction;
            var c = ray1.Origin + ray1.Direction;
            var normal = System.Numerics.Vector3.Cross(b - a, c - a);
            var segment = position - start;

            bool WithinCut(System.Numerics.Vector3 hit)
            {
                if (Viewport.WorldToScreen(hit) is not { } screen) return false;
                var t = System.Windows.Vector.Multiply(screen - start, segment) / segment.LengthSquared;
                return t >= -0.01 && t <= 1.01;
            }

            RunEditOperator("Knife", (m, _) => MeshTopologyOperators.KnifeCut(m, node.GetWorldTransform(), a, normal, WithinCut),
                "The cut line didn't cross any edge.");
        }

        private void EndKnife()
        {
            _isKnifeActive = false;
            _knifeStart = null;
            Viewport.SetGuideLine(null, null);
        }

        // ---- Interactive inset (I): mouse distance to the faces sets the thickness ----

        private sealed class ModalMeshOperation
        {
            public required Node Node { get; init; }
            public required Mesh Original { get; init; }
            public required HashSet<int> Selection { get; init; }
            public required string Name { get; init; }
            public required Func<Mesh, IReadOnlySet<int>, float, IReadOnlySet<int>?> Apply { get; init; }
            public required Func<Point, float> ValueFromMouse { get; init; }
            public required Func<float, string> Describe { get; init; }
            /// <summary>Mouse wheel during the operation (e.g. bevel segments); null = ignored.</summary>
            public Action<int>? OnWheel { get; init; }
        }

        private ModalMeshOperation? _modalMeshOp;

        private void BeginModalInset()
        {
            var session = _renderer.EditSession;
            if (session.Target is not { Mesh: { } mesh } node || !Viewport.IsMouseOver) { InsetFaces(); return; }
            var selection = session.SelectedVertexIndices.ToHashSet();
            if (MeshOperators.InsetFaces(mesh.Clone(), selection, 0.2f) is null)
            {
                Viewport.ShowMessage("Select one or more faces to inset.");
                return;
            }

            var centroid = session.GetSelectionWorldCentroid() ?? node.GetWorldPosition();
            var center = Viewport.WorldToScreen(centroid) ?? Mouse.GetPosition(Viewport);
            var mouse = Mouse.GetPosition(Viewport);
            var startDistance = Math.Max(30, (mouse - center).Length);
            _modalMeshOp = new ModalMeshOperation
            {
                Node = node,
                Original = mesh,
                Selection = selection,
                Name = "Inset Faces",
                Apply = (m, sel, value) => MeshOperators.InsetFaces(m, sel, value),
                // Moving towards the faces' centre makes the inset deeper (as in Blender).
                ValueFromMouse = p => (float)Math.Clamp(1 - 0.8 * (p - center).Length / startDistance, 0.01, 0.99),
                Describe = v => $"Inset: {v:P0}",
            };
            UpdateModalMeshOperation(mouse);
        }

        /// <summary>Ctrl+B: bevels the selected edges; the distance of the mouse from the
        /// selection sets the width (Blender behaviour), click confirms.</summary>
        private void BeginModalBevel()
        {
            var session = _renderer.EditSession;
            if (session.Target is not { Mesh: { } mesh } node || !Viewport.IsMouseOver) return;
            var selection = session.SelectedVertexIndices.ToHashSet();
            if (MeshBevel.BevelEdges(mesh.Clone(), selection, 0.01f) is null)
            {
                Viewport.ShowMessage("Bevel: select edges shared by two faces (e.g. Edge mode, or faces).");
                return;
            }

            var centroid = session.GetSelectionWorldCentroid() ?? node.GetWorldPosition();
            var center = Viewport.WorldToScreen(centroid) ?? Mouse.GetPosition(Viewport);
            var unitsPerPixel = Viewport.WorldUnitsPerPixel(centroid);
            // Width in the mesh's own units, so a scaled object bevels by what you see.
            var worldScale = System.Numerics.Vector3.TransformNormal(System.Numerics.Vector3.UnitX, node.GetWorldTransform()).Length();
            if (worldScale < 1e-6f) worldScale = 1f;
            var segments = _lastBevelSegments;
            _modalMeshOp = new ModalMeshOperation
            {
                Node = node,
                Original = mesh,
                Selection = selection,
                Name = "Bevel",
                Apply = (m, sel, value) => MeshBevel.BevelEdges(m, sel, value, segments),
                ValueFromMouse = p => (float)Math.Max(0.0005, (p - center).Length * unitsPerPixel * 0.35 / worldScale),
                Describe = v => $"Bevel width: {v:0.###}   segments: {segments} (mouse wheel)",
                OnWheel = d => _lastBevelSegments = segments = Math.Clamp(segments + d, 1, 32),
            };
            UpdateModalMeshOperation(Mouse.GetPosition(Viewport));
        }

        private int _lastBevelSegments = 1;

        private void BevelMenuItem_Click(object sender, RoutedEventArgs e) { if (_isInitialized && IsEditMode) BeginModalBevel(); }

        // ================= Spin / Screw / Extrude Along Normals =================

        /// <summary>Converts a world-space point and direction into the edited mesh's local space.</summary>
        private static (System.Numerics.Vector3 Point, System.Numerics.Vector3 Direction) ToMeshLocal(Node node, System.Numerics.Vector3 point, System.Numerics.Vector3 direction)
        {
            if (!System.Numerics.Matrix4x4.Invert(node.GetWorldTransform(), out var inv)) return (point, direction);
            return (System.Numerics.Vector3.Transform(point, inv), System.Numerics.Vector3.TransformNormal(direction, inv));
        }

        private bool CanSweep(out Node node, out Mesh mesh, out HashSet<int> selection)
        {
            node = null!; mesh = null!; selection = null!;
            var session = _renderer.EditSession;
            if (session.Target is not { Mesh: { } m } n || !Viewport.IsMouseOver) return false;
            var sel = session.SelectedVertexIndices.ToHashSet();
            if (MeshOperators.ExtrudeRegion(m.Clone(), sel, 0f) is null)
            {
                Viewport.ShowMessage("Select one or more faces first.");
                return false;
            }
            node = n; mesh = m; selection = sel;
            return true;
        }

        /// <summary>Alt+R: spins the selected faces around the 3D cursor about the view axis;
        /// the mouse turning around the cursor sets the angle (Ctrl snaps to 15°).</summary>
        private void BeginModalSpin()
        {
            if (!CanSweep(out var node, out var mesh, out var selection)) return;
            var cursor = Viewport.CursorPosition;
            var (center, axis) = ToMeshLocal(node, cursor, Viewport.Camera.Forward);
            var c = Viewport.WorldToScreen(cursor) ?? Mouse.GetPosition(Viewport);
            var start = Mouse.GetPosition(Viewport);
            var previous = Math.Atan2(start.Y - c.Y, start.X - c.X);
            var accumulated = 0.0;
            _modalMeshOp = new ModalMeshOperation
            {
                Node = node,
                Original = mesh,
                Selection = selection,
                Name = "Spin",
                Apply = (m, sel, degrees) => MeshSweepOperators.Spin(m, sel, center, axis, degrees, 12),
                ValueFromMouse = p =>
                {
                    var a = Math.Atan2(p.Y - c.Y, p.X - c.X);
                    var d = a - previous;
                    if (d > Math.PI) d -= 2 * Math.PI;
                    if (d < -Math.PI) d += 2 * Math.PI;
                    previous = a;
                    accumulated += d;
                    var degrees = accumulated * 180 / Math.PI;
                    if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) degrees = Math.Round(degrees / 15) * 15;
                    return (float)Math.Clamp(degrees, -360, 360);
                },
                Describe = v => $"Spin: {v:0}°  (12 steps, around the 3D cursor)",
            };
            UpdateModalMeshOperation(start);
        }

        /// <summary>Screw: two turns around the 3D cursor's view axis; moving the mouse up/down sets the height.</summary>
        private void BeginModalScrew()
        {
            if (!CanSweep(out var node, out var mesh, out var selection)) return;
            var cursor = Viewport.CursorPosition;
            var (center, axis) = ToMeshLocal(node, cursor, Viewport.Camera.Up);
            var start = Mouse.GetPosition(Viewport);
            var unitsPerPixel = Viewport.WorldUnitsPerPixel(cursor);
            const int steps = 24;
            _modalMeshOp = new ModalMeshOperation
            {
                Node = node,
                Original = mesh,
                Selection = selection,
                Name = "Screw",
                Apply = (m, sel, height) => MeshSweepOperators.Spin(m, sel, center, axis, 720f, steps, height / steps),
                ValueFromMouse = p => (float)((start.Y - p.Y) * unitsPerPixel + 1.0),
                Describe = v => $"Screw: height {v:0.##}  (2 turns)",
            };
            UpdateModalMeshOperation(start);
        }

        /// <summary>Alt+E: extrude the faces along their normals; mouse up/down sets the distance.</summary>
        private void BeginModalExtrudeAlongNormals()
        {
            if (!CanSweep(out var node, out var mesh, out var selection)) return;
            var start = Mouse.GetPosition(Viewport);
            var centroid = _renderer.EditSession.GetSelectionWorldCentroid() ?? node.GetWorldPosition();
            var unitsPerPixel = Viewport.WorldUnitsPerPixel(centroid);
            var worldScale = System.Numerics.Vector3.TransformNormal(System.Numerics.Vector3.UnitX, node.GetWorldTransform()).Length();
            if (worldScale < 1e-6f) worldScale = 1f;
            _modalMeshOp = new ModalMeshOperation
            {
                Node = node,
                Original = mesh,
                Selection = selection,
                Name = "Extrude Along Normals",
                Apply = (m, sel, distance) => MeshSweepOperators.ExtrudeAlongNormals(m, sel, distance),
                ValueFromMouse = p => (float)((start.Y - p.Y) * unitsPerPixel / worldScale),
                Describe = v => $"Extrude along normals: {v:0.###}",
            };
            UpdateModalMeshOperation(start);
        }

        private void SpinMenuItem_Click(object sender, RoutedEventArgs e) { if (_isInitialized && IsEditMode) BeginModalSpin(); }
        private void ScrewMenuItem_Click(object sender, RoutedEventArgs e) { if (_isInitialized && IsEditMode) BeginModalScrew(); }
        private void ExtrudeNormalsMenuItem_Click(object sender, RoutedEventArgs e) { if (_isInitialized && IsEditMode) BeginModalExtrudeAlongNormals(); }

        private void CursorToOriginMenuItem_Click(object sender, RoutedEventArgs e) { if (_isInitialized) ResetCursorAndFrame(); }
    }
}
