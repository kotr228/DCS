namespace JolieCat3D.Core.Geometry
{
    /// <summary>
    /// The (up to 4) bone-slot indices a <see cref="Vertex"/> pairs positionally with
    /// its own <see cref="Vertex.BoneWeights"/> - plain <c>int</c>s (there is no
    /// <c>Vector4</c>-of-<c>int</c> in <see cref="System.Numerics"/> to reuse the way
    /// <see cref="Vertex.BoneWeights"/> reuses <see cref="System.Numerics.Vector4"/> for
    /// its own 4 floats). All-zero (every field defaulting to slot 0) is a perfectly
    /// safe default even though 0 is also a genuine, valid bone slot - see
    /// <see cref="Vertex.BoneIndices"/>'s own remarks on why no separate "unbound"
    /// sentinel is needed here.
    /// </summary>
    public readonly struct BoneIndices : IEquatable<BoneIndices>
    {
        public readonly int X;
        public readonly int Y;
        public readonly int Z;
        public readonly int W;

        public BoneIndices(int x, int y, int z, int w)
        {
            X = x;
            Y = y;
            Z = z;
            W = w;
        }

        public bool Equals(BoneIndices other) => X == other.X && Y == other.Y && Z == other.Z && W == other.W;
        public override bool Equals(object? obj) => obj is BoneIndices other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(X, Y, Z, W);
    }
}
