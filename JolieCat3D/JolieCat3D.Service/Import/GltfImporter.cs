using System.Numerics;
using JolieCat3D.Core.Geometry;
using JolieCat3D.Core.Numerics;
using JolieCat3D.Core.Scene;
using SharpGLTF.Schema2;
using CoreMaterial = JolieCat3D.Core.Materials.Material;
using CoreMesh = JolieCat3D.Core.Geometry.Mesh;
using CoreNode = JolieCat3D.Core.Scene.Node;
using CoreScene = JolieCat3D.Core.Scene.Scene3D;
using GltfMaterial = SharpGLTF.Schema2.Material;
using GltfNode = SharpGLTF.Schema2.Node;

namespace JolieCat3D.Service.Import
{
    /// <summary>
    /// Loads a <c>.glb</c>/<c>.gltf</c> file into a <see cref="CoreScene"/> via
    /// <c>SharpGLTF.Toolkit</c> - the read-side counterpart to <see cref="Export.GltfExporter"/>,
    /// which this class deliberately mirrors section-for-section (node graph, then mesh/
    /// material, then camera/light) so the two stay easy to compare.
    ///
    /// Deliberately, disclosed scope cuts (matching this project's own established
    /// precedent - see <c>MeshFileService</c>'s own remarks on why <c>.fbx</c> isn't
    /// supported at all): a <see cref="Skin"/> (skeletal skinning), an
    /// <see cref="Animation"/>, and any morph target are all read straight past without
    /// being applied - a skinned node's mesh still imports as a plain STATIC mesh at its
    /// glTF-authored rest pose, with no <see cref="Node.SkinBinding"/>/joints attached at
    /// all, and no <c>AnimationTimeline</c> track is ever produced from a glTF
    /// <see cref="Animation"/>. Wiring either up is a real, separate piece of work (this
    /// project's own timeline/skinning data model would need its own conversion from
    /// glTF's rather than just being a reverse of <see cref="Export.GltfExporter"/>'s own
    /// forward mapping) - out of scope for "open and render an imported model's meshes/
    /// materials", which is what this class actually does completely and correctly.
    /// </summary>
    public static class GltfImporter
    {
        /// <summary>glTF's own camera/light convention points local -Z "forward", while
        /// this project's own <see cref="Node.GetWorldForward"/> convention points local
        /// +Z (see <see cref="Export.GltfExporter.CreateLookDirectionFix"/>'s own
        /// remarks - this is that same fix, applied in reverse). Composing the imported
        /// node's own decomposed rotation with an extra 180-degree spin around Y before
        /// assigning it as this project's own <see cref="Node.LocalRotation"/> makes the
        /// resulting <see cref="Node.GetWorldForward"/> match whatever direction the
        /// glTF file's own -Z convention actually pointed the camera/light - a general
        /// fix that works for ANY glTF file (this project's own previously-exported
        /// ones included, not just something that specifically detects and collapses
        /// the exporter's own "$CameraLookFix"/"$LightLookFix" helper child node).</summary>
        private static readonly Quaternion LookDirectionFix = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI);

        public static CoreScene Import(string filePath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
            if (!File.Exists(filePath)) throw new FileNotFoundException("glTF/GLB file not found.", filePath);

            var model = ModelRoot.Load(filePath);
            var scene = new CoreScene(Path.GetFileNameWithoutExtension(filePath));

            var materialCache = new Dictionary<GltfMaterial, CoreMaterial>();
            // Embedded/base64 image bytes get written out here so Material's own
            // DiffuseTexturePath/NormalTexturePath/MetallicRoughnessTexturePath (plain
            // file paths - see GltfExporter's own remarks on why) have a real file to
            // point at; named after the source file so re-importing the same .glb twice
            // reuses the same extracted files rather than endlessly accumulating copies.
            var textureDirectory = Path.Combine(Path.GetTempPath(), "JolieCat3D_GltfImport", Path.GetFileNameWithoutExtension(filePath));
            var extractedImagePaths = new Dictionary<Image, string>();

            // The default scene's own top-level nodes if the file declares one (the
            // overwhelmingly common case); every node with no visual parent at all
            // otherwise - a glTF file is technically permitted to define nodes without
            // ever placing them into any Scene.
            var rootGltfNodes = (model.DefaultScene ?? model.LogicalScenes.FirstOrDefault())?.VisualChildren
                ?? model.LogicalNodes.Where(n => n.VisualParent is null);

            foreach (var gltfNode in rootGltfNodes)
                scene.AddRootNode(BuildNode(gltfNode, materialCache, extractedImagePaths, textureDirectory));

            return scene;
        }

        // ============================= Node graph =============================

        private static CoreNode BuildNode(
            GltfNode gltfNode, Dictionary<GltfMaterial, CoreMaterial> materialCache,
            Dictionary<Image, string> extractedImagePaths, string textureDirectory)
        {
            var node = new CoreNode(gltfNode.Name ?? "Node");

            var transform = gltfNode.LocalTransform;
            node.LocalPosition = transform.Translation;
            node.LocalScale = transform.Scale;
            // Only a Camera/Light-carrying node (and only when it has no Mesh of its own
            // to accidentally rotate along with it - see LookDirectionFix's own remarks)
            // gets the extra 180-degree correction; a plain mesh/pivot node's rotation
            // means the same thing in both conventions.
            var needsLookFix = gltfNode.Mesh is null && (gltfNode.Camera is not null || gltfNode.PunctualLight is not null);
            node.LocalRotation = needsLookFix ? transform.Rotation * LookDirectionFix : transform.Rotation;

            if (gltfNode.Mesh is { Primitives.Count: > 0 } gltfMesh)
            {
                // Every primitive beyond the first becomes an additional CHILD node
                // (named "<Mesh>_Part2", "_Part3", ...) rather than trying to merge
                // several different materials onto one Mesh.Material - a disclosed
                // simplification (this project's own Multi-Material Support/
                // Polygon.MaterialSlotIndex mechanism is authored/edited live in this
                // app's own UI, not round-tripped through any importer/exporter yet -
                // see Mesh.MaterialSlots' own remarks), but a lossless one: every
                // primitive's own geometry and material still imports, just as a
                // sibling node instead of a single combined mesh.
                node.Mesh = BuildMesh(gltfMesh.Primitives[0], gltfMesh.Name ?? gltfNode.Name ?? "Mesh", materialCache, extractedImagePaths, textureDirectory);

                for (var i = 1; i < gltfMesh.Primitives.Count; i++)
                {
                    var partName = $"{gltfMesh.Name ?? gltfNode.Name ?? "Mesh"}_Part{i + 1}";
                    node.AddChild(new CoreNode(partName) { Mesh = BuildMesh(gltfMesh.Primitives[i], partName, materialCache, extractedImagePaths, textureDirectory) });
                }
            }

            if (gltfNode.Camera is { } camera)
                node.Camera = BuildCamera(camera);

            if (gltfNode.PunctualLight is { } light)
                node.Light = BuildLight(light);

            foreach (var child in gltfNode.VisualChildren)
                node.AddChild(BuildNode(child, materialCache, extractedImagePaths, textureDirectory));

            return node;
        }

        // ============================= Mesh / Material =============================

        private static CoreMesh BuildMesh(
            MeshPrimitive primitive, string name,
            Dictionary<GltfMaterial, CoreMaterial> materialCache,
            Dictionary<Image, string> extractedImagePaths, string textureDirectory)
        {
            var mesh = new CoreMesh(name) { Material = GetOrCreateMaterial(primitive.Material, materialCache, extractedImagePaths, textureDirectory) };

            var positions = primitive.GetVertexAccessor("POSITION")?.AsVector3Array();
            if (positions is null) return mesh; // No POSITION at all - nothing this primitive can contribute.

            var normals = primitive.GetVertexAccessor("NORMAL")?.AsVector3Array();
            var uvs = primitive.GetVertexAccessor("TEXCOORD_0")?.AsVector2Array();
            // Alpha defaults to 1 for a COLOR_0 accessor storing only RGB (no 4th
            // component) - matching glTF's own spec default for a missing alpha there.
            var colors = primitive.GetVertexAccessor("COLOR_0")?.AsColorArray(1f);

            for (var i = 0; i < positions.Count; i++)
            {
                var normal = normals is { Count: > 0 } ? normals[i] : Vector3.Zero;
                var uv = uvs is { Count: > 0 } ? uvs[i] : Vector2.Zero;
                // Vertex's own constructor already maps an omitted/default Color4 to
                // White (see its own remarks) - a Color4.Transparent-black placeholder
                // would read as "hide this vertex", not "no color data supplied".
                var color = colors is { Count: > 0 } ? new Color4(colors[i]) : default;
                mesh.AddVertex(new Vertex(positions[i], normal, uv, color));
            }

            // GetTriangleIndices() already triangulates regardless of the primitive's own
            // DrawPrimitiveType (TRIANGLES/TRIANGLE_STRIP/TRIANGLE_FAN all normalize to
            // the same (A,B,C) triples here) - no separate draw-mode branch needed.
            foreach (var (a, b, c) in primitive.GetTriangleIndices())
                mesh.AddTriangle(a, b, c);

            if (normals is null) mesh.RecalculateNormals();
            return mesh;
        }

        private static CoreMaterial GetOrCreateMaterial(
            GltfMaterial? gltfMaterial, Dictionary<GltfMaterial, CoreMaterial> cache,
            Dictionary<Image, string> extractedImagePaths, string textureDirectory)
        {
            if (gltfMaterial is null) return CoreMaterial.CreateDefault();
            if (cache.TryGetValue(gltfMaterial, out var existing)) return existing;

            var material = new CoreMaterial(string.IsNullOrWhiteSpace(gltfMaterial.Name) ? "Material" : gltfMaterial.Name);

            if (gltfMaterial.FindChannel("BaseColor") is { } baseColor)
            {
                var color = baseColor.Color;
                material.DiffuseColor = new Color4(color.X, color.Y, color.Z);
                // OPAQUE/MASK both mean "ignore whatever alpha the base color carries,
                // this surface is fully opaque" per the glTF spec itself - only BLEND
                // actually uses it as a real opacity.
                material.Opacity = gltfMaterial.Alpha == AlphaMode.BLEND ? color.W : 1f;

                if (TryExtractImage(baseColor.Texture, "BaseColor", extractedImagePaths, textureDirectory) is { } baseColorPath)
                    material.DiffuseTexturePath = baseColorPath;
            }

            if (gltfMaterial.FindChannel("MetallicRoughness") is { } metallicRoughness)
            {
                material.Metallic = metallicRoughness.GetFactor("MetallicFactor");
                material.Roughness = metallicRoughness.GetFactor("RoughnessFactor");

                if (TryExtractImage(metallicRoughness.Texture, "ORM", extractedImagePaths, textureDirectory) is { } ormPath)
                    material.MetallicRoughnessTexturePath = ormPath;
            }

            if (gltfMaterial.FindChannel("Normal") is { } normal &&
                TryExtractImage(normal.Texture, "Normal", extractedImagePaths, textureDirectory) is { } normalPath)
                material.NormalTexturePath = normalPath;

            cache[gltfMaterial] = material;
            return material;
        }

        /// <summary>Writes <paramref name="texture"/>'s own <see cref="Image.Content"/>
        /// bytes out to a real file under <paramref name="textureDirectory"/> (creating
        /// it if needed) and returns that path - null for no texture at all. Cached per
        /// <see cref="Image"/> (not just per texture path) within a single
        /// <see cref="Import"/> call, the same "don't re-extract the same image twice"
        /// reasoning <see cref="Export.GltfExporter.TryLoadImage"/> already applies in
        /// reverse.</summary>
        private static string? TryExtractImage(
            Texture? texture, string channelName, Dictionary<Image, string> extractedImagePaths, string textureDirectory)
        {
            if (texture?.PrimaryImage is not { } image) return null;
            if (extractedImagePaths.TryGetValue(image, out var cached)) return cached;

            try
            {
                Directory.CreateDirectory(textureDirectory);
                // MemoryImage.FileExtension returns a BARE extension with no leading dot
                // ("png", not ".png") - MemoryImage.SaveToFile itself, though, demands
                // the filename it's given end in a dotted extension matching the
                // image's own real format, throwing otherwise.
                var extension = string.IsNullOrEmpty(image.Content.FileExtension) ? "png" : image.Content.FileExtension;
                var path = Path.Combine(textureDirectory, $"{channelName}_{extractedImagePaths.Count}.{extension}");
                image.Content.SaveToFile(path);
                extractedImagePaths[image] = path;
                return path;
            }
            catch (Exception)
            {
                // Same "missing/unreadable texture degrades visibly rather than
                // crashing the whole import" tolerance GltfExporter.TryLoadImage and
                // Engine.Geometry.MaterialFactory already both apply.
                return null;
            }
        }

        // ============================= Camera / Light =============================

        private static CameraData BuildCamera(Camera camera)
        {
            var data = new CameraData();

            if (camera.Settings is CameraPerspective perspective)
            {
                data.ProjectionMode = CameraProjectionMode.Perspective;
                data.FieldOfView = perspective.VerticalFOV * 180f / MathF.PI;
                data.NearPlaneDistance = perspective.ZNear;
                data.FarPlaneDistance = perspective.ZFar;
            }
            else if (camera.Settings is CameraOrthographic orthographic)
            {
                data.ProjectionMode = CameraProjectionMode.Orthographic;
                data.OrthographicWidth = orthographic.XMag * 2f;
                data.NearPlaneDistance = orthographic.ZNear;
                data.FarPlaneDistance = orthographic.ZFar;
            }

            return data;
        }

        private static LightData BuildLight(PunctualLight light)
        {
            var data = new LightData
            {
                Type = light.LightType switch
                {
                    PunctualLightType.Point => LightType.Point,
                    PunctualLightType.Spot => LightType.Spot,
                    _ => LightType.Directional,
                },
                Color = new Color4(light.Color.X, light.Color.Y, light.Color.Z),
                // The exact inverse of GltfExporter.LightIntensityToGltfScale.
                Intensity = light.Intensity / Export.GltfExporter.LightIntensityToGltfScale,
                Range = light.Range,
            };

            if (data.Type == LightType.Spot)
                // The exact inverse of GltfExporter.BuildLight's own OuterConeAngle
                // formula (full-angle degrees <-> half-angle radians).
                data.SpotAngle = light.OuterConeAngle * 360f / MathF.PI;

            return data;
        }
    }
}
