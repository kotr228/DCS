using System.Globalization;
using System.Numerics;
using JolieCat3D.Core.Geometry;
using JolieCat3D.Core.Scene;

namespace JolieCat3D.Service.Stl
{
    /// <summary>
    /// Writes a <see cref="Mesh"/> or a whole <see cref="Scene3D"/> to an <c>.stl</c>
    /// file - ASCII text only, deliberately: STL's binary form is more compact but this
    /// project has no way to verify a hand-written binary encoder byte-for-byte against
    /// a real STL-consuming tool in this environment, where a subtly wrong binary writer
    /// could produce a file that "looks right" in code review but is silently corrupt.
    /// ASCII STL is the same triangle data in an unambiguous, always-valid text form -
    /// the "basic" STL export this project actually commits to. (Reading a binary STL,
    /// by contrast, is exactly as safe as reading any other file - see
    /// <see cref="StlImporter"/>, which supports both forms.) STL has no scene-graph
    /// concept of its own, so <see cref="ExportScene"/> bakes every node's geometry into
    /// world space and merges it all into one flat triangle list, the same way
    /// <c>ObjExporter</c> has to for the same reason.
    /// </summary>
    public static class StlExporter
    {
        public static void Export(Mesh mesh, string filePath, string? solidName = null)
        {
            ArgumentNullException.ThrowIfNull(mesh);
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

            using var writer = new StreamWriter(filePath, append: false);
            WriteAscii(writer, SanitizeName(solidName ?? mesh.Name), GetTriangles(mesh, Matrix4x4.Identity));
        }

        public static void ExportScene(Scene3D scene, string filePath, string? solidName = null)
        {
            ArgumentNullException.ThrowIfNull(scene);
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

            var triangles = scene.Traverse()
                .Where(node => node.Mesh is not null)
                .SelectMany(node => GetTriangles(node.Mesh!, node.GetWorldTransform()));

            using var writer = new StreamWriter(filePath, append: false);
            WriteAscii(writer, SanitizeName(solidName ?? scene.Name), triangles);
        }

        private static void WriteAscii(TextWriter writer, string solidName, IEnumerable<(Vector3 Normal, Vector3 A, Vector3 B, Vector3 C)> triangles)
        {
            writer.WriteLine($"solid {solidName}");

            foreach (var (normal, a, b, c) in triangles)
            {
                writer.WriteLine($"  facet normal {Fmt(normal.X)} {Fmt(normal.Y)} {Fmt(normal.Z)}");
                writer.WriteLine("    outer loop");
                writer.WriteLine($"      vertex {Fmt(a.X)} {Fmt(a.Y)} {Fmt(a.Z)}");
                writer.WriteLine($"      vertex {Fmt(b.X)} {Fmt(b.Y)} {Fmt(b.Z)}");
                writer.WriteLine($"      vertex {Fmt(c.X)} {Fmt(c.Y)} {Fmt(c.Z)}");
                writer.WriteLine("    endloop");
                writer.WriteLine("  endfacet");
            }

            writer.WriteLine($"endsolid {solidName}");
        }

        /// <summary>Every triangle in <paramref name="mesh"/> (via <see cref="Mesh.GetRenderFaces"/> -
        /// STL has no quad/n-gon concept at all, unlike OBJ, so this is the one exporter
        /// in this project that always fully triangulates), transformed by
        /// <paramref name="transform"/>. The facet normal is computed fresh from the
        /// already-transformed triangle's own geometry (a plain cross product), not by
        /// transforming <see cref="Vertex.Normal"/> through the same matrix - correct
        /// even under a non-uniform scale without needing a separate inverse-transpose
        /// normal matrix the way transforming an existing normal vector would (see
        /// <c>ObjExporter</c>, which does need one, since it exports the mesh's own
        /// per-vertex normals rather than recomputing a flat one per triangle).</summary>
        private static IEnumerable<(Vector3 Normal, Vector3 A, Vector3 B, Vector3 C)> GetTriangles(Mesh mesh, Matrix4x4 transform)
        {
            foreach (var face in mesh.GetRenderFaces())
            {
                var a = Vector3.Transform(mesh.Vertices[face.A].Position, transform);
                var b = Vector3.Transform(mesh.Vertices[face.B].Position, transform);
                var c = Vector3.Transform(mesh.Vertices[face.C].Position, transform);

                var geometric = Vector3.Cross(b - a, c - a);
                var normal = geometric.LengthSquared() > float.Epsilon ? Vector3.Normalize(geometric) : Vector3.UnitY;

                yield return (normal, a, b, c);
            }
        }

        private static string Fmt(float value) => value.ToString("0.######", CultureInfo.InvariantCulture);

        private static string SanitizeName(string name)
        {
            var chars = name.ToCharArray();
            for (var i = 0; i < chars.Length; i++)
                if (char.IsWhiteSpace(chars[i])) chars[i] = '_';

            var sanitized = new string(chars);
            return sanitized.Length == 0 ? "Model" : sanitized;
        }
    }
}
