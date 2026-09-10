using System.Numerics;
using JolieCat3D.Core.Materials;
using JolieCat3D.Core.Scene;
using static JolieCat3D.Service.Wavefront.ObjTextHelpers;

namespace JolieCat3D.Service.Wavefront
{
    /// <summary>
    /// Writes a <see cref="Scene3D"/> to a Wavefront <c>.obj</c> file, plus a companion
    /// <c>.mtl</c> (see <see cref="MtlFile"/>) for every distinct <see cref="Material"/>
    /// referenced. OBJ has no scene-graph/hierarchy or per-instance transform concept of
    /// its own - every node's geometry is baked into world space at export time (via
    /// <see cref="Node.GetWorldTransform"/>, with normals correctly transformed by the
    /// inverse-transpose of that matrix's linear part rather than the matrix itself, so a
    /// non-uniformly-scaled node's normals still come out correct) - one <c>o</c> object
    /// per node that actually has a mesh. Quads/n-gons are written as their own face line
    /// rather than triangulated (<see cref="Core.Geometry.Mesh.Faces"/> and
    /// <see cref="Core.Geometry.Mesh.Polygons"/> separately, not the always-triangulated
    /// <see cref="Core.Geometry.Mesh.GetRenderFaces"/>), so a quad round-trips as a quad
    /// through another tool that also prefers them (Blender included).
    /// </summary>
    public static class ObjExporter
    {
        public static void Export(Scene3D scene, string filePath)
        {
            ArgumentNullException.ThrowIfNull(scene);
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

            var fullPath = Path.GetFullPath(filePath);
            var directory = Path.GetDirectoryName(fullPath) ?? ".";
            var mtlFileName = Path.GetFileNameWithoutExtension(fullPath) + ".mtl";

            var materialNames = new Dictionary<Material, string>();
            var materialsToWrite = new List<(string Name, Material Material)>();
            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            using (var writer = new StreamWriter(fullPath, append: false))
            {
                writer.WriteLine("# Exported by JolieCat3D");
                writer.WriteLine($"mtllib {mtlFileName}");

                var vertexOffset = 0;
                var objectIndex = 0;

                foreach (var node in scene.Traverse())
                {
                    if (node.Mesh is not { } mesh || mesh.Vertices.Count == 0) continue;
                    objectIndex++;

                    writer.WriteLine();
                    writer.WriteLine($"o {SanitizeName(node.Name, $"Object{objectIndex}")}");

                    if (mesh.Material is { } material)
                        writer.WriteLine($"usemtl {GetOrRegisterMaterialName(material, materialNames, materialsToWrite, usedNames)}");

                    var world = node.GetWorldTransform();
                    if (!Matrix4x4.Invert(world, out var inverseWorld)) inverseWorld = Matrix4x4.Identity;
                    var normalMatrix = Matrix4x4.Transpose(inverseWorld);

                    foreach (var vertex in mesh.Vertices)
                    {
                        var worldPosition = Vector3.Transform(vertex.Position, world);
                        writer.WriteLine($"v {FormatFloat(worldPosition.X)} {FormatFloat(worldPosition.Y)} {FormatFloat(worldPosition.Z)}");
                    }

                    foreach (var vertex in mesh.Vertices)
                        writer.WriteLine($"vt {FormatFloat(vertex.UV.X)} {FormatFloat(vertex.UV.Y)}");

                    foreach (var vertex in mesh.Vertices)
                    {
                        var worldNormal = Vector3.TransformNormal(vertex.Normal, normalMatrix);
                        if (worldNormal.LengthSquared() > float.Epsilon) worldNormal = Vector3.Normalize(worldNormal);
                        writer.WriteLine($"vn {FormatFloat(worldNormal.X)} {FormatFloat(worldNormal.Y)} {FormatFloat(worldNormal.Z)}");
                    }

                    foreach (var face in mesh.Faces)
                        WriteFaceLine(writer, vertexOffset, new[] { face.A, face.B, face.C });

                    foreach (var polygon in mesh.Polygons)
                        WriteFaceLine(writer, vertexOffset, polygon.Indices);

                    vertexOffset += mesh.Vertices.Count;
                }
            }

            if (materialsToWrite.Count > 0)
                MtlFile.Save(Path.Combine(directory, mtlFileName), materialsToWrite);
        }

        private static void WriteFaceLine(TextWriter writer, int offset, IReadOnlyList<int> indices)
        {
            writer.Write('f');
            foreach (var index in indices)
            {
                var objIndex = index + offset + 1; // OBJ indices are 1-based.
                writer.Write($" {objIndex}/{objIndex}/{objIndex}");
            }
            writer.WriteLine();
        }

        private static string GetOrRegisterMaterialName(
            Material material, Dictionary<Material, string> materialNames,
            List<(string Name, Material Material)> materialsToWrite, HashSet<string> usedNames)
        {
            if (materialNames.TryGetValue(material, out var existingName)) return existingName;

            var baseName = SanitizeName(material.Name, $"Material{materialsToWrite.Count + 1}");
            var name = baseName;
            var suffix = 2;
            // Two different Material instances that happen to share a Name each still
            // need their own unique entry in the .mtl file.
            while (!usedNames.Add(name)) name = $"{baseName}_{suffix++}";

            materialNames[material] = name;
            materialsToWrite.Add((name, material));
            return name;
        }
    }
}
