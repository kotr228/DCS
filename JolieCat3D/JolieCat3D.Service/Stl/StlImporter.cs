using System.Globalization;
using System.Numerics;
using JolieCat3D.Core.Geometry;

namespace JolieCat3D.Service.Stl
{
    /// <summary>
    /// Loads an <c>.stl</c> file (either of the two forms real STL files come in - plain
    /// ASCII text, or the far more common compact binary layout) into a single
    /// <see cref="Mesh"/>. STL is a flat triangle soup with no shared-vertex, object,
    /// material, or UV concept at all - every triangle gets its own 3 fresh
    /// <see cref="Vertex"/> instances (no cross-triangle vertex cache the way
    /// <c>ObjImporter</c> needs one), matching the format's own nature rather than
    /// trying to impose sharing it was never designed to carry.
    /// </summary>
    public static class StlImporter
    {
        public static Mesh Import(string filePath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
            if (!File.Exists(filePath)) throw new FileNotFoundException("STL file not found.", filePath);

            var name = Path.GetFileNameWithoutExtension(filePath);
            var bytes = File.ReadAllBytes(filePath);

            return LooksBinary(bytes) ? ImportBinary(bytes, name) : ImportAscii(filePath, name);
        }

        /// <summary>
        /// The reliable way to tell binary STL from ASCII - not just checking whether the
        /// file starts with the text "solid" (a binary file's own 80-byte free-form
        /// header can legally start with that exact text too, a well-known real-world
        /// gotcha). A binary STL's total length is always exactly
        /// 84 + 50 * (triangle count read from bytes 80-83) - checking that the file's
        /// actual length matches what that formula predicts is decisive either way.
        /// </summary>
        private static bool LooksBinary(byte[] bytes)
        {
            if (bytes.Length < 84) return false;

            var triangleCount = BitConverter.ToUInt32(bytes, 80);
            var expectedBinaryLength = 84L + 50L * triangleCount;
            return expectedBinaryLength == bytes.Length;
        }

        private static Mesh ImportBinary(byte[] bytes, string name)
        {
            var mesh = new Mesh(name);
            var triangleCount = BitConverter.ToUInt32(bytes, 80);
            var offset = 84;

            for (var i = 0; i < triangleCount; i++)
            {
                var normal = ReadVector3(bytes, offset); offset += 12;
                var a = ReadVector3(bytes, offset); offset += 12;
                var b = ReadVector3(bytes, offset); offset += 12;
                var c = ReadVector3(bytes, offset); offset += 12;
                offset += 2; // "attribute byte count" - vendor-specific, unused here.

                AddTriangle(mesh, a, b, c, normal);
            }

            return mesh;
        }

        private static Vector3 ReadVector3(byte[] bytes, int offset) => new(
            BitConverter.ToSingle(bytes, offset),
            BitConverter.ToSingle(bytes, offset + 4),
            BitConverter.ToSingle(bytes, offset + 8));

        private static Mesh ImportAscii(string filePath, string name)
        {
            var mesh = new Mesh(name);
            var normal = Vector3.Zero;
            var triangleVertices = new List<Vector3>(3);

            foreach (var rawLine in File.ReadLines(filePath))
            {
                var line = rawLine.Trim();
                if (line.Length == 0) continue;

                var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0) continue;

                if (parts[0].Equals("facet", StringComparison.OrdinalIgnoreCase) && parts.Length >= 5)
                {
                    normal = new Vector3(ParseFloat(parts[2]), ParseFloat(parts[3]), ParseFloat(parts[4]));
                    triangleVertices.Clear();
                }
                else if (parts[0].Equals("vertex", StringComparison.OrdinalIgnoreCase) && parts.Length >= 4)
                {
                    triangleVertices.Add(new Vector3(ParseFloat(parts[1]), ParseFloat(parts[2]), ParseFloat(parts[3])));
                    if (triangleVertices.Count == 3)
                        AddTriangle(mesh, triangleVertices[0], triangleVertices[1], triangleVertices[2], normal);
                }
            }

            return mesh;
        }

        private static void AddTriangle(Mesh mesh, Vector3 a, Vector3 b, Vector3 c, Vector3 normal)
        {
            // A missing/all-zero "facet normal" (some writers emit that and expect the
            // reader to derive it) falls back to the geometric one instead of baking in
            // a degenerate (0,0,0) normal.
            if (normal.LengthSquared() < float.Epsilon)
            {
                var geometric = Vector3.Cross(b - a, c - a);
                normal = geometric.LengthSquared() > float.Epsilon ? Vector3.Normalize(geometric) : Vector3.UnitY;
            }
            else
            {
                normal = Vector3.Normalize(normal);
            }

            var ia = mesh.AddVertex(new Vertex(a, normal));
            var ib = mesh.AddVertex(new Vertex(b, normal));
            var ic = mesh.AddVertex(new Vertex(c, normal));
            mesh.AddTriangle(ia, ib, ic);
        }

        private static float ParseFloat(string token) => float.Parse(token, CultureInfo.InvariantCulture);
    }
}
