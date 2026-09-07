namespace JolieCat3D.Core.Geometry
{
    /// <summary>
    /// One triangle in a <see cref="Mesh"/>: three indices into <see cref="Mesh.Vertices"/>,
    /// wound counter-clockwise when viewed from the side the normal points toward (the
    /// same convention <c>System.Windows.Media.Media3D.MeshGeometry3D.TriangleIndices</c>
    /// expects, so <c>JolieCat3D.Engine</c>'s adapter can copy these straight across with
    /// no winding-order fix-up). This is the GPU-native primitive every renderer actually
    /// draws - contrast <see cref="Polygon"/>, the authoring-time n-sided face a modeling
    /// tool works with, which <see cref="Mesh.Triangulate"/> fans out into one or more of
    /// these.
    /// </summary>
    public readonly struct Face : IEquatable<Face>
    {
        public readonly int A;
        public readonly int B;
        public readonly int C;

        public Face(int a, int b, int c)
        {
            A = a;
            B = b;
            C = c;
        }

        /// <summary>The three indices in winding order, for code that wants to loop over them uniformly.</summary>
        public IEnumerable<int> Indices()
        {
            yield return A;
            yield return B;
            yield return C;
        }

        public bool Equals(Face other) => A == other.A && B == other.B && C == other.C;
        public override bool Equals(object? obj) => obj is Face other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(A, B, C);
        public override string ToString() => $"Face({A}, {B}, {C})";
    }
}
