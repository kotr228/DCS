using System.Numerics;

namespace JolieCat3D.Core.Geometry
{
    /// <summary>
    /// Finds CONCAVE edges (creases/corners a mesh folds INTO, like the inside of a
    /// notch - as opposed to a CONVEX edge, like a cube's outer corner, which folds
    /// AWAY and gets no darker) - the actual geometry behind "SSAO" in this project's
    /// fixed-function WPF pipeline.
    ///
    /// A REAL Screen Space Ambient Occlusion pass needs per-pixel depth AND normal
    /// buffers sampled in a programmable pixel shader - WPF's classic <c>Model3D</c>
    /// pipeline has neither (the same structural limitation <see cref="Materials.Material"/>'s
    /// own remarks already disclose for normal mapping/environment reflections), and
    /// even <c>System.Windows.Media.Effects.ShaderEffect</c> (2D post-processing on the
    /// FINAL composited bitmap - see <c>Engine.Rendering.Scene3DRenderer</c>'s own
    /// Anti-Aliasing remarks for the one thing WPF's 2D effect stack DOES let this
    /// project do) has no depth channel of its own to sample either - there is no
    /// screen-space depth/normal data ANYWHERE in this rendering pipeline for a real
    /// SSAO pass to read. What this class computes instead is the actual geometric
    /// INTUITION real AO approximates (a crevice occludes more ambient light than a
    /// flat or convex surface) directly from mesh TOPOLOGY: for every edge shared by
    /// exactly two triangles, the dihedral angle between their own face normals, and
    /// whether that fold is concave (a crevice - darkened) or convex (an outer edge -
    /// left alone). <see cref="Engine.Rendering.AmbientOcclusionVisualFactory"/> turns
    /// the result into visible darkening decals along each crease.
    /// </summary>
    public static class ContactOcclusion
    {
        /// <summary>One concave (crease/corner) edge found by <see cref="FindConcaveEdges"/> -
        /// the shared edge's own two endpoints, the AVERAGE of the two adjoining faces'
        /// normals (which way to offset a darkening decal so it doesn't z-fight the
        /// surface it hugs), and a [0,1] <see cref="Strength"/> (0 at the concavity
        /// threshold itself, 1 at a full 180-degree fold-back).</summary>
        public readonly record struct ConcaveEdge(Vector3 A, Vector3 B, Vector3 Normal, float Strength);

        /// <summary>Every concave edge in <paramref name="mesh"/> whose own dihedral
        /// angle exceeds <paramref name="concavityThresholdDegrees"/> (20 degrees by
        /// default - roughly "a crisp enough fold to actually read as a crevice",
        /// excluding the small angle noise a subdivided/smoothed curved surface's own
        /// adjacent faces would otherwise constantly trip on). Shared-edge detection is
        /// by VERTEX INDEX, not position - two faces that happen to occupy the same
        /// edge in world space but don't share an actual vertex INDEX there (a UV-seam-
        /// duplicated vertex, the same case <c>Modifiers.MirrorModifier</c>'s own
        /// position-based welding exists for elsewhere in this project) are not detected
        /// as adjacent here - a real, disclosed limitation of working from topology
        /// alone, not a claim that every visually-touching edge is found. An edge shared
        /// by anything other than exactly 2 triangles (a boundary edge with only one, or
        /// a non-manifold edge with three or more) has no single well-defined fold to
        /// measure and is skipped entirely.</summary>
        public static IEnumerable<ConcaveEdge> FindConcaveEdges(Mesh mesh, float concavityThresholdDegrees = 20f)
        {
            ArgumentNullException.ThrowIfNull(mesh);

            var edgeFaces = new Dictionary<(int Min, int Max), List<(Vector3 Normal, Vector3 Centroid, Vector3 A, Vector3 B)>>();

            void AddEdge(int i, int j, Vector3 normal, Vector3 centroid)
            {
                var key = i < j ? (i, j) : (j, i);
                var a = mesh.Vertices[i].Position;
                var b = mesh.Vertices[j].Position;

                if (!edgeFaces.TryGetValue(key, out var faces)) edgeFaces[key] = faces = new List<(Vector3, Vector3, Vector3, Vector3)>();
                faces.Add((normal, centroid, a, b));
            }

            foreach (var face in mesh.GetRenderFaces())
            {
                var a = mesh.Vertices[face.A].Position;
                var b = mesh.Vertices[face.B].Position;
                var c = mesh.Vertices[face.C].Position;

                var cross = Vector3.Cross(b - a, c - a);
                if (cross.LengthSquared() < 1e-12f) continue; // a degenerate (zero-area) triangle has no well-defined normal to fold against

                var normal = Vector3.Normalize(cross);
                var centroid = (a + b + c) / 3f;

                AddEdge(face.A, face.B, normal, centroid);
                AddEdge(face.B, face.C, normal, centroid);
                AddEdge(face.C, face.A, normal, centroid);
            }

            var thresholdRadians = concavityThresholdDegrees * (MathF.PI / 180f);

            foreach (var faces in edgeFaces.Values)
            {
                if (faces.Count != 2) continue;

                var (normal1, centroid1, a, b) = faces[0];
                var (normal2, centroid2, _, _) = faces[1];

                var cosAngle = Math.Clamp(Vector3.Dot(normal1, normal2), -1f, 1f);
                var angle = MathF.Acos(cosAngle);
                if (angle <= thresholdRadians) continue;

                // The standard convex/concave edge test: a vector from face 1's own
                // centroid toward face 2's centroid that points roughly the SAME way
                // face 1's own normal does means face 2 sits "in front of" face 1 -
                // the two faces bend AWAY from each other (convex, like a cube's outer
                // corner - nothing to darken). Pointing the OPPOSITE way means face 2
                // sits "behind" face 1 relative to its own normal - the two faces fold
                // TOWARD each other, forming a crevice (concave - exactly what this
                // method looks for).
                var towardOtherFace = centroid2 - centroid1;
                var isConcave = Vector3.Dot(normal1, towardOtherFace) < 0f;
                if (!isConcave) continue;

                var strength = Math.Clamp((angle - thresholdRadians) / (MathF.PI - thresholdRadians), 0f, 1f);
                var averageNormal = Vector3.Normalize(normal1 + normal2);
                // A perfect 180-degree fold-back (normal1 == -normal2) has no well-defined
                // average at all - an extreme, degenerate case (a mesh folded flat onto
                // itself) with no sane single direction to offset a decal along; skip it
                // rather than emit a NaN normal.
                if (float.IsNaN(averageNormal.X)) continue;

                yield return new ConcaveEdge(a, b, averageNormal, strength);
            }
        }
    }
}
