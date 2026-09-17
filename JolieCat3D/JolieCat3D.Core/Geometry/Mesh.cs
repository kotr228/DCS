using System.Numerics;
using JolieCat3D.Core.Materials;
using JolieCat3D.Core.Numerics;

namespace JolieCat3D.Core.Geometry
{
    /// <summary>
    /// A named collection of <see cref="Vertex"/> plus the <see cref="Face"/> triangles
    /// (and/or authoring-time <see cref="Polygon"/> n-gons) connecting them, with an
    /// optional <see cref="Material"/> reference for how it should be shaded. The
    /// platform-agnostic 3D geometry type this whole project is built around -
    /// <c>JolieCat3D.Engine</c>'s renderer reads a Mesh and produces a WPF
    /// <c>MeshGeometry3D</c> from it; nothing in here references WPF, Windows, or any
    /// rendering API at all, so a Mesh can be built, loaded, or edited (in
    /// <c>JolieCat3D.Core</c> or <c>JolieCat3D.Service</c>) with no GPU or UI framework
    /// in the process at all.
    /// </summary>
    public sealed class Mesh
    {
        private readonly List<Vertex> _vertices = new();
        private readonly List<Face> _faces = new();
        private readonly List<Polygon> _polygons = new();
        private readonly List<Material> _materialSlots = new();

        public string Name { get; set; }

        /// <summary>Optional - a mesh with no material renders with <see cref="Material.CreateDefault"/> instead.
        /// The material every <see cref="Polygon"/> renders with UNLESS it has its own
        /// <see cref="Polygon.MaterialSlotIndex"/> pointing into <see cref="MaterialSlots"/>
        /// (see that property's own remarks) - this one is never a "slot" itself, only
        /// ever the plain fallback, matching every mesh authored before Multi-Material
        /// Support existed.</summary>
        public Material? Material { get; set; }

        /// <summary>Additional materials, beyond the single plain <see cref="Material"/>,
        /// a <see cref="Polygon"/> can opt into via its own <see cref="Polygon.MaterialSlotIndex"/> -
        /// "Multi-Material Support"/Sub-mesh Materials. Empty by default (matching every
        /// mesh authored before this existed - nothing here changes how such a mesh
        /// renders at all, since no polygon has a non-default <see cref="Polygon.MaterialSlotIndex"/>
        /// to reference one anyway). <c>JolieCat3D.Engine.Geometry.MeshGeometryFactory</c>
        /// groups a mesh's own triangles by their owning polygon's slot (or the plain
        /// <see cref="Material"/>, for anything with no override) into one
        /// <c>MeshGeometry3D</c>/<c>GeometryModel3D</c> pair per DISTINCT material
        /// actually used - the "upgrade the rendering pipeline to support multiple
        /// Materials on a single mesh" half of the task.
        ///
        /// A known, disclosed scope limit: slots are NOT yet round-tripped through
        /// <c>Service.Project.Jolie3DProjectSerializer</c> (project save/load) or any
        /// exporter (glTF/OBJ) - a live, in-session/viewport feature for now, the same
        /// "ship the core feature, disclose what isn't wired up yet" precedent
        /// <c>Modifiers.BooleanModifier</c> already established for the exact same
        /// reason (project serialization is a large, separate surface, not currently
        /// reachable from any UI save/load action either).</summary>
        public IReadOnlyList<Material> MaterialSlots => _materialSlots;

        public IReadOnlyList<Vertex> Vertices => _vertices;

        /// <summary>Ready-to-render triangles, referencing <see cref="Vertices"/> by index.</summary>
        public IReadOnlyList<Face> Faces => _faces;

        /// <summary>Authoring-time n-sided faces (see <see cref="Polygon"/>'s own remarks)
        /// - not rendered directly; <see cref="GetRenderFaces"/> triangulates them
        /// alongside <see cref="Faces"/> for anything that actually needs triangles.</summary>
        public IReadOnlyList<Polygon> Polygons => _polygons;

        public Mesh(string name = "Mesh") => Name = name;

        public int AddVertex(Vertex vertex)
        {
            _vertices.Add(vertex);
            return _vertices.Count - 1;
        }

        public void AddFace(Face face) => _faces.Add(face);

        public void AddTriangle(int a, int b, int c) => AddFace(new Face(a, b, c));

        /// <summary>Adds a quad as a <see cref="Polygon"/> (not two immediately-split
        /// triangles) so it round-trips as one 4-sided face for anything downstream that
        /// wants to reason about the quad itself, not its arbitrary triangulation -
        /// <see cref="GetRenderFaces"/> still triangulates it for actual rendering.</summary>
        public void AddQuad(int a, int b, int c, int d) => _polygons.Add(new Polygon(a, b, c, d));

        public void AddPolygon(Polygon polygon)
        {
            ArgumentNullException.ThrowIfNull(polygon);
            _polygons.Add(polygon);
        }

        /// <summary>A complete, fully independent copy - every <see cref="Vertex"/>/
        /// <see cref="Face"/> (both plain structs, copied by value automatically) plus a
        /// brand new <see cref="Polygon"/> instance per entry in <see cref="Polygons"/>
        /// (a <see cref="Polygon"/> is a reference type wrapping its own mutable-looking
        /// index list, so sharing the same instances would let an edit to the clone's
        /// polygon list somehow reach back into this mesh's own, or vice versa - the same
        /// "new Polygon(polygon.Indices)" copy <see cref="Modifiers.MirrorModifier.Apply"/>
        /// already uses for exactly this reason). <see cref="Material"/> itself is copied
        /// by REFERENCE, not deep-cloned - the same "materials are shared, not owned
        /// per-mesh" assumption every other part of this project already makes (see e.g.
        /// <c>Service.Animation.AnimationTimeline.TextureTracks</c>, keyed by the actual
        /// <see cref="Material"/> instance). The one caller today
        /// (<c>Service.Commands.MeshEditCommandFactory</c>) uses this to capture an
        /// Undo/Redo snapshot BEFORE a structural edit (Extrude/Subdivide) that mutates a
        /// mesh in place - a full independent copy is what makes "restore the exact
        /// pre-edit geometry" possible without <see cref="Mesh"/> needing any kind of
        /// "undo log"/inverse-operation of its own.</summary>
        public Mesh Clone()
        {
            var clone = new Mesh(Name) { Material = Material };

            foreach (var vertex in _vertices) clone.AddVertex(vertex);
            foreach (var face in _faces) clone.AddFace(face);
            foreach (var polygon in _polygons)
                clone.AddPolygon(new Polygon(polygon.Indices) { MaterialSlotIndex = polygon.MaterialSlotIndex });
            foreach (var material in _materialSlots) clone._materialSlots.Add(material);

            return clone;
        }

        /// <summary>Adds <paramref name="material"/> as a new <see cref="MaterialSlots"/>
        /// entry and returns its own index - what a <see cref="Polygon.MaterialSlotIndex"/>
        /// then references to render with it instead of this mesh's own plain
        /// <see cref="Material"/>.</summary>
        public int AddMaterialSlot(Material material)
        {
            ArgumentNullException.ThrowIfNull(material);
            _materialSlots.Add(material);
            return _materialSlots.Count - 1;
        }

        /// <summary>Removes the <see cref="MaterialSlots"/> entry at
        /// <paramref name="slotIndex"/> - every <see cref="Polygon"/> that referenced it
        /// falls back to this mesh's own plain <see cref="Material"/> (its own
        /// <see cref="Polygon.MaterialSlotIndex"/> reset to -1), and every polygon
        /// referencing a LATER slot has its own index shifted down by one to stay
        /// pointing at the same actual material after the removed entry shifts the rest
        /// of the list down - the same "positional index, re-numbered on removal"
        /// convention <see cref="RemoveVertices"/> already established for vertex
        /// indices. A no-op for an out-of-range <paramref name="slotIndex"/>.</summary>
        public void RemoveMaterialSlot(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= _materialSlots.Count) return;

            _materialSlots.RemoveAt(slotIndex);

            foreach (var polygon in _polygons)
            {
                if (polygon.MaterialSlotIndex == slotIndex) polygon.MaterialSlotIndex = -1;
                else if (polygon.MaterialSlotIndex > slotIndex) polygon.MaterialSlotIndex--;
            }
        }

        /// <summary>The material <paramref name="polygon"/> should actually render with -
        /// whichever <see cref="MaterialSlots"/> entry its own <see cref="Polygon.MaterialSlotIndex"/>
        /// points to, if that index is currently valid, or this mesh's own plain
        /// <see cref="Material"/> otherwise (no override at all, or a stale index left
        /// over from a since-<see cref="RemoveMaterialSlot"/>-removed slot this
        /// particular call site hasn't reset yet).</summary>
        public Material? GetEffectiveMaterial(Polygon polygon)
        {
            ArgumentNullException.ThrowIfNull(polygon);
            var slotIndex = polygon.MaterialSlotIndex;
            return slotIndex >= 0 && slotIndex < _materialSlots.Count ? _materialSlots[slotIndex] : Material;
        }

        /// <summary>Replaces the position of the vertex at <paramref name="index"/> in
        /// place, keeping its existing normal/UV/color - the one way to move an
        /// existing vertex without re-adding it (see <see cref="Vertex"/>'s own remarks
        /// on why it's otherwise an immutable struct). Used by
        /// <c>JolieCat3D.Engine.Editing.MeshEditSession</c> to drag selected vertices in
        /// Edit Mode. Deliberately does not recalculate normals itself - a caller moving
        /// several vertices in one drag should call <see cref="RecalculateNormals"/>
        /// once afterward, not once per vertex.</summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is not a valid vertex index.</exception>
        public void SetVertexPosition(int index, Vector3 position)
        {
            if (index < 0 || index >= _vertices.Count)
                throw new ArgumentOutOfRangeException(nameof(index));

            _vertices[index] = _vertices[index].WithPosition(position);
        }

        /// <summary>Replaces the texture coordinate of the vertex at
        /// <paramref name="index"/> in place, keeping its existing position/normal/color -
        /// the mutation <see cref="UVProjector"/> uses to (re)map every vertex's UV from
        /// its own position/normal, the same way <see cref="SetVertexPosition"/> is the
        /// mutation Edit Mode uses for position.</summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is not a valid vertex index.</exception>
        public void SetVertexUV(int index, Vector2 uv)
        {
            if (index < 0 || index >= _vertices.Count)
                throw new ArgumentOutOfRangeException(nameof(index));

            _vertices[index] = _vertices[index].WithUV(uv);
        }

        /// <summary>
        /// Removes every vertex index in <paramref name="indices"/> from this mesh, along
        /// with any <see cref="Face"/>/<see cref="Polygon"/> that references ANY of them -
        /// a face missing even one of its own corners isn't a valid face anymore, the same
        /// "delete this vertex, and everything touching it goes too" convention most
        /// modeling tools use for a bare "Delete Vertices" command (as opposed to a
        /// topology-preserving "Dissolve", which this is not). Every SURVIVING face/polygon
        /// is then re-indexed to account for the gap the removal leaves in
        /// <see cref="Vertices"/> - indices are positional, so deleting vertex 3 shifts
        /// every vertex after it down by one - and normals are recalculated for what's left
        /// (a deleted neighboring face can change a surviving vertex's own smooth-shaded
        /// normal, e.g. an edge that used to be interior becoming a boundary).
        ///
        /// Existing UVs on every SURVIVING vertex are left completely untouched - unlike
        /// <see cref="ExtrudeFace"/>/<see cref="Subdivide"/> (which invent brand new
        /// vertices with only an interim, immediately-stale UV that a full
        /// <see cref="UVProjector"/> re-projection is needed to fix up), deleting never
        /// creates new geometry, so there is nothing here that actually needs a fresh UV
        /// projection - doing one anyway would needlessly discard a hand-authored or
        /// imported UV layout this operation never even touched.
        ///
        /// A no-op if <paramref name="indices"/> contains nothing currently valid (already
        /// out of range, or the mesh is empty) - <see cref="RecalculateNormals"/> is not
        /// even called in that case, so calling this with an empty/invalid selection is
        /// always safe and never mutates the mesh.
        /// </summary>
        public void RemoveVertices(IEnumerable<int> indices)
        {
            ArgumentNullException.ThrowIfNull(indices);

            var toRemove = new HashSet<int>();
            foreach (var index in indices)
                if (index >= 0 && index < _vertices.Count) toRemove.Add(index);

            if (toRemove.Count == 0) return;

            // Drop every face/polygon touching a removed vertex FIRST, while their own
            // indices still refer to the OLD (pre-removal) vertex numbering - the same
            // numbering `toRemove` itself was built against.
            _faces.RemoveAll(face => toRemove.Contains(face.A) || toRemove.Contains(face.B) || toRemove.Contains(face.C));
            _polygons.RemoveAll(polygon => polygon.Indices.Any(toRemove.Contains));

            // old index -> new index, built against the STILL-UNCHANGED _vertices.Count -
            // -1 marks a removed vertex, never actually read back out (every face/polygon
            // that could have referenced one was already dropped above).
            var remap = new int[_vertices.Count];
            var nextIndex = 0;
            for (var oldIndex = 0; oldIndex < _vertices.Count; oldIndex++)
                remap[oldIndex] = toRemove.Contains(oldIndex) ? -1 : nextIndex++;

            for (var i = 0; i < _faces.Count; i++)
            {
                var face = _faces[i];
                _faces[i] = new Face(remap[face.A], remap[face.B], remap[face.C]);
            }

            for (var i = 0; i < _polygons.Count; i++)
            {
                var polygon = _polygons[i];
                _polygons[i] = new Polygon(polygon.Indices.Select(index => remap[index]));
            }

            var survivors = new List<Vertex>(_vertices.Count - toRemove.Count);
            for (var oldIndex = 0; oldIndex < _vertices.Count; oldIndex++)
                if (!toRemove.Contains(oldIndex)) survivors.Add(_vertices[oldIndex]);

            _vertices.Clear();
            _vertices.AddRange(survivors);

            RecalculateNormals();
        }

        /// <summary>
        /// Duplicates every vertex in <paramref name="indices"/> (same Position/Normal/UV/Color,
        /// appended as brand new entries in <see cref="Vertices"/> - never mutating or
        /// reindexing anything that already existed) plus every <see cref="Face"/>/
        /// <see cref="Polygon"/> whose EVERY vertex is in <paramref name="indices"/>
        /// (duplicated as a new Face/Polygon referencing the freshly duplicated vertices,
        /// sitting exactly on top of the original until moved) - the standard modeling-tool
        /// "Duplicate Selection" (Blender's Shift+D) for a component selection: a loose
        /// vertex not part of any fully-selected face still duplicates as a standalone
        /// point; a fully-selected face/polygon duplicates as a whole new face, not just
        /// its corner vertices in isolation - a partially-selected face (some but not all
        /// of its own corners in <paramref name="indices"/>) duplicates none of its
        /// vertices' connectivity at all, only the loose vertex/vertices themselves,
        /// exactly like deleting one corner of a triangle takes the whole face with it
        /// (see <see cref="RemoveVertices"/>'s own remarks) - there is no partial/torn-face
        /// duplication here either.
        ///
        /// Returns the new vertex indices, in the SAME order as <paramref name="indices"/>
        /// itself was enumerated (deduplicated - a repeated index in <paramref name="indices"/>
        /// is only ever duplicated once) - so a caller (<c>Engine.Editing.MeshEditSession</c>)
        /// can select exactly the new geometry afterward, the same "the operation leaves
        /// its own result selected" convention <see cref="ExtrudeFace"/> already follows.
        /// An empty list for an empty/all-invalid <paramref name="indices"/> - a genuine
        /// no-op, nothing added to the mesh at all.
        /// </summary>
        public IReadOnlyList<int> DuplicateVertices(IEnumerable<int> indices)
        {
            ArgumentNullException.ThrowIfNull(indices);

            var selectedSet = new HashSet<int>();
            var selectedOrder = new List<int>();
            foreach (var index in indices)
            {
                if (index < 0 || index >= _vertices.Count) continue;
                if (selectedSet.Add(index)) selectedOrder.Add(index);
            }

            if (selectedOrder.Count == 0) return Array.Empty<int>();

            // Snapshot BEFORE adding anything - AddVertex/AddFace/AddPolygon below append
            // to these same lists, and this loop must only ever consider the ORIGINAL
            // faces/polygons that existed at the moment this method was called, never one
            // it just duplicated a moment ago in this very call.
            var originalFaces = _faces.ToList();
            var originalPolygons = _polygons.ToList();

            var remap = new Dictionary<int, int>(selectedOrder.Count);
            var newIndices = new List<int>(selectedOrder.Count);
            foreach (var index in selectedOrder)
            {
                var newIndex = AddVertex(_vertices[index]);
                remap[index] = newIndex;
                newIndices.Add(newIndex);
            }

            foreach (var face in originalFaces)
                if (selectedSet.Contains(face.A) && selectedSet.Contains(face.B) && selectedSet.Contains(face.C))
                    AddFace(new Face(remap[face.A], remap[face.B], remap[face.C]));

            foreach (var polygon in originalPolygons)
                if (polygon.Indices.All(selectedSet.Contains))
                    AddPolygon(new Polygon(polygon.Indices.Select(index => remap[index])));

            return newIndices;
        }

        /// <summary>Every distinct edge in this mesh: a deduplicated (normalized so
        /// A &lt; B - the same edge shared by two adjacent faces is reported once, not
        /// twice) unordered pair of vertex indices, derived from <see cref="Faces"/> and
        /// <see cref="Polygons"/> (each face/polygon contributes the edge between every
        /// pair of consecutive corners, wrapping back to its first). This mesh has no
        /// separate "edge" data structure of its own - only faces/polygons that imply
        /// them - so <c>JolieCat3D.Engine.Editing.ComponentHitTester</c> (Edge-mode
        /// picking) and the edge-overlay marker visual both call this rather than
        /// walking <see cref="Faces"/>/<see cref="Polygons"/> themselves.</summary>
        public IEnumerable<(int A, int B)> GetEdges()
        {
            var seen = new HashSet<(int, int)>();

            static IEnumerable<(int, int)> EdgesOf(IReadOnlyList<int> indices)
            {
                for (var i = 0; i < indices.Count; i++)
                {
                    var a = indices[i];
                    var b = indices[(i + 1) % indices.Count];
                    yield return a < b ? (a, b) : (b, a);
                }
            }

            foreach (var face in _faces)
                foreach (var edge in EdgesOf(new[] { face.A, face.B, face.C }))
                    if (seen.Add(edge)) yield return edge;

            foreach (var polygon in _polygons)
                foreach (var edge in EdgesOf(polygon.Indices))
                    if (seen.Add(edge)) yield return edge;
        }

        /// <summary>Every triangle this mesh should render as: <see cref="Faces"/>
        /// verbatim, plus every <see cref="Polygon"/> in <see cref="Polygons"/>
        /// fan-triangulated (see <see cref="Polygon.Triangulate"/>). The one method
        /// <c>JolieCat3D.Engine</c>'s geometry adapter actually calls - it never needs to
        /// know Face/Polygon are two different representations at all.</summary>
        public IEnumerable<Face> GetRenderFaces()
        {
            foreach (var face in _faces) yield return face;
            foreach (var polygon in _polygons)
                foreach (var triangle in polygon.Triangulate())
                    yield return triangle;
        }

        /// <summary>The axis-aligned bounding box of every vertex position, in this
        /// mesh's own local space - <c>(Vector3.Zero, Vector3.Zero)</c> for an empty mesh.
        /// Used by <c>JolieCat3D.Engine</c>'s camera helper to frame a scene automatically.</summary>
        public (Vector3 Min, Vector3 Max) GetBounds()
        {
            if (_vertices.Count == 0) return (Vector3.Zero, Vector3.Zero);

            var min = _vertices[0].Position;
            var max = min;

            foreach (var vertex in _vertices)
            {
                min = Vector3.Min(min, vertex.Position);
                max = Vector3.Max(max, vertex.Position);
            }

            return (min, max);
        }

        /// <summary>
        /// Extrudes <paramref name="face"/> (which must already be one of this mesh's own
        /// <see cref="Polygons"/>) outward along its own face normal by
        /// <paramref name="distance"/>: duplicates its vertices into a new ring offset
        /// along that normal, connects the original ring to the new one with a side quad
        /// per edge, replaces the original polygon with the new offset one as the cap, and
        /// recalculates normals for the whole mesh afterward. Returns the new cap polygon,
        /// so a caller can chain a further operation onto the freshly-extruded face (an
        /// extrude-then-extrude "tower", for instance) the same way a modeling tool's own
        /// "Extrude" leaves the new face selected. The original face's own vertices are
        /// left untouched and still exactly where they were - anything else in the mesh
        /// that shares them (a neighboring face on the rest of the object) is unaffected;
        /// only the new side quads and cap are new geometry. Verified (vertex/polygon
        /// counts, and that every resulting face still winds outward) against a
        /// hand-built cube in a throwaway console script before being written here.
        ///
        /// The new side/cap vertices start out with the SAME UV their originating
        /// vertex already had (<c>WithPosition</c> keeps every other field, UV
        /// included) - meaningless once that vertex has been duplicated and moved
        /// somewhere else entirely, so this re-projects the WHOLE mesh's UVs via
        /// <see cref="UVProjector"/> (<see cref="UVProjectionMode.Box"/> - a per-vertex
        /// dominant-axis projection, the closest thing to a general-purpose "just make
        /// it look reasonable" unwrap for an arbitrarily extruded shape) before
        /// returning, rather than leaving the freshly extruded geometry with stale,
        /// duplicated texture coordinates a caller would otherwise have to remember to
        /// fix up itself. This does replace any hand-authored/imported UVs the REST of
        /// the mesh had too (<see cref="UVProjector.Apply"/> always re-projects
        /// everything, not just what changed) - a deliberate trade, not an oversight:
        /// "the whole mesh's UVs stay consistent with each other" matters more here than
        /// "an edit never touches anything it didn't strictly have to".
        /// </summary>
        /// <exception cref="ArgumentException"><paramref name="face"/> is not one of
        /// this mesh's own <see cref="Polygons"/>.</exception>
        public Polygon ExtrudeFace(Polygon face, float distance)
        {
            ArgumentNullException.ThrowIfNull(face);
            if (!_polygons.Remove(face))
                throw new ArgumentException("The given polygon is not part of this mesh.", nameof(face));

            var indices = face.Indices;
            var normal = ComputeFaceNormal(indices);
            var offset = normal * distance;

            var newIndices = new int[indices.Count];
            for (var i = 0; i < indices.Count; i++)
            {
                var original = _vertices[indices[i]];
                newIndices[i] = AddVertex(original.WithPosition(original.Position + offset).WithNormal(normal));
            }

            for (var i = 0; i < indices.Count; i++)
            {
                var next = (i + 1) % indices.Count;
                // (old[i], old[next], new[next], new[i]) - this exact order, verified
                // numerically (every resulting side quad winds outward on a hand-built
                // cube) before being written here; the seemingly-equivalent
                // (old[i], new[i], new[next], old[next]) is actually its reverse and
                // winds every side quad inward instead.
                AddQuad(indices[i], indices[next], newIndices[next], newIndices[i]);
            }

            var capPolygon = new Polygon(newIndices);
            AddPolygon(capPolygon);

            RecalculateNormals();
            UVProjector.Apply(this, UVProjectionMode.Box);
            return capPolygon;
        }

        /// <summary>
        /// Replaces every triangle in <see cref="Faces"/> with 4 smaller ones (split at
        /// its own 3 edge midpoints) and every n-gon in <see cref="Polygons"/> with N
        /// quads (one per edge, meeting at a new center vertex) - the standard "linear"
        /// mesh subdivision (no Catmull-Clark-style smoothing/re-positioning of existing
        /// vertices, just adding new geometry along existing edges/faces). Operates on
        /// the whole mesh, not a per-face selection - there's no concept of a partial
        /// selection in <c>JolieCat3D.Core</c> itself (that's a viewport/UI concern), so
        /// "Subdivide" here means "subdivide everything", the same way a modeling tool's
        /// own Subdivide button behaves with nothing specific selected. An edge shared by
        /// two faces gets exactly one shared midpoint vertex (not two, one per face) -
        /// verified against a hand-built shared-vertex cube in a throwaway console script
        /// (26 vertices - 8 original + 12 shared edge midpoints + 6 face centers - not 38,
        /// which is what an unshared/duplicated version would have produced) before being
        /// written here, so the subdivided mesh has no seams or cracks between faces that
        /// used to share an edge. Recalculates normals afterward, then (like
        /// <see cref="ExtrudeFace"/> - see its own remarks) re-projects the WHOLE mesh's
        /// UVs via <see cref="UVProjector"/>/<see cref="UVProjectionMode.Box"/>: the
        /// interpolated-from-parents UV every new midpoint/center vertex gets below is a
        /// reasonable INTERIM value (kept for the same "never construct a vertex with an
        /// arbitrary placeholder" reason the interim per-vertex normal lerp below is
        /// kept, even though <see cref="RecalculateNormals"/> immediately supersedes it
        /// too), not the final word - a full re-projection afterward is what actually
        /// keeps old and new geometry using one consistent, non-stretched mapping.
        /// </summary>
        public void Subdivide()
        {
            var edgeMidpoints = new Dictionary<(int, int), int>();

            int GetOrCreateMidpoint(int a, int b)
            {
                var key = a < b ? (a, b) : (b, a);
                if (edgeMidpoints.TryGetValue(key, out var existing)) return existing;

                var va = _vertices[a];
                var vb = _vertices[b];
                var midpointNormal = va.Normal + vb.Normal;
                var midpoint = new Vertex(
                    (va.Position + vb.Position) / 2f,
                    midpointNormal.LengthSquared() > float.Epsilon ? Vector3.Normalize(midpointNormal) : va.Normal,
                    (va.UV + vb.UV) / 2f,
                    Color4.Lerp(va.Color, vb.Color, 0.5f));

                var index = AddVertex(midpoint);
                edgeMidpoints[key] = index;
                return index;
            }

            var oldFaces = _faces.ToList();
            var oldPolygons = _polygons.ToList();
            _faces.Clear();
            _polygons.Clear();

            foreach (var face in oldFaces)
            {
                var m01 = GetOrCreateMidpoint(face.A, face.B);
                var m12 = GetOrCreateMidpoint(face.B, face.C);
                var m20 = GetOrCreateMidpoint(face.C, face.A);

                AddTriangle(face.A, m01, m20);
                AddTriangle(m01, face.B, m12);
                AddTriangle(m20, m12, face.C);
                AddTriangle(m01, m12, m20);
            }

            foreach (var polygon in oldPolygons)
            {
                var indices = polygon.Indices;
                var n = indices.Count;

                var midpoints = new int[n];
                for (var i = 0; i < n; i++)
                    midpoints[i] = GetOrCreateMidpoint(indices[i], indices[(i + 1) % n]);

                var centerPosition = Vector3.Zero;
                var centerNormal = Vector3.Zero;
                var centerUV = Vector2.Zero;
                foreach (var index in indices)
                {
                    centerPosition += _vertices[index].Position;
                    centerNormal += _vertices[index].Normal;
                    centerUV += _vertices[index].UV;
                }
                centerPosition /= n;
                centerNormal = centerNormal.LengthSquared() > float.Epsilon ? Vector3.Normalize(centerNormal) : Vector3.UnitY;
                centerUV /= n;

                var centerIndex = AddVertex(new Vertex(centerPosition, centerNormal, centerUV));

                for (var i = 0; i < n; i++)
                {
                    var previous = (i - 1 + n) % n;
                    // New quad: original corner -> edge-midpoint after it -> face center
                    // -> edge-midpoint before it - preserves the original polygon's own
                    // winding direction.
                    AddQuad(indices[i], midpoints[i], centerIndex, midpoints[previous]);
                }
            }

            RecalculateNormals();
            UVProjector.Apply(this, UVProjectionMode.Box);
        }

        /// <summary>
        /// Inserts one new edge loop through the ring of quads reachable from the edge
        /// <paramref name="edgeA"/>-&gt;<paramref name="edgeB"/> - the standard "Loop
        /// Cut" every quad-based modeling tool offers. Only ever walks/splits 4-sided
        /// <see cref="Polygon"/> entries (a real, disclosed scope limitation matching
        /// <see cref="ExtrudeFace"/>'s own "Polygon only, not a triangulated
        /// <see cref="Faces"/> entry" precedent) - starting from the given edge, this
        /// finds its OWN quad's OPPOSITE edge (2 steps around that quad's own 4-cycle),
        /// crosses into whichever OTHER quad shares that opposite edge (if any), and
        /// repeats - in BOTH directions from the starting edge - until the ring closes
        /// back on itself or reaches a boundary/non-quad/non-manifold edge, matching the
        /// classic "edge ring" a real loop cut follows. One shared midpoint vertex is
        /// created per ring edge (an edge two ring-adjacent quads share gets exactly ONE
        /// midpoint, not two - the same "shared edge, shared vertex" convention
        /// <see cref="Subdivide"/> already established for its own edge midpoints), and
        /// every quad the ring passes through is replaced by two new quads split along
        /// the new edge connecting consecutive ring midpoints.
        ///
        /// Returns every new quad this call actually created, in no particular order -
        /// empty if <paramref name="edgeA"/>/<paramref name="edgeB"/> isn't part of any
        /// quad at all. Recalculates normals and re-projects UVs afterward (Box
        /// projection - see <see cref="ExtrudeFace"/>'s own remarks on why a full
        /// re-projection, not just patching up the newly-inserted geometry's own
        /// interim values, is what keeps the whole mesh's UVs consistent with each
        /// other).
        /// </summary>
        public IReadOnlyList<Polygon> LoopCut(int edgeA, int edgeB)
        {
            var edgeToQuads = new Dictionary<(int A, int B), List<Polygon>>();
            foreach (var polygon in _polygons)
            {
                if (polygon.Indices.Count != 4) continue;
                for (var i = 0; i < 4; i++)
                {
                    var key = NormalizeEdge(polygon.Indices[i], polygon.Indices[(i + 1) % 4]);
                    if (!edgeToQuads.TryGetValue(key, out var list)) edgeToQuads[key] = list = new List<Polygon>();
                    list.Add(polygon);
                }
            }

            var startKey = NormalizeEdge(edgeA, edgeB);
            if (!edgeToQuads.TryGetValue(startKey, out var startQuads) || startQuads.Count == 0)
                return Array.Empty<Polygon>();

            var ringEdges = new List<(int A, int B)> { startKey };
            var ringQuads = new List<Polygon>();
            var usedQuads = new HashSet<Polygon>();

            void ExtendRing(Polygon firstQuad, (int A, int B) enteringEdge, bool forward)
            {
                var quad = firstQuad;
                var edge = enteringEdge;

                while (usedQuads.Add(quad))
                {
                    var indices = quad.Indices;
                    var position = indices.Select((_, i) => i).FirstOrDefault(i => NormalizeEdge(indices[i], indices[(i + 1) % 4]) == edge, -1);
                    if (position < 0) break;

                    var oppositePosition = (position + 2) % 4;
                    var oppositeEdge = NormalizeEdge(indices[oppositePosition], indices[(oppositePosition + 1) % 4]);

                    if (forward) { ringEdges.Add(oppositeEdge); ringQuads.Add(quad); }
                    else { ringEdges.Insert(0, oppositeEdge); ringQuads.Insert(0, quad); }

                    if (oppositeEdge == startKey) break; // the ring closed back on itself
                    if (!edgeToQuads.TryGetValue(oppositeEdge, out var candidates)) break; // a boundary edge - nothing beyond it
                    if (candidates.Count > 2) break; // non-manifold (3+ quads sharing one edge) - bail rather than guess which one continues the ring

                    var next = candidates.FirstOrDefault(q => q != quad && !usedQuads.Contains(q));
                    if (next is null) break;

                    quad = next;
                    edge = oppositeEdge;
                }
            }

            ExtendRing(startQuads[0], startKey, forward: true);
            if (startQuads.Count > 1 && !usedQuads.Contains(startQuads[1]))
                ExtendRing(startQuads[1], startKey, forward: false);

            if (ringQuads.Count == 0) return Array.Empty<Polygon>();

            var edgeMidpoints = new Dictionary<(int A, int B), int>();
            int GetOrCreateEdgeMidpoint(int a, int b)
            {
                var key = NormalizeEdge(a, b);
                if (edgeMidpoints.TryGetValue(key, out var existing)) return existing;

                var va = _vertices[key.A];
                var vb = _vertices[key.B];
                var midpointNormal = va.Normal + vb.Normal;
                var midpoint = new Vertex(
                    (va.Position + vb.Position) / 2f,
                    midpointNormal.LengthSquared() > float.Epsilon ? Vector3.Normalize(midpointNormal) : va.Normal,
                    (va.UV + vb.UV) / 2f,
                    Color4.Lerp(va.Color, vb.Color, 0.5f));

                var index = AddVertex(midpoint);
                edgeMidpoints[key] = index;
                return index;
            }

            var midpoints = new int[ringEdges.Count];
            for (var i = 0; i < ringEdges.Count; i++)
                midpoints[i] = GetOrCreateEdgeMidpoint(ringEdges[i].A, ringEdges[i].B);

            var newPolygons = new List<Polygon>();
            for (var i = 0; i < ringQuads.Count; i++)
            {
                var quad = ringQuads[i];
                var indices = quad.Indices;
                var entryPosition = indices.Select((_, k) => k).First(k => NormalizeEdge(indices[k], indices[(k + 1) % 4]) == ringEdges[i]);

                var a = indices[entryPosition];
                var b = indices[(entryPosition + 1) % 4];
                var c = indices[(entryPosition + 2) % 4];
                var d = indices[(entryPosition + 3) % 4];
                var m1 = midpoints[i];
                var m2 = midpoints[i + 1];

                _polygons.Remove(quad);

                var quadA = new Polygon(a, m1, m2, d);
                var quadB = new Polygon(m1, b, c, m2);
                AddPolygon(quadA);
                AddPolygon(quadB);
                newPolygons.Add(quadA);
                newPolygons.Add(quadB);
            }

            RecalculateNormals();
            UVProjector.Apply(this, UVProjectionMode.Box);
            return newPolygons;
        }

        private static (int A, int B) NormalizeEdge(int a, int b) => a < b ? (a, b) : (b, a);

        private static int IndexOfValue(IReadOnlyList<int> indices, int value)
        {
            for (var i = 0; i < indices.Count; i++)
                if (indices[i] == value) return i;
            return -1;
        }

        /// <summary>
        /// Chamfers the single vertex at <paramref name="vertexIndex"/> - the standard
        /// "Bevel Vertex" every hard-surface modeling tool offers, cutting its sharp
        /// corner into a small flat facet instead. Only supported for a vertex whose
        /// surrounding <see cref="Polygons"/> form one closed, manifold fan around it
        /// (every polygon touching the vertex chains to the next via a shared neighbor,
        /// looping back to the first - the same "closed fan" a real interior vertex of a
        /// solid always has); a boundary vertex (an open fan/mesh edge) or one touched by
        /// fewer than 3 faces returns null, doing nothing at all, rather than guessing at
        /// an ambiguous result. Like <see cref="LoopCut"/>, this only ever considers
        /// <see cref="Polygons"/>, never a raw triangulated <see cref="Faces"/> entry -
        /// the same disclosed scope <see cref="ExtrudeFace"/> already established.
        ///
        /// For each of the vertex's own N surrounding polygons/edges, this creates one
        /// new vertex a small fraction (<paramref name="amount"/>, clamped to (0, 0.5))
        /// of the way along that edge toward its far neighbor, replaces the original
        /// vertex's own corner in each surrounding polygon with the TWO new vertices
        /// bounding that polygon's own wedge (turning, e.g., a beveled cube corner's
        /// quads into pentagons), and adds one new N-sided cap polygon connecting all N
        /// new vertices to fill the resulting facet. The ORIGINAL vertex itself is left
        /// in <see cref="Vertices"/>, simply no longer referenced by anything (an
        /// intentional, disclosed simplification - re-indexing/removing it the way
        /// <see cref="RemoveVertices"/> does for a deleted vertex would need to touch
        /// every OTHER face/polygon in the whole mesh just to shift indices down by one,
        /// for no benefit beyond a few unused floats in <see cref="Vertices"/>).
        ///
        /// Returns the new cap <see cref="Polygon"/> (the same "leave the operation's own
        /// result selected/available for a further edit" convention <see cref="ExtrudeFace"/>'s
        /// own return value already follows), or null if the vertex isn't a supported
        /// closed-fan interior vertex. Recalculates normals and re-projects UVs
        /// afterward, same as <see cref="LoopCut"/>/<see cref="ExtrudeFace"/>/<see cref="Subdivide"/>.
        /// </summary>
        public Polygon? BevelVertex(int vertexIndex, float amount)
        {
            if (vertexIndex < 0 || vertexIndex >= _vertices.Count) return null;

            var touching = new List<(Polygon Polygon, int Prev, int Next)>();
            foreach (var polygon in _polygons)
            {
                var indices = polygon.Indices;
                var position = IndexOfValue(indices, vertexIndex);
                if (position < 0) continue;

                var n = indices.Count;
                touching.Add((polygon, indices[(position - 1 + n) % n], indices[(position + 1) % n]));
            }

            if (touching.Count < 3) return null;

            var ordered = new List<(Polygon Polygon, int Prev, int Next)> { touching[0] };
            var remaining = new List<(Polygon Polygon, int Prev, int Next)>(touching.Skip(1));

            while (remaining.Count > 0)
            {
                var matchIndex = remaining.FindIndex(candidate => candidate.Prev == ordered[^1].Next);
                if (matchIndex < 0) return null; // couldn't chain every touching polygon into one closed fan
                ordered.Add(remaining[matchIndex]);
                remaining.RemoveAt(matchIndex);
            }

            if (ordered[^1].Next != ordered[0].Prev) return null; // an open fan (a boundary vertex) - not a closed loop around the vertex

            var vertexPosition = _vertices[vertexIndex].Position;
            var t = Math.Clamp(amount, 0.001f, 0.499f);
            var k = ordered.Count;

            // bevelVertices[i] sits along the edge from vertexIndex toward
            // ordered[i].Next - i.e. ordered[i] itself supplies the polygon whose own
            // "far" side of the cut uses this new vertex.
            var bevelVertices = new int[k];
            for (var i = 0; i < k; i++)
            {
                var neighborIndex = ordered[i].Next;
                var neighborVertex = _vertices[neighborIndex];
                var newPosition = Vector3.Lerp(vertexPosition, neighborVertex.Position, t);
                bevelVertices[i] = AddVertex(neighborVertex.WithPosition(newPosition));
            }

            for (var i = 0; i < k; i++)
            {
                var polygon = ordered[i].Polygon;
                var indices = polygon.Indices;
                var position = IndexOfValue(indices, vertexIndex);

                var newIndices = new List<int>(indices);
                // ordered[i]'s own Prev is ordered[(i-1+k)%k]'s own Next - so the bevel
                // vertex sitting toward THIS polygon's prev-side neighbor is
                // bevelVertices[(i-1+k)%k], and toward its next-side neighbor is
                // bevelVertices[i] - inserted in that same order to preserve the
                // polygon's own original winding direction.
                newIndices[position] = bevelVertices[i];
                newIndices.Insert(position, bevelVertices[(i - 1 + k) % k]);

                _polygons.Remove(polygon);
                AddPolygon(new Polygon(newIndices));
            }

            var cap = new Polygon(bevelVertices);
            AddPolygon(cap);

            RecalculateNormals();
            UVProjector.Apply(this, UVProjectionMode.Box);
            return cap;
        }

        /// <summary>Newell's method - the face normal of an arbitrary (possibly
        /// non-planar or non-triangular) polygon, robust where a plain 3-point cross
        /// product isn't. Matches this codebase's existing winding convention
        /// (<see cref="RecalculateNormals"/>'s own <c>Cross(b-a, c-a)</c> per triangle) -
        /// verified against it (a simple CCW-from-+Z square) before being relied on here.</summary>
        private Vector3 ComputeFaceNormal(IReadOnlyList<int> indices)
        {
            var normal = Vector3.Zero;
            for (var i = 0; i < indices.Count; i++)
            {
                var current = _vertices[indices[i]].Position;
                var next = _vertices[indices[(i + 1) % indices.Count]].Position;
                normal.X += (current.Y - next.Y) * (current.Z + next.Z);
                normal.Y += (current.Z - next.Z) * (current.X + next.X);
                normal.Z += (current.X - next.X) * (current.Y + next.Y);
            }

            return normal.LengthSquared() > float.Epsilon ? Vector3.Normalize(normal) : Vector3.UnitY;
        }

        /// <summary>
        /// Recomputes every vertex's <see cref="Vertex.Normal"/> as the normalized
        /// average of the face normals of every triangle (from <see cref="GetRenderFaces"/>)
        /// it participates in - the standard smooth-shading normal, for a mesh built
        /// (like <c>Primitives</c>'s helpers) or edited without normals of its own.
        /// Replaces <see cref="Vertices"/> in place.
        /// </summary>
        public void RecalculateNormals()
        {
            var accumulated = new Vector3[_vertices.Count];

            foreach (var face in GetRenderFaces())
            {
                var a = _vertices[face.A].Position;
                var b = _vertices[face.B].Position;
                var c = _vertices[face.C].Position;

                var faceNormal = Vector3.Cross(b - a, c - a);
                // A degenerate (zero-area) triangle contributes nothing rather than
                // polluting its vertices' normals with a NaN from normalizing a zero vector.
                if (faceNormal.LengthSquared() < float.Epsilon) continue;

                accumulated[face.A] += faceNormal;
                accumulated[face.B] += faceNormal;
                accumulated[face.C] += faceNormal;
            }

            for (var i = 0; i < _vertices.Count; i++)
            {
                var normal = accumulated[i].LengthSquared() > float.Epsilon
                    ? Vector3.Normalize(accumulated[i])
                    : Vector3.UnitY;
                _vertices[i] = _vertices[i].WithNormal(normal);
            }
        }
    }
}
