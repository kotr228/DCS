using System.Numerics;

namespace JolieCat3D.Core.Geometry
{
    /// <summary>
    /// (Re)computes every vertex's <see cref="Vertex.UV"/> in a <see cref="Mesh"/> from
    /// its own position/normal, via one of <see cref="UVProjectionMode"/>'s projection
    /// schemes, replacing whatever UVs (authored, absent, or left stale by an edit -
    /// <see cref="Mesh.ExtrudeFace"/>/<see cref="Mesh.Subdivide"/> neither know nor try
    /// to keep a face's UVs meaningful across a reshape) the mesh had before. Mutates
    /// the mesh in place via <see cref="Mesh.SetVertexUV"/>, the same "read the whole
    /// mesh, then mutate it in one pass" shape <see cref="Mesh.RecalculateNormals"/>
    /// already uses for a similar per-vertex recomputation.
    /// </summary>
    public static class UVProjector
    {
        public static void Apply(Mesh mesh, UVProjectionMode mode)
        {
            ArgumentNullException.ThrowIfNull(mesh);

            switch (mode)
            {
                case UVProjectionMode.Planar:
                    ApplyPlanar(mesh);
                    break;
                case UVProjectionMode.Box:
                    ApplyBox(mesh);
                    break;
                case UVProjectionMode.Spherical:
                    ApplySpherical(mesh);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(mode));
            }
        }

        /// <summary>Projects straight down Y onto the mesh's own XZ bounds, normalized
        /// to 0-1 - see <see cref="UVProjectionMode.Planar"/>'s own remarks.</summary>
        private static void ApplyPlanar(Mesh mesh)
        {
            var (min, max) = mesh.GetBounds();
            var sizeX = MathF.Max(max.X - min.X, float.Epsilon);
            var sizeZ = MathF.Max(max.Z - min.Z, float.Epsilon);

            for (var i = 0; i < mesh.Vertices.Count; i++)
            {
                var position = mesh.Vertices[i].Position;
                var u = (position.X - min.X) / sizeX;
                var v = (position.Z - min.Z) / sizeZ;
                mesh.SetVertexUV(i, new Vector2(u, v));
            }
        }

        /// <summary>Per-vertex triplanar projection onto whichever axis plane the
        /// vertex's own normal points most toward - see
        /// <see cref="UVProjectionMode.Box"/>'s own remarks. Falls back to the Y-facing
        /// (XZ-plane) projection for a zero normal (an unlit/not-yet-normal-calculated
        /// mesh), rather than picking an arbitrary axis or dividing by a zero-length
        /// comparison.</summary>
        private static void ApplyBox(Mesh mesh)
        {
            var (min, max) = mesh.GetBounds();
            var size = Vector3.Max(max - min, new Vector3(float.Epsilon));

            for (var i = 0; i < mesh.Vertices.Count; i++)
            {
                var vertex = mesh.Vertices[i];
                var position = vertex.Position;
                var normal = vertex.Normal;
                var absNormal = new Vector3(MathF.Abs(normal.X), MathF.Abs(normal.Y), MathF.Abs(normal.Z));

                float u, v;
                if (absNormal.X >= absNormal.Y && absNormal.X >= absNormal.Z && absNormal.X > 0f)
                {
                    // Dominant X - project onto the YZ plane.
                    u = (position.Z - min.Z) / size.Z;
                    v = (position.Y - min.Y) / size.Y;
                }
                else if (absNormal.Z >= absNormal.Y && absNormal.Z > 0f)
                {
                    // Dominant Z - project onto the XY plane.
                    u = (position.X - min.X) / size.X;
                    v = (position.Y - min.Y) / size.Y;
                }
                else
                {
                    // Dominant Y (or a zero normal) - project onto the XZ plane.
                    u = (position.X - min.X) / size.X;
                    v = (position.Z - min.Z) / size.Z;
                }

                mesh.SetVertexUV(i, new Vector2(u, v));
            }
        }

        /// <summary>Standard equirectangular mapping relative to the mesh's own bounds
        /// center - see <see cref="UVProjectionMode.Spherical"/>'s own remarks. A vertex
        /// exactly at the center (a degenerate, zero-radius case) falls back to +Y
        /// rather than normalizing a zero vector into NaN.</summary>
        private static void ApplySpherical(Mesh mesh)
        {
            var (min, max) = mesh.GetBounds();
            var center = (min + max) / 2f;

            for (var i = 0; i < mesh.Vertices.Count; i++)
            {
                var offset = mesh.Vertices[i].Position - center;
                var direction = offset.LengthSquared() > float.Epsilon ? Vector3.Normalize(offset) : Vector3.UnitY;

                var u = 0.5f + MathF.Atan2(direction.Z, direction.X) / (2f * MathF.PI);
                var v = 0.5f - MathF.Asin(Math.Clamp(direction.Y, -1f, 1f)) / MathF.PI;

                mesh.SetVertexUV(i, new Vector2(u, v));
            }
        }
    }
}
