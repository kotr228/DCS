using System.Numerics;
using System.Text.Json;
using JolieCat3D.Core.Geometry;
using JolieCat3D.Core.Materials;
using JolieCat3D.Core.Modifiers;
using JolieCat3D.Core.Numerics;
using JolieCat3D.Core.Scene;
using JolieCat3D.Service.Animation;

namespace JolieCat3D.Service.Project
{
    /// <summary>
    /// Saves/loads a COMPLETE JolieCat3D workspace - the whole <see cref="Scene3D"/>
    /// (every node's own transform, mesh geometry, material, and modifier stack) plus
    /// the whole <see cref="AnimationTimeline"/> - as a single ".jolie3d" JSON file (see
    /// <see cref="ProjectFileData"/>'s own remarks on exactly what that covers and why a
    /// plain mesh interchange format like OBJ/STL (<c>MeshFileService</c>) can't). This
    /// is the "Save Project"/"Open Project" half of the application, deliberately kept
    /// separate from <c>MeshFileService</c>'s own OBJ/STL Import/Export: those return/take
    /// just a <see cref="Scene3D"/> (the shape a MESH interchange format can actually
    /// hold), while this returns/takes a <see cref="Scene3D"/> AND an
    /// <see cref="AnimationTimeline"/> together, since a project's own animation
    /// (<see cref="AnimationTrack"/>s reference <see cref="Node"/>s, <see cref="TextureAnimationTrack"/>s
    /// reference <see cref="Material"/>s) only means anything alongside the exact scene
    /// it was authored against.
    /// </summary>
    public static class Jolie3DProjectSerializer
    {
        private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

        /// <summary>Writes <paramref name="scene"/> and <paramref name="timeline"/>
        /// together to <paramref name="filePath"/> - the whole node tree (recursively,
        /// see <see cref="ConvertNode"/>) plus the animation timeline (via
        /// <see cref="AnimationExporter.BuildExportData"/>, the exact same conversion
        /// <see cref="AnimationExporter.ExportJson"/> itself uses for its own standalone
        /// file).</summary>
        public static void Save(Scene3D scene, AnimationTimeline timeline, string filePath)
        {
            ArgumentNullException.ThrowIfNull(scene);
            ArgumentNullException.ThrowIfNull(timeline);
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

            var data = new ProjectFileData
            {
                SceneName = scene.Name,
                Animation = AnimationExporter.BuildExportData(timeline),
                ActiveCameraNodePath = scene.ActiveCamera is { } activeCamera ? NodePathResolver.GetPath(activeCamera) : null,
            };

            foreach (var root in scene.RootNodes)
                data.RootNodes.Add(ConvertNode(root));

            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            using var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write);
            JsonSerializer.Serialize(stream, data, WriteOptions);
        }

        /// <summary>Reads <paramref name="filePath"/> back into a brand new
        /// <see cref="Scene3D"/> and <see cref="AnimationTimeline"/> - the exact
        /// inverse of <see cref="Save"/>: every node's own transform/mesh/modifiers
        /// reconstructed (see <see cref="ConvertNodeData"/>), then the animation applied
        /// on top via <see cref="AnimationExporter.ApplyExportData"/> once the whole
        /// scene tree already exists (its own node-path/material-name resolution needs
        /// real <see cref="Node"/>/<see cref="Material"/> instances to resolve
        /// against).</summary>
        /// <exception cref="InvalidDataException"><paramref name="filePath"/> isn't
        /// valid JSON, deserializes to nothing at all, or names a modifier/mirror-axis
        /// this version of the format doesn't recognize.</exception>
        public static (Scene3D Scene, AnimationTimeline Timeline) Load(string filePath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

            ProjectFileData? data;
            try
            {
                using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
                data = JsonSerializer.Deserialize<ProjectFileData>(stream);
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException($"'{filePath}' is not a valid JolieCat3D project file.", ex);
            }

            if (data is null) throw new InvalidDataException($"'{filePath}' is not a valid JolieCat3D project file (empty).");

            var scene = new Scene3D(data.SceneName);
            foreach (var rootData in data.RootNodes)
                scene.AddRootNode(ConvertNodeData(rootData, filePath));

            scene.ActiveCamera = NodePathResolver.FindByPath(scene, data.ActiveCameraNodePath);

            var timeline = new AnimationTimeline();
            AnimationExporter.ApplyExportData(data.Animation, timeline, scene);

            return (scene, timeline);
        }

        // ============================= Node =============================

        private static ProjectNodeData ConvertNode(Node node)
        {
            var data = new ProjectNodeData
            {
                Name = node.Name,
                PositionX = node.LocalPosition.X,
                PositionY = node.LocalPosition.Y,
                PositionZ = node.LocalPosition.Z,
                RotationX = node.LocalRotation.X,
                RotationY = node.LocalRotation.Y,
                RotationZ = node.LocalRotation.Z,
                RotationW = node.LocalRotation.W,
                ScaleX = node.LocalScale.X,
                ScaleY = node.LocalScale.Y,
                ScaleZ = node.LocalScale.Z,
            };

            if (node.Mesh is { } mesh) data.Mesh = ConvertMesh(mesh);
            if (node.Camera is { } camera) data.Camera = ConvertCamera(camera);
            if (node.Light is { } light) data.Light = ConvertLight(light);
            foreach (var modifier in node.Modifiers) data.Modifiers.Add(ConvertModifier(modifier));
            foreach (var child in node.Children) data.Children.Add(ConvertNode(child));

            return data;
        }

        private static Node ConvertNodeData(ProjectNodeData data, string sourceFilePath)
        {
            var node = new Node(data.Name)
            {
                LocalPosition = new Vector3(data.PositionX, data.PositionY, data.PositionZ),
                LocalRotation = new Quaternion(data.RotationX, data.RotationY, data.RotationZ, data.RotationW),
                LocalScale = new Vector3(data.ScaleX, data.ScaleY, data.ScaleZ),
            };

            if (data.Mesh is { } meshData) node.Mesh = ConvertMeshData(meshData);
            if (data.Camera is { } cameraData) node.Camera = ConvertCameraData(cameraData);
            if (data.Light is { } lightData) node.Light = ConvertLightData(lightData);
            foreach (var modifierData in data.Modifiers) node.Modifiers.Add(ConvertModifierData(modifierData, sourceFilePath));
            foreach (var childData in data.Children) node.AddChild(ConvertNodeData(childData, sourceFilePath));

            return node;
        }

        // ============================= Camera =============================

        private static ProjectCameraData ConvertCamera(CameraData camera) => new()
        {
            ProjectionMode = camera.ProjectionMode.ToString(),
            FieldOfView = camera.FieldOfView,
            OrthographicWidth = camera.OrthographicWidth,
            NearPlaneDistance = camera.NearPlaneDistance,
            FarPlaneDistance = camera.FarPlaneDistance,
        };

        private static CameraData ConvertCameraData(ProjectCameraData data) => new()
        {
            ProjectionMode = Enum.TryParse<CameraProjectionMode>(data.ProjectionMode, out var mode) ? mode : CameraProjectionMode.Perspective,
            FieldOfView = data.FieldOfView,
            OrthographicWidth = data.OrthographicWidth,
            NearPlaneDistance = data.NearPlaneDistance,
            FarPlaneDistance = data.FarPlaneDistance,
        };

        // ============================= Light =============================

        private static ProjectLightData ConvertLight(LightData light) => new()
        {
            Type = light.Type.ToString(),
            ColorR = light.Color.R,
            ColorG = light.Color.G,
            ColorB = light.Color.B,
            ColorA = light.Color.A,
            Intensity = light.Intensity,
            Range = light.Range,
            SpotAngle = light.SpotAngle,
        };

        private static LightData ConvertLightData(ProjectLightData data) => new()
        {
            Type = Enum.TryParse<LightType>(data.Type, out var type) ? type : LightType.Directional,
            Color = new Color4(data.ColorR, data.ColorG, data.ColorB, data.ColorA),
            Intensity = data.Intensity,
            Range = data.Range,
            SpotAngle = data.SpotAngle,
        };

        // ============================= Mesh =============================

        private static ProjectMeshData ConvertMesh(Mesh mesh)
        {
            var data = new ProjectMeshData { Name = mesh.Name };

            foreach (var vertex in mesh.Vertices)
            {
                data.Vertices.Add(new ProjectVertexData
                {
                    PositionX = vertex.Position.X,
                    PositionY = vertex.Position.Y,
                    PositionZ = vertex.Position.Z,
                    NormalX = vertex.Normal.X,
                    NormalY = vertex.Normal.Y,
                    NormalZ = vertex.Normal.Z,
                    UVX = vertex.UV.X,
                    UVY = vertex.UV.Y,
                    ColorR = vertex.Color.R,
                    ColorG = vertex.Color.G,
                    ColorB = vertex.Color.B,
                    ColorA = vertex.Color.A,
                });
            }

            foreach (var face in mesh.Faces)
                data.Faces.Add(new ProjectFaceData { A = face.A, B = face.B, C = face.C });

            foreach (var polygon in mesh.Polygons)
                data.Polygons.Add(polygon.Indices.ToList());

            if (mesh.Material is { } material) data.Material = ConvertMaterial(material);

            return data;
        }

        private static Mesh ConvertMeshData(ProjectMeshData data)
        {
            var mesh = new Mesh(data.Name);

            foreach (var vertexData in data.Vertices)
            {
                mesh.AddVertex(new Vertex(
                    new Vector3(vertexData.PositionX, vertexData.PositionY, vertexData.PositionZ),
                    new Vector3(vertexData.NormalX, vertexData.NormalY, vertexData.NormalZ),
                    new Vector2(vertexData.UVX, vertexData.UVY),
                    new Color4(vertexData.ColorR, vertexData.ColorG, vertexData.ColorB, vertexData.ColorA)));
            }

            foreach (var faceData in data.Faces)
                mesh.AddTriangle(faceData.A, faceData.B, faceData.C);

            foreach (var polygonIndices in data.Polygons)
                mesh.AddPolygon(new Polygon(polygonIndices));

            if (data.Material is { } materialData) mesh.Material = ConvertMaterialData(materialData);

            return mesh;
        }

        // ============================= Material =============================

        private static ProjectMaterialData ConvertMaterial(Material material) => new()
        {
            Name = material.Name,
            DiffuseR = material.DiffuseColor.R,
            DiffuseG = material.DiffuseColor.G,
            DiffuseB = material.DiffuseColor.B,
            DiffuseA = material.DiffuseColor.A,
            SpecularR = material.SpecularColor.R,
            SpecularG = material.SpecularColor.G,
            SpecularB = material.SpecularColor.B,
            SpecularA = material.SpecularColor.A,
            SpecularPower = material.SpecularPower,
            Opacity = material.Opacity,
            Roughness = material.Roughness,
            Metallic = material.Metallic,
            DiffuseTexturePath = material.DiffuseTexturePath,
            DiffuseTextureOffsetX = material.DiffuseTextureOffset.X,
            DiffuseTextureOffsetY = material.DiffuseTextureOffset.Y,
            DiffuseTextureScaleX = material.DiffuseTextureScale.X,
            DiffuseTextureScaleY = material.DiffuseTextureScale.Y,
        };

        private static Material ConvertMaterialData(ProjectMaterialData data) => new(data.Name)
        {
            DiffuseColor = new Color4(data.DiffuseR, data.DiffuseG, data.DiffuseB, data.DiffuseA),
            SpecularColor = new Color4(data.SpecularR, data.SpecularG, data.SpecularB, data.SpecularA),
            SpecularPower = data.SpecularPower,
            Opacity = data.Opacity,
            Roughness = data.Roughness,
            Metallic = data.Metallic,
            DiffuseTexturePath = data.DiffuseTexturePath,
            DiffuseTextureOffset = new Vector2(data.DiffuseTextureOffsetX, data.DiffuseTextureOffsetY),
            DiffuseTextureScale = new Vector2(data.DiffuseTextureScaleX, data.DiffuseTextureScaleY),
        };

        // ============================= Modifier =============================

        /// <summary>Every concrete <see cref="Modifier"/> subtype this format knows how
        /// to serialize - throws for anything else (rather than silently dropping an
        /// unrecognized modifier from a saved project) so a future modifier type added
        /// to <c>JolieCat3D.Core</c> without a matching case added HERE fails loudly at
        /// save time, not silently loses data a user would only discover much later on
        /// reopening the file.</summary>
        /// <exception cref="NotSupportedException"><paramref name="modifier"/> is a
        /// type this serializer has no case for.</exception>
        private static ProjectModifierData ConvertModifier(Modifier modifier) => modifier switch
        {
            MirrorModifier mirror => new ProjectModifierData
            {
                Type = "Mirror",
                IsEnabled = mirror.IsEnabled,
                Axis = mirror.Axis.ToString(),
                WeldThreshold = mirror.WeldThreshold,
            },
            SubdivisionSurfaceModifier subsurf => new ProjectModifierData
            {
                Type = "SubdivisionSurface",
                IsEnabled = subsurf.IsEnabled,
                Iterations = subsurf.Iterations,
            },
            _ => throw new NotSupportedException(
                $"'{modifier.GetType().Name}' has no Jolie3DProjectSerializer case - cannot save it into a project file."),
        };

        /// <exception cref="InvalidDataException"><paramref name="data"/>'s own
        /// <see cref="ProjectModifierData.Type"/> isn't one this version of the format
        /// recognizes - a newer project file (from a future version of this format) or a
        /// hand-corrupted one, either way not silently ignored.</exception>
        private static Modifier ConvertModifierData(ProjectModifierData data, string sourceFilePath) => data.Type switch
        {
            "Mirror" => new MirrorModifier
            {
                IsEnabled = data.IsEnabled,
                Axis = Enum.TryParse<MirrorAxis>(data.Axis, out var axis) ? axis : MirrorAxis.X,
                WeldThreshold = data.WeldThreshold ?? 0.0001f,
            },
            "SubdivisionSurface" => new SubdivisionSurfaceModifier
            {
                IsEnabled = data.IsEnabled,
                Iterations = data.Iterations ?? 1,
            },
            _ => throw new InvalidDataException(
                $"'{sourceFilePath}' has a modifier of unknown type '{data.Type}' - this file may need a newer version of JolieCat3D."),
        };
    }
}
