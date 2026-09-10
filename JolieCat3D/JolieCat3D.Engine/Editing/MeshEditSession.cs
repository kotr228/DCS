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
        /// updates too. A no-op with nothing selected or no <see cref="Target"/>.</summary>
        public void ApplyTranslation(Vector3 localDelta)
        {
            if (Target?.Mesh is not { } mesh || _selectedVertexIndices.Count == 0) return;

            foreach (var index in _selectedVertexIndices)
            {
                var current = mesh.Vertices[index].Position;
                mesh.SetVertexPosition(index, current + localDelta);
            }

            mesh.RecalculateNormals();
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
    }
}
