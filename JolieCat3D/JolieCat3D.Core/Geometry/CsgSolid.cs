using System.Numerics;
using JolieCat3D.Core.Modifiers;
using JolieCat3D.Core.Numerics;

namespace JolieCat3D.Core.Geometry
{
    /// <summary>
    /// Constructive Solid Geometry: combines two triangulated <see cref="Mesh"/>es (already
    /// in the SAME coordinate space - see <see cref="Modifiers.BooleanModifier"/> for how a
    /// target mesh gets transformed into that space first) via <see cref="BooleanOperation.Union"/>/
    /// <see cref="BooleanOperation.Difference"/>/<see cref="BooleanOperation.Intersection"/>.
    ///
    /// A faithful port of the classic BSP-tree CSG algorithm (the same one behind csg.js/
    /// OpenSCAD-style tools, tracing back to Naylor/Amanatides/Thibault's 1990 "Merging BSP
    /// Trees Yields Polyhedral Set Operations") rather than a hand-rolled approximation: a
    /// BSP tree built from one solid's own polygons can classify any point in space as
    /// inside/outside that solid, which is exactly what clipping the OTHER solid's polygons
    /// against it computes - keep the parts of B outside A (and vice versa) for a Union,
    /// keep the parts of A outside B plus the parts of B INSIDE A but flipped for a
    /// Difference, and so on. Every polygon here is a convex, planar N-gon (not
    /// necessarily a triangle) while the algorithm runs - splitting a triangle against an
    /// arbitrary plane can produce a quad or a pentagon - and the whole result is
    /// re-triangulated (fan triangulation from each polygon's own first vertex, the exact
    /// convention <see cref="Polygon.Triangulate"/> already uses elsewhere in this project)
    /// only once, at the very end, when converting back to a <see cref="Mesh"/>.
    /// </summary>
    public static class CsgSolid
    {
        /// <summary>How close to exactly on a splitting plane a vertex has to be to count
        /// as coplanar with it, rather than strictly in front of or behind it - the same
        /// role <see cref="Modifiers.MirrorModifier.WeldThreshold"/> plays for "is this
        /// vertex effectively ON the mirror plane". Too tight and ordinary floating-point
        /// rounding on an axis-aligned cut turns a coplanar face into a razor-thin sliver
        /// polygon; too loose and a genuinely angled cut gets misclassified.</summary>
        private const float PlaneEpsilon = 1e-5f;

        /// <summary>Combines <paramref name="a"/> and <paramref name="b"/> (both already
        /// in the same local space) via <paramref name="operation"/>, returning a brand
        /// new <see cref="Mesh"/> - neither input is mutated, the same non-destructive
        /// contract every other <see cref="Modifier"/> already has. Normals are
        /// recalculated fresh on the result (a BSP split invents new vertices along every
        /// cut, whose "correct" normal is whatever <see cref="Mesh.RecalculateNormals"/>'s
        /// own smooth-shading average produces, not something meaningful to carry through
        /// the clip itself) - UVs are NOT recomputed here (a CSG cut has no principled
        /// mapping of its own to invent one from), left at whatever the nearest surviving
        /// input vertex's own UV was, matching how a caller normally re-projects UVs
        /// afterward anyway (<see cref="UVProjector"/>) rather than this method attempting
        /// to guess a texture layout no CSG algorithm has enough information to get right.</summary>
        public static Mesh Combine(Mesh a, Mesh b, BooleanOperation operation)
        {
            ArgumentNullException.ThrowIfNull(a);
            ArgumentNullException.ThrowIfNull(b);

            var polygonsA = ToCsgPolygons(a);
            var polygonsB = ToCsgPolygons(b);

            var result = operation switch
            {
                BooleanOperation.Union => Union(polygonsA, polygonsB),
                BooleanOperation.Difference => Subtract(polygonsA, polygonsB),
                BooleanOperation.Intersection => Intersect(polygonsA, polygonsB),
                _ => throw new ArgumentOutOfRangeException(nameof(operation)),
            };

            return FromCsgPolygons(result, a.Name);
        }

        // ============================= A <-> Mesh conversion =============================

        private static List<CsgPolygon> ToCsgPolygons(Mesh mesh)
        {
            var polygons = new List<CsgPolygon>();

            foreach (var face in mesh.GetRenderFaces())
            {
                var vertices = new List<CsgVertex>(3)
                {
                    ToCsgVertex(mesh.Vertices[face.A]),
                    ToCsgVertex(mesh.Vertices[face.B]),
                    ToCsgVertex(mesh.Vertices[face.C]),
                };

                // A degenerate (zero-area, e.g. two coincident corners) triangle has no
                // well-defined plane to build a BSP node from at all - dropped here rather
                // than let CsgPlane.FromPoints normalize a zero-length cross product into a
                // NaN normal that would silently poison every split it ever touches.
                if (IsDegenerate(vertices)) continue;

                polygons.Add(new CsgPolygon(vertices));
            }

            return polygons;
        }

        private static bool IsDegenerate(List<CsgVertex> triangle)
        {
            var normal = Vector3.Cross(triangle[1].Position - triangle[0].Position, triangle[2].Position - triangle[0].Position);
            return normal.LengthSquared() <= float.Epsilon;
        }

        private static CsgVertex ToCsgVertex(Vertex vertex) => new(vertex.Position, vertex.Normal, vertex.UV, vertex.Color);

        /// <summary>Fan-triangulates every surviving (possibly N-gon, after however many
        /// BSP splits it went through) polygon - see <see cref="Combine"/>'s own remarks -
        /// and recalculates normals once for the whole result.</summary>
        private static Mesh FromCsgPolygons(List<CsgPolygon> polygons, string name)
        {
            var mesh = new Mesh(name);

            foreach (var polygon in polygons)
            {
                if (polygon.Vertices.Count < 3) continue; // a sliver a split degenerated away to nothing - never a valid face

                var indices = new int[polygon.Vertices.Count];
                for (var i = 0; i < polygon.Vertices.Count; i++)
                {
                    var v = polygon.Vertices[i];
                    indices[i] = mesh.AddVertex(new Vertex(v.Position, v.Normal, v.UV, v.Color));
                }

                // Fan triangulation from indices[0] - matches Polygon.Triangulate's own
                // convention exactly, so this reads back identically to any other
                // n-gon-producing operation in this project.
                for (var i = 1; i < indices.Length - 1; i++)
                    mesh.AddTriangle(indices[0], indices[i], indices[i + 1]);
            }

            mesh.RecalculateNormals();
            return mesh;
        }

        // ============================= The BSP algorithm itself =============================
        // A direct, faithful port of the classic "csg.js" BSP CSG algorithm (Union/Subtract/
        // Intersect all reduce to the same handful of clip/invert steps, just in a
        // different order/count) - see each method's own remarks for why its particular
        // sequence produces that specific operation.

        /// <summary>Union: keep the parts of A outside B, and the parts of B outside A (a
        /// polygon exactly ON the boundary between them is kept only once - from A's own
        /// clip - rather than duplicated, which is what the specific
        /// clip-A-clip-B-invert-clip-invert-rebuild sequence below achieves).</summary>
        private static List<CsgPolygon> Union(List<CsgPolygon> a, List<CsgPolygon> b)
        {
            var nodeA = new CsgNode(a);
            var nodeB = new CsgNode(b);

            nodeA.ClipTo(nodeB);
            nodeB.ClipTo(nodeA);
            nodeB.Invert();
            nodeB.ClipTo(nodeA);
            nodeB.Invert();
            nodeA.Build(nodeB.AllPolygons());

            return nodeA.AllPolygons();
        }

        /// <summary>Difference (A - B): keep the parts of A outside B, plus the parts of B
        /// INSIDE A with their winding/normals flipped (the "inner surface" of the cut,
        /// facing back into the resulting cavity) - achieved by inverting A first (so
        /// "outside A" and "inside A" swap meaning for the clip operations below), doing
        /// the same clip dance as <see cref="Union"/>, then inverting the WHOLE result back
        /// the right way round at the end.</summary>
        private static List<CsgPolygon> Subtract(List<CsgPolygon> a, List<CsgPolygon> b)
        {
            var nodeA = new CsgNode(a);
            var nodeB = new CsgNode(b);

            nodeA.Invert();
            nodeA.ClipTo(nodeB);
            nodeB.ClipTo(nodeA);
            nodeB.Invert();
            nodeB.ClipTo(nodeA);
            nodeB.Invert();
            nodeA.Build(nodeB.AllPolygons());
            nodeA.Invert();

            return nodeA.AllPolygons();
        }

        /// <summary>Intersection: keep only the parts of A that are INSIDE B, and the parts
        /// of B that are inside A - the mirror image of <see cref="Union"/> (inverting both
        /// solids turns "keep the outside" into "keep the inside" for every clip below,
        /// then the final invert un-does the bookkeeping, not the actual geometry).</summary>
        private static List<CsgPolygon> Intersect(List<CsgPolygon> a, List<CsgPolygon> b)
        {
            var nodeA = new CsgNode(a);
            var nodeB = new CsgNode(b);

            nodeA.Invert();
            nodeB.ClipTo(nodeA);
            nodeB.Invert();
            nodeA.ClipTo(nodeB);
            nodeB.ClipTo(nodeA);
            nodeA.Build(nodeB.AllPolygons());
            nodeA.Invert();

            return nodeA.AllPolygons();
        }

        /// <summary>One CSG vertex - like <see cref="Geometry.Vertex"/>, but a mutable
        /// class (not a readonly struct): a BSP split needs to build brand new,
        /// linearly-interpolated vertices along a cut on the fly, which is exactly the
        /// shape a mutable, freely-constructible helper type is for, without pulling
        /// <see cref="Geometry.Vertex"/>'s own "immutable value type" contract into this
        /// entirely internal, throwaway representation.</summary>
        private sealed class CsgVertex
        {
            public Vector3 Position;
            public Vector3 Normal;
            public Vector2 UV;
            public Color4 Color;

            public CsgVertex(Vector3 position, Vector3 normal, Vector2 uv, Color4 color)
            {
                Position = position;
                Normal = normal;
                UV = uv;
                Color = color;
            }

            public CsgVertex Clone() => new(Position, Normal, UV, Color);

            public void Flip() => Normal = -Normal;

            public static CsgVertex Interpolate(CsgVertex a, CsgVertex b, float t) => new(
                Vector3.Lerp(a.Position, b.Position, t),
                Vector3.Lerp(a.Normal, b.Normal, t),
                Vector2.Lerp(a.UV, b.UV, t),
                Color4.Lerp(a.Color, b.Color, t));
        }

        /// <summary>The infinite plane one CSG polygon lies in - <c>Dot(Normal, p) == W</c>
        /// for every point <c>p</c> on it. A mutable class (not a struct/readonly value):
        /// <see cref="CsgNode"/> keeps ONE plane per tree node and needs to
        /// <see cref="Flip"/> it in place when the whole node inverts - see
        /// <see cref="CsgNode.Invert"/>'s own remarks on why every reference to a node's
        /// own plane must first be <see cref="Clone"/>d rather than shared, or flipping one
        /// copy would silently flip every other polygon that happened to reference the
        /// exact same instance too.</summary>
        private sealed class CsgPlane
        {
            public Vector3 Normal;
            public float W;

            private CsgPlane(Vector3 normal, float w)
            {
                Normal = normal;
                W = w;
            }

            public static CsgPlane FromPoints(Vector3 a, Vector3 b, Vector3 c)
            {
                var normal = Vector3.Normalize(Vector3.Cross(b - a, c - a));
                return new CsgPlane(normal, Vector3.Dot(normal, a));
            }

            public CsgPlane Clone() => new(Normal, W);

            public void Flip()
            {
                Normal = -Normal;
                W = -W;
            }

            /// <summary>Classifies <paramref name="polygon"/> against this plane and
            /// routes it (or, for one that straddles the plane, the two NEW polygons it
            /// splits into) into whichever of the four output lists applies - the one
            /// operation the entire BSP algorithm is actually built from.</summary>
            public void SplitPolygon(
                CsgPolygon polygon,
                List<CsgPolygon> coplanarFront,
                List<CsgPolygon> coplanarBack,
                List<CsgPolygon> front,
                List<CsgPolygon> back)
            {
                const int Coplanar = 0, Front = 1, Back = 2, Spanning = 3;

                var polygonType = 0;
                var types = new int[polygon.Vertices.Count];

                for (var i = 0; i < polygon.Vertices.Count; i++)
                {
                    var distance = Vector3.Dot(Normal, polygon.Vertices[i].Position) - W;
                    var type = distance < -PlaneEpsilon ? Back : distance > PlaneEpsilon ? Front : Coplanar;
                    types[i] = type;
                    polygonType |= type;
                }

                switch (polygonType)
                {
                    case Coplanar:
                        (Vector3.Dot(Normal, polygon.Plane.Normal) > 0 ? coplanarFront : coplanarBack).Add(polygon);
                        break;

                    case Front:
                        front.Add(polygon);
                        break;

                    case Back:
                        back.Add(polygon);
                        break;

                    default: // Spanning - straddles the plane, split into a front part and a back part.
                        var frontVertices = new List<CsgVertex>();
                        var backVertices = new List<CsgVertex>();

                        for (var i = 0; i < polygon.Vertices.Count; i++)
                        {
                            var j = (i + 1) % polygon.Vertices.Count;
                            var typeI = types[i];
                            var typeJ = types[j];
                            var vertexI = polygon.Vertices[i];
                            var vertexJ = polygon.Vertices[j];

                            if (typeI != Back) frontVertices.Add(vertexI);
                            if (typeI != Front) backVertices.Add(typeI != Back ? vertexI.Clone() : vertexI);

                            if ((typeI | typeJ) == Spanning)
                            {
                                var denominator = Vector3.Dot(Normal, vertexJ.Position - vertexI.Position);
                                var t = (W - Vector3.Dot(Normal, vertexI.Position)) / denominator;
                                var splitVertex = CsgVertex.Interpolate(vertexI, vertexJ, t);
                                frontVertices.Add(splitVertex);
                                backVertices.Add(splitVertex.Clone());
                            }
                        }

                        if (frontVertices.Count >= 3) front.Add(new CsgPolygon(frontVertices));
                        if (backVertices.Count >= 3) back.Add(new CsgPolygon(backVertices));
                        break;
                }
            }
        }

        /// <summary>One convex, planar polygon (a triangle going in, possibly an N-gon
        /// after being split against another solid's planes) - <see cref="Vertices"/> plus
        /// the <see cref="CsgPlane"/> its own first 3 vertices define.</summary>
        private sealed class CsgPolygon
        {
            public List<CsgVertex> Vertices { get; }
            public CsgPlane Plane { get; }

            public CsgPolygon(List<CsgVertex> vertices)
            {
                Vertices = vertices;
                Plane = CsgPlane.FromPoints(vertices[0].Position, vertices[1].Position, vertices[2].Position);
            }

            public void Flip()
            {
                Vertices.Reverse();
                foreach (var vertex in Vertices) vertex.Flip();
                Plane.Flip();
            }
        }

        /// <summary>One node of a Binary Space Partitioning tree - <see cref="Polygons"/>
        /// are the ones that lie exactly ON this node's own <see cref="Plane"/>;
        /// everything in front of it recurses into <see cref="_front"/>, everything behind
        /// into <see cref="_back"/>. Building one from a solid's own polygons (picking each
        /// node's splitting plane from the FIRST remaining polygon, the same simple,
        /// standard heuristic csg.js itself uses rather than a more elaborate
        /// balance-optimizing plane selection) is what turns "a list of polygons" into
        /// something that can answer "is this OTHER polygon in front of, behind, or
        /// straddling everything this solid is made of" - exactly what clipping one
        /// solid's polygons against another's tree computes.</summary>
        private sealed class CsgNode
        {
            private CsgPlane? _plane;
            private CsgNode? _front;
            private CsgNode? _back;
            private List<CsgPolygon> _polygons = new();

            public CsgNode()
            {
            }

            public CsgNode(List<CsgPolygon> polygons) => Build(polygons);

            /// <summary>Swaps "inside" and "outside" for this whole subtree: every
            /// polygon's own winding/normal flips, this node's own splitting plane flips,
            /// front/back subtrees recursively invert AND then swap places with each other
            /// (the region that used to be "in front of this plane" is now "behind" the
            /// flipped one, and vice versa). <see cref="_plane"/> is always this node's own
            /// PRIVATE clone (see <see cref="Build"/>), never a reference shared with any
            /// polygon's own <see cref="CsgPolygon.Plane"/> - flipping it here must never
            /// also silently flip some unrelated polygon that merely started out
            /// coplanar with it.</summary>
            public void Invert()
            {
                foreach (var polygon in _polygons) polygon.Flip();
                _plane?.Flip();
                _front?.Invert();
                _back?.Invert();
                (_front, _back) = (_back, _front);
            }

            /// <summary>Every polygon in <paramref name="polygons"/>, clipped to only the
            /// parts that lie OUTSIDE this solid (in front of every plane a polygon
            /// reaches, walking down whichever side of this tree it's actually on) - the
            /// core primitive <see cref="ClipTo"/> is built from.</summary>
            private List<CsgPolygon> ClipPolygons(List<CsgPolygon> polygons)
            {
                if (_plane is null) return new List<CsgPolygon>(polygons);

                var front = new List<CsgPolygon>();
                var back = new List<CsgPolygon>();

                foreach (var polygon in polygons)
                    _plane.SplitPolygon(polygon, front, back, front, back);

                if (_front is not null) front = _front.ClipPolygons(front);

                // The standard BSP-CSG convention (this is the one place it actually
                // shows up in code, so it's worth spelling out): space in FRONT of a leaf
                // plane with no further subdivision defaults to OUTSIDE the solid (a
                // solid's own polygons all face outward, so past the outermost plane,
                // with nothing left to check, there is nothing solid left to be inside
                // of) - kept, by simply leaving `front` as whatever SplitPolygon already
                // bucketed into it above. Space BEHIND a leaf plane with no _back
                // subtree defaults the OTHER way - to INSIDE the solid (nothing further
                // subdivides the interior, so "behind every remaining plane" just means
                // "the solid's own interior") - discarded here, not kept, since
                // ClipPolygons' whole job is "keep only what's outside". Getting this
                // backwards (keeping instead of discarding here) is exactly what silently
                // turned every Boolean operation into a no-op union-by-concatenation the
                // first time this was written - caught by CsgSolid's own regression
                // script (a volume check, not just "did it throw"), not by inspection.
                back = _back is not null ? _back.ClipPolygons(back) : new List<CsgPolygon>();

                front.AddRange(back);
                return front;
            }

            /// <summary>Replaces this WHOLE subtree's own polygons with the result of
            /// clipping them against <paramref name="other"/> - recursively, so every
            /// polygon anywhere in this tree ends up keeping only the part of itself that
            /// lies outside <paramref name="other"/>'s own solid.</summary>
            public void ClipTo(CsgNode other)
            {
                _polygons = other.ClipPolygons(_polygons);
                _front?.ClipTo(other);
                _back?.ClipTo(other);
            }

            /// <summary>Every polygon anywhere in this tree, front and back subtrees
            /// included - the "flatten the BSP tree back into a plain polygon list" step
            /// every CSG operation ends with.</summary>
            public List<CsgPolygon> AllPolygons()
            {
                var result = new List<CsgPolygon>(_polygons);
                if (_front is not null) result.AddRange(_front.AllPolygons());
                if (_back is not null) result.AddRange(_back.AllPolygons());
                return result;
            }

            /// <summary>Inserts <paramref name="polygons"/> into this tree - the first
            /// call (from the constructor) picks this node's own splitting plane from the
            /// first polygon given (cloned - see <see cref="Invert"/>'s own remarks on
            /// why never a shared reference); every subsequent call (from
            /// <see cref="Union"/>/<see cref="Subtract"/>/<see cref="Intersect"/> merging
            /// the other solid's surviving polygons back in) reuses whatever plane this
            /// node already has.</summary>
            public void Build(List<CsgPolygon> polygons)
            {
                if (polygons.Count == 0) return;

                _plane ??= polygons[0].Plane.Clone();

                var front = new List<CsgPolygon>();
                var back = new List<CsgPolygon>();

                foreach (var polygon in polygons)
                    _plane.SplitPolygon(polygon, _polygons, _polygons, front, back);

                if (front.Count > 0)
                {
                    _front ??= new CsgNode();
                    _front.Build(front);
                }

                if (back.Count > 0)
                {
                    _back ??= new CsgNode();
                    _back.Build(back);
                }
            }
        }
    }
}
