using System.Numerics;
using JolieCat3D.Core.Geometry;
using CoreNode = JolieCat3D.Core.Scene.Node;

namespace JolieCat3D.Engine.Editing
{
    /// <summary>
    /// Edit Mode's own per-target state: which <see cref="CoreNode"/> is being edited,
    /// which <see cref="ComponentType"/> mode is active, and which of its mesh's
    /// vertices are currently selected. Selection is stored as a single
    /// <see cref="SelectedVertexIndices"/> set no matter the active
    /// <see cref="ComponentMode"/> - selecting an edge or a face just adds every vertex
    /// index it touches to that same set, and (deliberately, matching how most modeling
    /// tools read a "select this edge/face" selection back out) an edge/face reads back
    /// as "selected" exactly when every one of its own vertices is in the set (see
    /// <see cref="IsEdgeSelected"/>/<see cref="IsFaceSelected"/>) - there is no separate
    /// edge-selection or face-selection set to keep in sync with the vertex one.
    /// </summary>
    public sealed class MeshEditSession
    {
        private readonly HashSet<int> _selectedVertexIndices = new();

        /// <summary>The node currently being edited - null when Edit Mode isn't active
        /// (or nothing is selected to edit), in which case there is nothing to select or
        /// draw markers for.</summary>
        public CoreNode? Target { get; private set; }

        /// <summary>Which component type a viewport click should resolve to - read by
        /// <c>JolieCat3D.UI</c> to route a click through the matching
        /// <see cref="ComponentHitTester"/> method.</summary>
        public ComponentType ComponentMode { get; set; } = ComponentType.Vertex;

        public IReadOnlySet<int> SelectedVertexIndices => _selectedVertexIndices;

        public bool HasSelection => _selectedVertexIndices.Count > 0;

        /// <summary>Off by default - when on, a Translate drag also nudges nearby
        /// UNSELECTED vertices (within <see cref="ProportionalRadius"/> of the selection)
        /// by a smoothly falling-off fraction of the same delta, the standard modeling-tool
        /// "Proportional Editing"/"Soft Selection" a broad, organic reshape (pulling up a
        /// hill on a terrain mesh, say) needs - dragging a single vertex with this off
        /// moves ONLY that vertex, leaving a hard crease in the surrounding surface.</summary>
        public bool ProportionalEditingEnabled { get; set; }

        /// <summary>The world-space (see <see cref="BeginProportionalDrag"/>'s own remarks
        /// on why world, not local) distance within which an unselected vertex falls under
        /// <see cref="ProportionalEditingEnabled"/>'s own falloff at all - beyond it, a
        /// vertex is completely unaffected, exactly as if proportional editing were off.
        /// 1 world unit by default - the same "primitives are authored at roughly this
        /// scale" reasoning <see cref="ComponentGizmo.GridSize"/>'s own remarks give for
        /// its own default.</summary>
        public float ProportionalRadius { get; set; } = 1f;

        /// <summary>Every vertex index this drag gesture's own falloff weight applies to
        /// (see <see cref="BeginProportionalDrag"/>), computed once at the start of the
        /// CURRENT drag - null whenever no drag is in progress, or
        /// <see cref="ProportionalEditingEnabled"/> is off.</summary>
        private Dictionary<int, float>? _proportionalWeights;

        /// <summary>Every vertex index the CURRENT drag gesture actually affects - the
        /// plain <see cref="SelectedVertexIndices"/> themselves whenever
        /// <see cref="ProportionalEditingEnabled"/> is off (or no drag is in progress),
        /// or every key of <see cref="_proportionalWeights"/> (the selection, PLUS every
        /// nearby unselected vertex the current drag's own falloff reaches) otherwise.
        /// <see cref="Editing.ComponentGizmo"/>'s own drag-start/drag-end snapshot uses
        /// this - not <see cref="SelectedVertexIndices"/> directly - to capture Undo/Redo
        /// state for EVERY vertex a proportional drag actually moves, not just the
        /// explicitly selected ones (missing this would silently break Undo: reverting
        /// only the selected vertices while leaving every proportionally-nudged one at its
        /// new, un-reverted position).</summary>
        public IReadOnlyCollection<int> AffectedVertexIndices =>
            (IReadOnlyCollection<int>?)_proportionalWeights?.Keys ?? _selectedVertexIndices;

        /// <summary>Switches which node this session edits (or detaches entirely, for
        /// null) and clears any existing selection - a set of vertex indices selected
        /// against one mesh is meaningless against another's.</summary>
        public void Attach(CoreNode? node)
        {
            Target = node;
            Clear();
        }

        public void Clear() => _selectedVertexIndices.Clear();

        /// <summary>Selects a single vertex - replacing the current selection unless
        /// <paramref name="additive"/> (e.g. a Shift-click) is set.</summary>
        public void SelectVertex(int index, bool additive = false)
        {
            if (!additive) Clear();
            _selectedVertexIndices.Add(index);
        }

        /// <summary>Adds or removes a single vertex from the current selection without
        /// touching the rest of it - a plain Shift-click toggle.</summary>
        public void ToggleVertex(int index)
        {
            if (!_selectedVertexIndices.Remove(index)) _selectedVertexIndices.Add(index);
        }

        /// <summary>Selects both of an edge's own endpoint vertices - replacing the
        /// current selection unless <paramref name="additive"/> is set.</summary>
        public void SelectEdge(int a, int b, bool additive = false)
        {
            if (!additive) Clear();
            _selectedVertexIndices.Add(a);
            _selectedVertexIndices.Add(b);
        }

        /// <summary>Selects every one of a face's own vertices - replacing the current
        /// selection unless <paramref name="additive"/> is set.</summary>
        public void SelectFace(IReadOnlyList<int> indices, bool additive = false)
        {
            ArgumentNullException.ThrowIfNull(indices);
            if (!additive) Clear();
            foreach (var index in indices) _selectedVertexIndices.Add(index);
        }

        public bool IsEdgeSelected(int a, int b) =>
            _selectedVertexIndices.Contains(a) && _selectedVertexIndices.Contains(b);

        public bool IsFaceSelected(IReadOnlyList<int> indices)
        {
            if (indices.Count == 0) return false;
            foreach (var index in indices)
                if (!_selectedVertexIndices.Contains(index)) return false;
            return true;
        }

        /// <summary>The average LOCAL-space position of every selected vertex,
        /// transformed to world space by <see cref="Target"/>'s own
        /// <see cref="CoreNode.GetWorldTransform"/> - where
        /// <see cref="ComponentGizmo"/> should be positioned. Null with nothing selected
        /// (or no <see cref="Target"/>).</summary>
        public Vector3? GetSelectionWorldCentroid()
        {
            if (Target?.Mesh is not { } mesh || _selectedVertexIndices.Count == 0) return null;

            var sum = Vector3.Zero;
            foreach (var index in _selectedVertexIndices) sum += mesh.Vertices[index].Position;
            var localCentroid = sum / _selectedVertexIndices.Count;

            return Vector3.Transform(localCentroid, Target.GetWorldTransform());
        }

        /// <summary>Moves every selected vertex by <paramref name="localDelta"/> (in the
        /// target mesh's own local space - see <see cref="ComponentGizmo"/>'s own
        /// remarks on converting a world-space drag into this) via
        /// <see cref="Mesh.SetVertexPosition"/>, then recalculates normals once for the
        /// whole mesh (not once per moved vertex) so the dragged region's shading
        /// updates too. A no-op with nothing selected or no <see cref="Target"/>.
        ///
        /// With a proportional drag in progress (see <see cref="BeginProportionalDrag"/>),
        /// every vertex it found - selected ones at full weight, nearby unselected ones
        /// scaled down by their own falloff weight - moves by <paramref name="localDelta"/>
        /// TIMES its own weight, not the same raw delta every selected vertex gets:
        /// exactly what turns a plain "move these vertices" drag into a smooth, organic
        /// reshape of the surrounding surface instead of a hard crease at the selection's
        /// own boundary.</summary>
        public void ApplyTranslation(Vector3 localDelta)
        {
            if (Target?.Mesh is not { } mesh || _selectedVertexIndices.Count == 0) return;

            if (_proportionalWeights is { } weights)
            {
                foreach (var (index, weight) in weights)
                {
                    var current = mesh.Vertices[index].Position;
                    mesh.SetVertexPosition(index, current + localDelta * weight);
                }
            }
            else
            {
                foreach (var index in _selectedVertexIndices)
                {
                    var current = mesh.Vertices[index].Position;
                    mesh.SetVertexPosition(index, current + localDelta);
                }
            }

            mesh.RecalculateNormals();
        }

        /// <summary>Computes (and caches, for the rest of the current drag gesture - see
        /// <see cref="_proportionalWeights"/>'s own remarks) which vertices a proportional
        /// drag actually affects and at what weight - every selected vertex at weight 1,
        /// plus every UNSELECTED vertex within <see cref="ProportionalRadius"/> of the
        /// NEAREST selected one, at a smooth falloff weight (1 at distance 0, smoothly
        /// down to 0 at exactly <see cref="ProportionalRadius"/> - the standard raised-
        /// cosine "Smooth" falloff curve most modeling tools offer as their own default,
        /// chosen here as the one curve rather than exposing several since it errs toward
        /// the least surprising, most universally reasonable-looking result for the widest
        /// range of drags without a per-drag curve choice this project was never asked
        /// for). Distance is measured in WORLD space (via <see cref="CoreNode.GetWorldTransform"/>),
        /// not local mesh space - a radius the user actually sees and sets in world units
        /// via a UI slider should mean the same real distance regardless of whether the
        /// target node happens to be scaled non-uniformly, not silently stretch/shrink
        /// with it. A no-op (no weights computed at all - <see cref="AffectedVertexIndices"/>
        /// then falls back to the plain selection) with <see cref="ProportionalEditingEnabled"/>
        /// off, nothing selected, or no <see cref="Target"/>. Call once at the START of a
        /// drag gesture (<see cref="Editing.ComponentGizmo"/>'s own GotMouseCapture) -
        /// recomputing every tick would make the falloff's own reference distances chase
        /// the vertices AS they move mid-drag, an incoherent, constantly-shifting result
        /// rather than one smooth, stable reshape.</summary>
        public void BeginProportionalDrag()
        {
            _proportionalWeights = null;
            if (!ProportionalEditingEnabled || Target?.Mesh is not { } mesh || _selectedVertexIndices.Count == 0) return;

            var world = Target.GetWorldTransform();
            var weights = new Dictionary<int, float>();
            var selectedWorldPositions = new List<Vector3>(_selectedVertexIndices.Count);

            foreach (var index in _selectedVertexIndices)
            {
                weights[index] = 1f;
                selectedWorldPositions.Add(Vector3.Transform(mesh.Vertices[index].Position, world));
            }

            for (var i = 0; i < mesh.Vertices.Count; i++)
            {
                if (weights.ContainsKey(i)) continue;

                var worldPosition = Vector3.Transform(mesh.Vertices[i].Position, world);
                var nearestDistance = float.MaxValue;
                foreach (var selectedWorldPosition in selectedWorldPositions)
                {
                    var distance = Vector3.Distance(worldPosition, selectedWorldPosition);
                    if (distance < nearestDistance) nearestDistance = distance;
                }

                if (nearestDistance >= ProportionalRadius) continue;
                weights[i] = ComputeFalloff(nearestDistance, ProportionalRadius);
            }

            _proportionalWeights = weights;
        }

        /// <summary>Clears whatever <see cref="BeginProportionalDrag"/> computed - call
        /// once the current drag gesture ends (<see cref="Editing.ComponentGizmo"/>'s own
        /// LostMouseCapture), so <see cref="AffectedVertexIndices"/> falls back to the
        /// plain selection again until the next drag's own <see cref="BeginProportionalDrag"/>
        /// call recomputes it fresh.</summary>
        public void EndProportionalDrag() => _proportionalWeights = null;

        /// <summary>The raised-cosine "Smooth" falloff curve - see
        /// <see cref="BeginProportionalDrag"/>'s own remarks on why this one curve. 1 at
        /// <paramref name="distance"/> 0, smoothly down to 0 at <paramref name="radius"/>
        /// (and clamped there for anything beyond it, rather than going negative).</summary>
        private static float ComputeFalloff(float distance, float radius)
        {
            var t = Math.Clamp(distance / radius, 0f, 1f);
            return (MathF.Cos(t * MathF.PI) + 1f) / 2f;
        }

        /// <summary>
        /// Deletes every currently selected vertex (see <see cref="Mesh.RemoveVertices"/>'s
        /// own remarks on what that takes with it: any face/polygon touching a deleted
        /// vertex, plus a full renumbering of what's left) - Edit Mode's own "Delete"
        /// command, and (per this class's own remarks on how an Edge/Face selection is
        /// ALWAYS represented as the set of vertices it touches) the exact same single
        /// operation regardless of whether <see cref="ComponentMode"/> is currently Vertex,
        /// Edge, or Face: "delete this edge" already means "delete both its endpoint
        /// vertices" here, exactly like "delete this face" already means "delete every one
        /// of its own vertices" - there is no separate "delete just this edge, keep its
        /// vertices" (dissolve) concept in this selection model at all. Clears the
        /// selection afterward (every one of its own indices is now either gone or
        /// meaningless against the shifted vertex numbering). Returns false (a no-op, mesh
        /// untouched) if nothing is selected, or there is no <see cref="Target"/> at all.
        /// </summary>
        public bool DeleteSelected()
        {
            if (Target?.Mesh is not { } mesh || _selectedVertexIndices.Count == 0) return false;

            mesh.RemoveVertices(_selectedVertexIndices);
            Clear();
            return true;
        }

        /// <summary>
        /// Extrudes the currently fully-selected face outward by <paramref name="distance"/>
        /// along its own normal (see <see cref="Mesh.ExtrudeFace"/>), replacing the
        /// current selection with the freshly-created cap face's own vertices - the
        /// modeling-tool convention of an Extrude leaving the new face selected, ready
        /// for a further drag (via <see cref="ComponentGizmo"/>) or another Extrude.
        /// Only ever finds a match among <see cref="Mesh.Polygons"/> (the first one
        /// whose every vertex is selected, per <see cref="IsFaceSelected"/>) - a
        /// <see cref="Mesh.Faces"/> triangle isn't itself a <see cref="Polygon"/>
        /// <see cref="Mesh.ExtrudeFace"/> could remove/replace, so an all-triangle mesh
        /// (an STL import, or the result of <see cref="SubdivideMesh"/>) has nothing
        /// this can extrude - a real, disclosed limitation of <see cref="Mesh.ExtrudeFace"/>
        /// itself, not something this method works around. Returns false (a no-op) with
        /// no fully-selected polygon face found, or no <see cref="Target"/> at all.
        /// </summary>
        public bool ExtrudeSelectedFace(float distance)
        {
            if (Target?.Mesh is not { } mesh) return false;

            var selectedPolygon = FindFullySelectedPolygon(mesh);
            if (selectedPolygon is null) return false;

            var cap = mesh.ExtrudeFace(selectedPolygon, distance);
            SelectFace(cap.Indices);
            return true;
        }

        private Polygon? FindFullySelectedPolygon(Mesh mesh)
        {
            foreach (var polygon in mesh.Polygons)
                if (IsFaceSelected(polygon.Indices)) return polygon;
            return null;
        }

        /// <summary>
        /// Duplicates every currently-selected vertex/edge/face (see
        /// <see cref="Mesh.DuplicateVertices"/> for exactly what "duplicate" means for a
        /// partially- vs. fully-selected face) and replaces the current selection with the
        /// newly duplicated vertices - Edit Mode's own "Duplicate Selection" (Blender's
        /// Shift+D): the freshly duplicated geometry sits exactly on top of the original
        /// until dragged, immediately ready to move via <see cref="ComponentGizmo"/>/
        /// <see cref="ApplyTranslation"/>, the same "leave the result selected, ready for a
        /// further drag" convention <see cref="ExtrudeSelectedFace"/> already follows.
        /// Returns false (a no-op) with nothing selected, or no <see cref="Target"/> at all.
        /// </summary>
        public bool DuplicateSelected()
        {
            if (Target?.Mesh is not { } mesh || _selectedVertexIndices.Count == 0) return false;

            var newIndices = mesh.DuplicateVertices(_selectedVertexIndices);
            if (newIndices.Count == 0) return false;

            _selectedVertexIndices.Clear();
            foreach (var index in newIndices) _selectedVertexIndices.Add(index);

            return true;
        }

        /// <summary>
        /// Subdivides the WHOLE target mesh (see <see cref="Mesh.Subdivide"/>'s own
        /// remarks on why this is never a partial/selection-scoped operation in
        /// <c>JolieCat3D.Core</c> itself - there is no concept of "subdivide just this
        /// selected face" there) and clears the current selection. The mesh's topology
        /// (which faces/polygons exist at all) has completely changed - a selection made
        /// against the PRE-subdivide face layout has no single coherent "face" left to
        /// refer to (each original face is now several smaller ones), even though the
        /// original vertices themselves are untouched and still present at the same
        /// indices (<see cref="Mesh.Subdivide"/> only ever appends new vertices, never
        /// removes or reorders existing ones). Returns false (a no-op) with no
        /// <see cref="Target"/> at all.
        /// </summary>
        public bool SubdivideMesh()
        {
            if (Target?.Mesh is not { } mesh) return false;

            mesh.Subdivide();
            Clear();
            return true;
        }

        /// <summary>
        /// Loop Cut's own selection-aware entry point - requires the current selection
        /// to be EXACTLY one edge (two selected vertices - see <see cref="Mesh.LoopCut"/>'s
        /// own remarks for what "edge" means there: it must belong to at least one
        /// 4-sided <see cref="Polygon"/>), and replaces the selection with every new
        /// vertex the cut just inserted (the whole freshly-created edge loop) -
        /// immediately ready for a further drag, the same "leave the operation's own
        /// result selected" convention <see cref="ExtrudeSelectedFace"/> already follows.
        /// Returns false (a no-op, mesh untouched) with no <see cref="Target"/>, a
        /// selection that isn't exactly 2 vertices, or an edge <see cref="Mesh.LoopCut"/>
        /// itself couldn't cut (not part of any quad, or reaches a topology it doesn't
        /// support).
        /// </summary>
        public bool LoopCutSelectedEdge()
        {
            if (Target?.Mesh is not { } mesh) return false;
            if (_selectedVertexIndices.Count != 2) return false;

            var edge = _selectedVertexIndices.ToArray();
            var newPolygons = mesh.LoopCut(edge[0], edge[1]);
            if (newPolygons.Count == 0) return false;

            Clear();
            foreach (var polygon in newPolygons)
                foreach (var index in polygon.Indices)
                    _selectedVertexIndices.Add(index);

            return true;
        }

        /// <summary>
        /// Bevel's own selection-aware entry point - requires the current selection to
        /// be EXACTLY one vertex (see <see cref="Mesh.BevelVertex"/>'s own remarks for
        /// exactly which vertices are supported: a closed, manifold fan of
        /// <see cref="Polygon"/>s around it), and replaces the selection with the new
        /// cap face's own vertices - same "leave the result selected" convention as
        /// <see cref="LoopCutSelectedEdge"/>/<see cref="ExtrudeSelectedFace"/>. Returns
        /// false (a no-op) with no <see cref="Target"/>, a selection that isn't exactly
        /// 1 vertex, or a vertex <see cref="Mesh.BevelVertex"/> itself couldn't bevel.
        /// </summary>
        public bool BevelSelectedVertex(float amount)
        {
            if (Target?.Mesh is not { } mesh) return false;
            if (_selectedVertexIndices.Count != 1) return false;

            var vertexIndex = _selectedVertexIndices.Single();
            var cap = mesh.BevelVertex(vertexIndex, amount);
            if (cap is null) return false;

            SelectFace(cap.Indices);
            return true;
        }

        /// <summary>
        /// Multi-Material Support's own "Assign to Selection" - finds the single
        /// <see cref="Mesh.Polygons"/> entry that's currently fully selected (Face mode -
        /// see <see cref="ExtrudeSelectedFace"/>'s own matching helper) and points its
        /// own <see cref="Polygon.MaterialSlotIndex"/> at <paramref name="slotIndex"/>
        /// (or resets it to -1/"use the mesh's own plain Material" for a negative
        /// <paramref name="slotIndex"/>). Returns false (a no-op) with no
        /// <see cref="Target"/>, or no single fully-selected <see cref="Polygon"/> found -
        /// the exact same "a triangulated <see cref="Mesh.Faces"/> entry isn't itself a
        /// <see cref="Polygon"/>" scope <see cref="ExtrudeSelectedFace"/> already
        /// discloses.
        /// </summary>
        public bool AssignMaterialSlotToSelectedFace(int slotIndex)
        {
            if (Target?.Mesh is not { } mesh) return false;

            var selectedPolygon = FindFullySelectedPolygon(mesh);
            if (selectedPolygon is null) return false;

            selectedPolygon.MaterialSlotIndex = slotIndex;
            return true;
        }
    }
}
