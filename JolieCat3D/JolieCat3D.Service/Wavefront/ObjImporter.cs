using System.Numerics;
using JolieCat3D.Core.Geometry;
using JolieCat3D.Core.Materials;
using JolieCat3D.Core.Scene;
using static JolieCat3D.Service.Wavefront.ObjTextHelpers;

namespace JolieCat3D.Service.Wavefront
{
    /// <summary>
    /// Loads a Wavefront <c>.obj</c> file into a <see cref="Scene3D"/> - one root
    /// <see cref="Node"/>/<see cref="Mesh"/> per <c>o</c>/<c>g</c> object/group the file
    /// declares (or one for the whole file, if it declares none at all). OBJ's own
    /// <c>v</c>/<c>vt</c>/<c>vn</c> lists are global across the whole file, but each
    /// <see cref="Mesh"/> needs its own local 0-based vertex list - a per-object cache
    /// keyed by the exact (position, uv, normal) index triple a face line references is
    /// what remaps "global OBJ attribute set" to "local Mesh vertex", reusing one Vertex
    /// for every face that references the exact same triple and creating a new one for
    /// any other combination (the standard, necessary approach for OBJ, since a single
    /// position can need different Vertex instances if different faces pair it with
    /// different normals/UVs there).
    /// </summary>
    public static class ObjImporter
    {
        public static Scene3D Import(string filePath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
            if (!File.Exists(filePath)) throw new FileNotFoundException("OBJ file not found.", filePath);

            var positions = new List<Vector3>();
            var uvs = new List<Vector2>();
            var normals = new List<Vector3>();
            var materials = new Dictionary<string, Material>();

            var scene = new Scene3D(Path.GetFileNameWithoutExtension(filePath));
            Mesh? currentMesh = null;
            var vertexCache = new Dictionary<(int Position, int Uv, int Normal), int>();
            var objectCount = 0;

            void StartNewObject(string name)
            {
                currentMesh = new Mesh(name);
                scene.AddRootNode(new Node(name) { Mesh = currentMesh });
                vertexCache.Clear();
                objectCount++;
            }

            var directory = Path.GetDirectoryName(Path.GetFullPath(filePath)) ?? ".";

            foreach (var rawLine in File.ReadLines(filePath))
            {
                var line = StripComment(rawLine).Trim();
                if (line.Length == 0) continue;

                var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0) continue;

                switch (parts[0])
                {
                    case "v" when parts.Length >= 4:
                        positions.Add(new Vector3(ParseFloat(parts[1]), ParseFloat(parts[2]), ParseFloat(parts[3])));
                        break;

                    case "vt" when parts.Length >= 3:
                        uvs.Add(new Vector2(ParseFloat(parts[1]), ParseFloat(parts[2])));
                        break;

                    case "vn" when parts.Length >= 4:
                        normals.Add(Vector3.Normalize(new Vector3(ParseFloat(parts[1]), ParseFloat(parts[2]), ParseFloat(parts[3]))));
                        break;

                    case "o" or "g":
                        StartNewObject(parts.Length > 1 ? parts[1] : $"Object{objectCount + 1}");
                        break;

                    case "mtllib" when parts.Length > 1:
                        var mtlPath = Path.Combine(directory, parts[1]);
                        if (File.Exists(mtlPath))
                            foreach (var (name, material) in MtlFile.Load(mtlPath))
                                materials[name] = material;
                        break;

                    case "usemtl" when parts.Length > 1 && currentMesh is not null:
                        materials.TryGetValue(parts[1], out var usedMaterial);
                        currentMesh.Material = usedMaterial;
                        break;

                    case "f" when parts.Length >= 4:
                        // A face line before any "o"/"g" is a file with one implicit object.
                        if (currentMesh is null) StartNewObject("Object");
                        AddFace(currentMesh!, parts, positions, uvs, normals, vertexCache);
                        break;
                }
            }

            // No "vn" lines anywhere in the file at all - every vertex above got a
            // Vector3.Zero placeholder normal, so derive real ones instead of leaving
            // the mesh unlit-looking.
            if (normals.Count == 0)
                foreach (var node in scene.Traverse())
                    node.Mesh?.RecalculateNormals();

            return scene;
        }

        private static void AddFace(
            Mesh mesh, string[] parts, List<Vector3> positions, List<Vector2> uvs, List<Vector3> normals,
            Dictionary<(int Position, int Uv, int Normal), int> vertexCache)
        {
            var faceIndices = new List<int>(parts.Length - 1);

            for (var i = 1; i < parts.Length; i++)
            {
                var (positionIndex, uvIndex, normalIndex) = ParseFaceVertex(parts[i], positions.Count, uvs.Count, normals.Count);
                var key = (positionIndex, uvIndex, normalIndex);

                if (!vertexCache.TryGetValue(key, out var localIndex))
                {
                    var uv = uvIndex >= 0 ? uvs[uvIndex] : Vector2.Zero;
                    var normal = normalIndex >= 0 ? normals[normalIndex] : Vector3.Zero;
                    localIndex = mesh.AddVertex(new Vertex(positions[positionIndex], normal, uv));
                    vertexCache[key] = localIndex;
                }

                faceIndices.Add(localIndex);
            }

            // A triangle becomes a Face (the ready-to-render kind); anything larger stays
            // a Polygon, preserving the file's own quads/n-gons rather than silently
            // triangulating on import - GetRenderFaces() triangulates only when something
            // actually needs triangles (the Engine's own geometry adapter).
            if (faceIndices.Count == 3) mesh.AddTriangle(faceIndices[0], faceIndices[1], faceIndices[2]);
            else mesh.AddPolygon(new Polygon(faceIndices));
        }

        /// <summary>Parses one <c>f</c> line's <c>v</c>, <c>v/vt</c>, <c>v//vn</c>, or
        /// <c>v/vt/vn</c> token into 0-based indices (-1 for an omitted <c>vt</c>/<c>vn</c>).
        /// Handles OBJ's negative/relative index convention (a negative value counts back
        /// from whatever's been parsed so far, e.g. -1 = "the vertex just declared") as
        /// well as the more common positive/absolute one.</summary>
        private static (int Position, int Uv, int Normal) ParseFaceVertex(string token, int positionCount, int uvCount, int normalCount)
        {
            var segments = token.Split('/');
            var positionIndex = ResolveIndex(segments[0], positionCount);
            var uvIndex = segments.Length > 1 && segments[1].Length > 0 ? ResolveIndex(segments[1], uvCount) : -1;
            var normalIndex = segments.Length > 2 && segments[2].Length > 0 ? ResolveIndex(segments[2], normalCount) : -1;
            return (positionIndex, uvIndex, normalIndex);
        }

        private static int ResolveIndex(string token, int count)
        {
            var value = int.Parse(token, System.Globalization.CultureInfo.InvariantCulture);
            return value > 0 ? value - 1 : count + value;
        }
    }
}
