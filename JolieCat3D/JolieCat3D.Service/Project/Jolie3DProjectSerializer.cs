using System.Numerics;
using System.Text.Json;
using JolieCat3D.Core.Constraints;
using JolieCat3D.Core.Geometry;
using JolieCat3D.Core.Materials;
using JolieCat3D.Core.Modifiers;
using JolieCat3D.Core.Numerics;
using JolieCat3D.Core.Scene;
using JolieCat3D.Service.Animation;

namespace JolieCat3D.Service.Project
{
    /// <summary>
    /// Saves/loads a complete JolieCat3D workspace (the whole <see cref="Scene3D"/> plus
    /// its <see cref="AnimationTimeline"/>) as a single ".jolie3d" JSON file. Unlike
    /// OBJ/STL/glTF export, this keeps everything the editor knows about: modifiers,
    /// cameras, lights, curves, armatures, skinning, constraints, material slots, seams
    /// and painted textures.
    ///
    /// Cross-node references (Boolean targets, constraint targets, skin bones, the active
    /// camera) are stored as <see cref="ProjectNodeData.Id"/> values, so they survive
    /// duplicate node names.
    /// </summary>
    public static class Jolie3DProjectSerializer
    {
        public const int CurrentFormatVersion = 2;

        public const string FileExtension = ".jolie3d";

        public const string FileFilter = "JolieCat3D Project (*.jolie3d)|*.jolie3d";

        private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = false };

        public static bool IsProjectFile(string filePath) =>
            string.Equals(Path.GetExtension(filePath), FileExtension, StringComparison.OrdinalIgnoreCase);

        // ============================= Save =============================

        public static void Save(Scene3D scene, AnimationTimeline timeline, string filePath)
        {
            ArgumentNullException.ThrowIfNull(scene);
            ArgumentNullException.ThrowIfNull(timeline);
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

            var data = BuildData(scene, timeline);

            var directory = Path.GetDirectoryName(Path.GetFullPath(filePath));
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            // Write to a temp file first so a crash mid-save never destroys the previous copy.
            var tempPath = filePath + ".tmp";
            using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write))
                JsonSerializer.Serialize(stream, data, WriteOptions);

            File.Move(tempPath, filePath, overwrite: true);
        }

        public static ProjectFileData BuildData(Scene3D scene, AnimationTimeline timeline)
        {
            var ids = new Dictionary<Node, int>();
            foreach (var node in scene.Traverse()) ids[node] = ids.Count;

            var data = new ProjectFileData
            {
                FormatVersion = CurrentFormatVersion,
                SceneName = scene.Name,
                Animation = AnimationExporter.BuildExportData(timeline),
                ActiveCameraNodePath = scene.ActiveCamera is { } activeCamera ? NodePathResolver.GetPath(activeCamera) : null,
                ActiveCameraNodeId = scene.ActiveCamera is { } cam && ids.TryGetValue(cam, out var camId) ? camId : null,
                EnvironmentSkyboxSource = scene.Environment?.SkyboxSource,
                EnvironmentSkyboxRotation = scene.Environment?.SkyboxRotation ?? 0f,
                EnvironmentSkyboxStrength = scene.Environment?.SkyboxStrength ?? 1f,
            };

            // Linked duplicates (Alt+D, Scatter) share one Mesh: store it once.
            var meshOwners = new Dictionary<Mesh, Node>(ReferenceEqualityComparer.Instance);
            foreach (var node in scene.Traverse())
                if (node.Mesh is { } shared) meshOwners.TryAdd(shared, node);

            foreach (var root in scene.RootNodes)
                data.RootNodes.Add(ConvertNode(root, ids, meshOwners: meshOwners));

            return data;
        }

        // ============================= Load =============================

        /// <exception cref="InvalidDataException">The file isn't a valid project file,
        /// or uses a modifier/constraint type this version doesn't know.</exception>
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

            return FromData(data, filePath);
        }

        public static (Scene3D Scene, AnimationTimeline Timeline) FromData(ProjectFileData data, string sourceName = "project")
        {
            ArgumentNullException.ThrowIfNull(data);

            if (data.FormatVersion > CurrentFormatVersion)
                throw new InvalidDataException(
                    $"'{sourceName}' was saved by a newer JolieCat3D (format {data.FormatVersion}, this build reads up to {CurrentFormatVersion}).");

            var context = new LoadContext(sourceName);
            var scene = new Scene3D(data.SceneName);
            foreach (var rootData in data.RootNodes)
                scene.AddRootNode(ConvertNodeData(rootData, context));

            // References can point forward in the tree, so resolve them only once every node exists.
            foreach (var fixup in context.Fixups) fixup();

            scene.ActiveCamera = data.ActiveCameraNodeId is { } activeId
                ? context.Find(activeId)
                : NodePathResolver.FindByPath(scene, data.ActiveCameraNodePath);

            scene.Environment = data.EnvironmentSkyboxSource is { Length: > 0 } skyboxSource
                ? new EnvironmentSettings
                {
                    SkyboxSource = skyboxSource,
                    SkyboxRotation = data.EnvironmentSkyboxRotation,
                    SkyboxStrength = float.IsFinite(data.EnvironmentSkyboxStrength) && data.EnvironmentSkyboxStrength >= 0 ? data.EnvironmentSkyboxStrength : 1f,
                }
                : null;

            var timeline = new AnimationTimeline();
            AnimationExporter.ApplyExportData(data.Animation, timeline, scene);

            return (scene, timeline);
        }

        private sealed class LoadContext(string sourceName)
        {
            public string SourceName { get; } = sourceName;
            public Dictionary<int, Node> NodesById { get; } = new();
            public List<Action> Fixups { get; } = new();

            public Node? Find(int? id) => id is { } value && NodesById.TryGetValue(value, out var node) ? node : null;
        }

        // ============================= Node =============================

        /// <summary>Stable text description of one node's own state (no children), used to
        /// detect whether an edit actually changed anything.</summary>
        internal static string DescribeNode(Node node) =>
            JsonSerializer.Serialize(ConvertNode(node, new IdentityIds(), recurse: false), WriteOptions);

        /// <summary>Maps any node to its identity hash - good enough for change detection.</summary>
        private sealed class IdentityIds : IReadOnlyDictionary<Node, int>
        {
            public int this[Node key] => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(key);
            public IEnumerable<Node> Keys => Array.Empty<Node>();
            public IEnumerable<int> Values => Array.Empty<int>();
            public int Count => 0;
            public bool ContainsKey(Node key) => true;
            public bool TryGetValue(Node key, out int value) { value = this[key]; return true; }
            public IEnumerator<KeyValuePair<Node, int>> GetEnumerator() => Enumerable.Empty<KeyValuePair<Node, int>>().GetEnumerator();
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        }

        private static ProjectNodeData ConvertNode(Node node, IReadOnlyDictionary<Node, int> ids, bool recurse = true, IReadOnlyDictionary<Mesh, Node>? meshOwners = null)
        {
            var data = new ProjectNodeData
            {
                Id = ids[node],
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
                IsArmature = node.Armature is not null,
                IsHidden = node.IsHidden,
            };

            if (node.Mesh is { } mesh)
            {
                if (meshOwners is not null && meshOwners.TryGetValue(mesh, out var owner) && !ReferenceEquals(owner, node))
                    data.SharedMeshNodeId = ids[owner];
                else
                    data.Mesh = ConvertMesh(mesh);
            }
            if (node.Camera is { } camera) data.Camera = ConvertCamera(camera);
            if (node.Light is { } light) data.Light = ConvertLight(light);
            if (node.Curve is { } curve) data.Curve = ConvertCurve(curve);
            if (node.Bone is { } bone) data.Bone = ConvertBone(bone);
            if (node.SkinBinding is { } skin) data.Skin = ConvertSkin(skin, ids);
            foreach (var modifier in node.Modifiers) data.Modifiers.Add(ConvertModifier(modifier, ids));
            foreach (var constraint in node.Constraints) data.Constraints.Add(ConvertConstraint(constraint, ids));
            if (recurse)
                foreach (var child in node.Children) data.Children.Add(ConvertNode(child, ids, meshOwners: meshOwners));

            return data;
        }

        private static Node ConvertNodeData(ProjectNodeData data, LoadContext context)
        {
            var node = new Node(data.Name)
            {
                LocalPosition = new Vector3(data.PositionX, data.PositionY, data.PositionZ),
                LocalRotation = new Quaternion(data.RotationX, data.RotationY, data.RotationZ, data.RotationW),
                LocalScale = new Vector3(data.ScaleX, data.ScaleY, data.ScaleZ),
                IsHidden = data.IsHidden,
            };

            if (data.Id >= 0) context.NodesById[data.Id] = node;

            if (data.Mesh is { } meshData) node.Mesh = ConvertMeshData(meshData);
            else if (data.SharedMeshNodeId is { } sharedId) context.Fixups.Insert(0, () => node.Mesh = context.Find(sharedId)?.Mesh);
            if (data.Camera is { } cameraData) node.Camera = ConvertCameraData(cameraData);
            if (data.Light is { } lightData) node.Light = ConvertLightData(lightData);
            if (data.Curve is { } curveData) node.Curve = ConvertCurveData(curveData);
            if (data.IsArmature) node.Armature = new ArmatureData();
            if (data.Bone is { } boneData) node.Bone = ConvertBoneData(boneData);
            if (data.Skin is { } skinData) node.SkinBinding = ConvertSkinData(skinData, context);
            foreach (var modifierData in data.Modifiers) node.Modifiers.Add(ConvertModifierData(modifierData, context));
            foreach (var constraintData in data.Constraints) node.Constraints.Add(ConvertConstraintData(constraintData, context));
            foreach (var childData in data.Children) node.AddChild(ConvertNodeData(childData, context));

            return node;
        }

        // ============================= Camera / Light =============================

        private static ProjectCameraData ConvertCamera(CameraData camera) => new()
        {
            ProjectionMode = camera.ProjectionMode.ToString(),
            FieldOfView = camera.FieldOfView,
            OrthographicWidth = camera.OrthographicWidth,
            NearPlaneDistance = camera.NearPlaneDistance,
            FarPlaneDistance = camera.FarPlaneDistance,
            DepthOfField = camera.DepthOfField,
            FocusDistance = camera.FocusDistance,
            FStop = camera.FStop,
        };

        private static CameraData ConvertCameraData(ProjectCameraData data) => new()
        {
            ProjectionMode = Enum.TryParse<CameraProjectionMode>(data.ProjectionMode, out var mode) ? mode : CameraProjectionMode.Perspective,
            FieldOfView = data.FieldOfView,
            OrthographicWidth = data.OrthographicWidth,
            NearPlaneDistance = data.NearPlaneDistance,
            FarPlaneDistance = data.FarPlaneDistance,
            DepthOfField = data.DepthOfField,
            FocusDistance = data.FocusDistance,
            FStop = data.FStop,
        };

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
            Width = light.Width,
            Height = light.Height,
        };

        private static LightData ConvertLightData(ProjectLightData data) => new()
        {
            Type = Enum.TryParse<LightType>(data.Type, out var type) ? type : LightType.Directional,
            Color = new Color4(data.ColorR, data.ColorG, data.ColorB, data.ColorA),
            Intensity = data.Intensity,
            Range = data.Range,
            SpotAngle = data.SpotAngle,
            Width = data.Width,
            Height = data.Height,
        };

        // ============================= Curve / Bone / Skin =============================

        private static ProjectCurveData ConvertCurve(CurveData curve) => new()
        {
            Points = curve.Points.Select(p => new ProjectCurvePointData
            {
                Position = ToArray(p.Position),
                HandleIn = ToArray(p.HandleIn),
                HandleOut = ToArray(p.HandleOut),
            }).ToList(),
            Closed = curve.Closed,
            BevelDepth = curve.BevelDepth,
            SegmentsPerSpan = curve.SegmentsPerSpan,
            RadialSegments = curve.RadialSegments,
        };

        private static CurveData ConvertCurveData(ProjectCurveData data)
        {
            var curve = new CurveData
            {
                Closed = data.Closed,
                BevelDepth = data.BevelDepth,
                SegmentsPerSpan = data.SegmentsPerSpan,
                RadialSegments = data.RadialSegments,
            };
            foreach (var p in data.Points)
                curve.Points.Add(new CurvePoint(ToVector3(p.Position), ToVector3(p.HandleIn), ToVector3(p.HandleOut)));
            return curve;
        }

        private static ProjectBoneData ConvertBone(BoneData bone) => new()
        {
            Head = ToArray(bone.Head),
            Tail = ToArray(bone.Tail),
            RestPoseWorldMatrix = ToArray(bone.RestPoseWorldMatrix),
        };

        private static BoneData ConvertBoneData(ProjectBoneData data) => new()
        {
            Head = ToVector3(data.Head),
            Tail = ToVector3(data.Tail),
            RestPoseWorldMatrix = data.RestPoseWorldMatrix is { Length: 16 } m ? ToMatrix(m) : Matrix4x4.Identity,
        };

        private static ProjectSkinData ConvertSkin(SkinBinding skin, IReadOnlyDictionary<Node, int> ids) => new()
        {
            ArmatureNodeId = skin.Armature is { } armature && ids.TryGetValue(armature, out var armatureId) ? armatureId : null,
            BoneNodeIds = skin.Bones.Select(b => ids.TryGetValue(b, out var id) ? id : -1).ToList(),
        };

        private static SkinBinding ConvertSkinData(ProjectSkinData data, LoadContext context)
        {
            var skin = new SkinBinding();
            context.Fixups.Add(() =>
            {
                skin.Armature = context.Find(data.ArmatureNodeId);
                // Keep list positions stable - vertex bone indices point into this list.
                foreach (var boneId in data.BoneNodeIds)
                {
                    var bone = context.Find(boneId)
                        ?? throw new InvalidDataException($"'{context.SourceName}' has a skin bound to missing bone node #{boneId}.");
                    skin.Bones.Add(bone);
                }
            });
            return skin;
        }

        // ============================= Mesh =============================

        private static ProjectMeshData ConvertMesh(Mesh mesh)
        {
            var data = new ProjectMeshData { Name = mesh.Name };
            var skinned = mesh.Vertices.Any(v => v.BoneWeights != Vector4.Zero);
            if (skinned)
            {
                data.BoneIndices = new List<int>(mesh.Vertices.Count * 4);
                data.BoneWeights = new List<float>(mesh.Vertices.Count * 4);
            }

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

                if (skinned)
                {
                    var bi = vertex.BoneIndices;
                    data.BoneIndices!.AddRange(new[] { bi.X, bi.Y, bi.Z, bi.W });
                    var bw = vertex.BoneWeights;
                    data.BoneWeights!.AddRange(new[] { bw.X, bw.Y, bw.Z, bw.W });
                }
            }

            foreach (var face in mesh.Faces)
                data.Faces.Add(new ProjectFaceData { A = face.A, B = face.B, C = face.C });

            foreach (var polygon in mesh.Polygons)
                data.Polygons.Add(polygon.Indices.ToList());

            if (mesh.Polygons.Any(p => p.MaterialSlotIndex >= 0))
                data.PolygonMaterialSlots = mesh.Polygons.Select(p => p.MaterialSlotIndex).ToList();

            if (mesh.Material is { } material) data.Material = ConvertMaterial(material);
            foreach (var slot in mesh.MaterialSlots) data.MaterialSlots.Add(ConvertMaterial(slot));

            if (mesh.SeamEdges.Count > 0)
                data.SeamEdges = mesh.SeamEdges.SelectMany(e => new[] { e.A, e.B }).ToList();
            if (mesh.CreaseEdges.Count > 0)
                data.CreaseEdges = mesh.CreaseEdges.SelectMany(e => new[] { e.A, e.B }).ToList();

            if (mesh.ShapeBasis is { } basis) data.ShapeBasis = Flatten(basis);
            if (mesh.ShapeKeys.Count > 0)
                data.ShapeKeys = mesh.ShapeKeys.Select(k => new ProjectShapeKeyData { Name = k.Name, Value = k.Value, Positions = Flatten(k.Positions) }).ToList();

            return data;

            static List<float> Flatten(Vector3[] points) => points.SelectMany(p => new[] { p.X, p.Y, p.Z }).ToList();
        }

        private static Mesh ConvertMeshData(ProjectMeshData data)
        {
            var mesh = new Mesh(data.Name);
            var hasSkin = data.BoneIndices is { } idx && data.BoneWeights is { } wts
                && idx.Count == data.Vertices.Count * 4 && wts.Count == data.Vertices.Count * 4;

            for (var i = 0; i < data.Vertices.Count; i++)
            {
                var v = data.Vertices[i];
                var boneIndices = default(BoneIndices);
                var boneWeights = Vector4.Zero;
                if (hasSkin)
                {
                    var o = i * 4;
                    var bi = data.BoneIndices!;
                    var bw = data.BoneWeights!;
                    boneIndices = new BoneIndices(bi[o], bi[o + 1], bi[o + 2], bi[o + 3]);
                    boneWeights = new Vector4(bw[o], bw[o + 1], bw[o + 2], bw[o + 3]);
                }

                mesh.AddVertex(new Vertex(
                    new Vector3(v.PositionX, v.PositionY, v.PositionZ),
                    new Vector3(v.NormalX, v.NormalY, v.NormalZ),
                    new Vector2(v.UVX, v.UVY),
                    new Color4(v.ColorR, v.ColorG, v.ColorB, v.ColorA),
                    boneIndices,
                    boneWeights));
            }

            foreach (var faceData in data.Faces)
                mesh.AddTriangle(faceData.A, faceData.B, faceData.C);

            for (var i = 0; i < data.Polygons.Count; i++)
            {
                var polygon = new Polygon(data.Polygons[i]);
                if (data.PolygonMaterialSlots is { } slots && i < slots.Count) polygon.MaterialSlotIndex = slots[i];
                mesh.AddPolygon(polygon);
            }

            if (data.Material is { } materialData) mesh.Material = ConvertMaterialData(materialData);
            foreach (var slot in data.MaterialSlots) mesh.AddMaterialSlot(ConvertMaterialData(slot));

            if (data.CreaseEdges is { } creaseList)
                for (var i = 0; i + 1 < creaseList.Count; i += 2) mesh.MarkCrease(creaseList[i], creaseList[i + 1]);
            if (data.SeamEdges is { } seams)
                for (var i = 0; i + 1 < seams.Count; i += 2)
                    mesh.MarkSeam(seams[i], seams[i + 1]);

            if (data.ShapeBasis is { } basis) mesh.ShapeBasis = Unflatten(basis);
            if (data.ShapeKeys is { } keys)
                foreach (var k in keys) mesh.ShapeKeys.Add(new ShapeKey(k.Name, Unflatten(k.Positions)) { Value = k.Value });

            return mesh;

            static Vector3[] Unflatten(List<float> f)
            {
                var result = new Vector3[f.Count / 3];
                for (var i = 0; i < result.Length; i++) result[i] = new Vector3(f[i * 3], f[i * 3 + 1], f[i * 3 + 2]);
                return result;
            }
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
            NormalTexturePath = material.NormalTexturePath,
            MetallicRoughnessTexturePath = material.MetallicRoughnessTexturePath,
            OcclusionTexturePath = material.OcclusionTexturePath,
            Emissive = material.IsEmissive || material.EmissiveTexturePath is not null
                ? new[] { material.EmissiveColor.R, material.EmissiveColor.G, material.EmissiveColor.B }
                : null,
            EmissiveStrength = material.EmissiveStrength,
            EmissiveTexturePath = material.EmissiveTexturePath,
            AlphaMode = material.AlphaMode == MaterialAlphaMode.Opaque ? null : material.AlphaMode.ToString(),
            AlphaCutoff = material.AlphaCutoff,
            DoubleSided = material.DoubleSided,
            PaintedTexture = material.PaintedTextureBuffer is { } buffer ? ConvertTexture(buffer) : null,
            NodeGraph = material.NodeGraph?.Clone(),
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
            NormalTexturePath = data.NormalTexturePath,
            MetallicRoughnessTexturePath = data.MetallicRoughnessTexturePath,
            OcclusionTexturePath = data.OcclusionTexturePath,
            EmissiveColor = data.Emissive is { Length: 3 } e ? new Color4(e[0], e[1], e[2]) : new Color4(0f, 0f, 0f),
            EmissiveStrength = data.EmissiveStrength,
            EmissiveTexturePath = data.EmissiveTexturePath,
            AlphaMode = Enum.TryParse<MaterialAlphaMode>(data.AlphaMode, out var alphaMode) ? alphaMode : MaterialAlphaMode.Opaque,
            AlphaCutoff = data.AlphaCutoff,
            DoubleSided = data.DoubleSided,
            PaintedTextureBuffer = data.PaintedTexture is { } texture ? ConvertTextureData(texture) : null,
            NodeGraph = SanitizeGraph(data.NodeGraph),
        };

        /// <summary>Drops unknown node types and dangling links from a loaded tree.</summary>
        private static Core.Materials.Nodes.ShaderGraph? SanitizeGraph(Core.Materials.Nodes.ShaderGraph? graph)
        {
            if (graph is null) return null;
            graph.Nodes ??= new();
            graph.Links ??= new();
            graph.Nodes.RemoveAll(n => n is null || !Core.Materials.Nodes.ShaderNodeTypes.Exists(n.Type));
            foreach (var n in graph.Nodes)
            {
                n.Inputs ??= new();
                n.Options ??= new();
            }
            graph.Links.RemoveAll(l => l is null || graph.Find(l.FromNode) is null || graph.Find(l.ToNode) is null);
            return graph;
        }

        private static ProjectTextureData ConvertTexture(TextureBuffer buffer)
        {
            var bytes = new byte[buffer.Width * buffer.Height * 4];
            var pixels = buffer.Pixels;
            for (var i = 0; i < pixels.Count; i++)
            {
                var c = pixels[i];
                bytes[i * 4] = ToByte(c.R);
                bytes[i * 4 + 1] = ToByte(c.G);
                bytes[i * 4 + 2] = ToByte(c.B);
                bytes[i * 4 + 3] = ToByte(c.A);
            }
            return new ProjectTextureData { Width = buffer.Width, Height = buffer.Height, RgbaBase64 = Convert.ToBase64String(bytes) };

            static byte ToByte(float value) => (byte)Math.Clamp((int)MathF.Round(value * 255f), 0, 255);
        }

        private static TextureBuffer? ConvertTextureData(ProjectTextureData data)
        {
            if (data.Width <= 0 || data.Height <= 0) return null;
            var bytes = Convert.FromBase64String(data.RgbaBase64);
            if (bytes.Length != data.Width * data.Height * 4) return null;

            var buffer = new TextureBuffer(data.Width, data.Height);
            for (var y = 0; y < data.Height; y++)
            for (var x = 0; x < data.Width; x++)
            {
                var o = (y * data.Width + x) * 4;
                buffer.SetPixel(x, y, new Color4(bytes[o] / 255f, bytes[o + 1] / 255f, bytes[o + 2] / 255f, bytes[o + 3] / 255f));
            }
            return buffer;
        }

        // ============================= Modifier =============================

        /// <exception cref="NotSupportedException">A modifier type with no case here -
        /// fails loudly at save time instead of silently dropping data.</exception>
        private static ProjectModifierData ConvertModifier(Modifier modifier, IReadOnlyDictionary<Node, int> ids) => modifier switch
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
            BooleanModifier boolean => new ProjectModifierData
            {
                Type = "Boolean",
                IsEnabled = boolean.IsEnabled,
                Operation = boolean.Operation.ToString(),
                TargetNodeId = boolean.Target is { } target && ids.TryGetValue(target, out var targetId) ? targetId : null,
            },
            ArrayModifier array => new ProjectModifierData
            {
                Type = "Array",
                IsEnabled = array.IsEnabled,
                Count = array.Count,
                RelativeOffset = ToArray(array.RelativeOffset),
                ConstantOffset = array.ConstantOffset == Vector3.Zero ? null : ToArray(array.ConstantOffset),
                MergeDistance = array.Merge ? array.MergeDistance : null,
            },
            SolidifyModifier solidify => new ProjectModifierData
            {
                Type = "Solidify",
                IsEnabled = solidify.IsEnabled,
                Thickness = solidify.Thickness,
            },
            ShrinkwrapModifier shrinkwrap => new ProjectModifierData
            {
                Type = "Shrinkwrap",
                IsEnabled = shrinkwrap.IsEnabled,
                ShrinkwrapMode = shrinkwrap.Mode.ToString(),
                Offset = shrinkwrap.Offset,
                TargetNodeId = shrinkwrap.Target is { } swTarget && ids.TryGetValue(swTarget, out var swId) ? swId : null,
            },
            CurveDeformModifier curveDeform => new ProjectModifierData
            {
                Type = "CurveDeform",
                IsEnabled = curveDeform.IsEnabled,
                TargetNodeId = curveDeform.Target is { } cdTarget && ids.TryGetValue(cdTarget, out var cdId) ? cdId : null,
            },
            WeightedNormalModifier weighted => new ProjectModifierData
            {
                Type = "WeightedNormal",
                IsEnabled = weighted.IsEnabled,
                AngleThresholdDegrees = weighted.AngleDegrees,
            },
            BevelModifier bevel => new ProjectModifierData
            {
                Type = "Bevel",
                IsEnabled = bevel.IsEnabled,
                Width = bevel.Width,
                Iterations = bevel.Segments,
                AngleThresholdDegrees = bevel.AngleDegrees,
            },
            DecimateModifier decimate => new ProjectModifierData
            {
                Type = "Decimate",
                IsEnabled = decimate.IsEnabled,
                Ratio = decimate.Ratio,
            },
            EdgeSplitModifier edgeSplit => new ProjectModifierData
            {
                Type = "EdgeSplit",
                IsEnabled = edgeSplit.IsEnabled,
                AngleThresholdDegrees = edgeSplit.AngleThresholdDegrees,
            },
            IParameterizedModifier parameterized when ParameterizedModifiers.TypeName(modifier) is { } typeName => new ProjectModifierData
            {
                Type = typeName,
                IsEnabled = modifier.IsEnabled,
                Parameters = parameterized.Parameters.ToDictionary(p => p.Label, p => p.Get()),
            },
            _ => throw new NotSupportedException(
                $"'{modifier.GetType().Name}' has no Jolie3DProjectSerializer case - cannot save it into a project file."),
        };

        private static Modifier ConvertModifierData(ProjectModifierData data, LoadContext context)
        {
            switch (data.Type)
            {
                case "Mirror":
                    return new MirrorModifier
                    {
                        IsEnabled = data.IsEnabled,
                        Axis = Enum.TryParse<MirrorAxis>(data.Axis, out var axis) ? axis : MirrorAxis.X,
                        WeldThreshold = data.WeldThreshold ?? 0.0001f,
                    };
                case "SubdivisionSurface":
                    return new SubdivisionSurfaceModifier { IsEnabled = data.IsEnabled, Iterations = data.Iterations ?? 1 };
                case "Boolean":
                    var boolean = new BooleanModifier
                    {
                        IsEnabled = data.IsEnabled,
                        Operation = Enum.TryParse<BooleanOperation>(data.Operation, out var op) ? op : BooleanOperation.Union,
                    };
                    context.Fixups.Add(() => boolean.Target = context.Find(data.TargetNodeId));
                    return boolean;
                case "Array":
                    return new ArrayModifier
                    {
                        IsEnabled = data.IsEnabled,
                        Count = data.Count ?? 3,
                        RelativeOffset = data.RelativeOffset is { Length: 3 } offset ? ToVector3(offset) : new Vector3(1f, 0f, 0f),
                        ConstantOffset = data.ConstantOffset is { Length: 3 } constant ? ToVector3(constant) : Vector3.Zero,
                        Merge = data.MergeDistance is not null,
                        MergeDistance = data.MergeDistance ?? 0.001f,
                    };
                case "Solidify":
                    return new SolidifyModifier { IsEnabled = data.IsEnabled, Thickness = data.Thickness ?? 0.1f };
                case "Shrinkwrap":
                    var shrinkwrap = new ShrinkwrapModifier
                    {
                        IsEnabled = data.IsEnabled,
                        Mode = Enum.TryParse<ShrinkwrapMode>(data.ShrinkwrapMode, out var swMode) ? swMode : ShrinkwrapMode.NearestSurface,
                        Offset = data.Offset ?? 0f,
                    };
                    context.Fixups.Add(() => shrinkwrap.Target = context.Find(data.TargetNodeId));
                    return shrinkwrap;
                case "CurveDeform":
                    var curveDeform = new CurveDeformModifier { IsEnabled = data.IsEnabled };
                    context.Fixups.Add(() => curveDeform.Target = context.Find(data.TargetNodeId));
                    return curveDeform;
                case "WeightedNormal":
                    return new WeightedNormalModifier { IsEnabled = data.IsEnabled, AngleDegrees = data.AngleThresholdDegrees ?? 30f };
                case "Bevel":
                    return new BevelModifier { IsEnabled = data.IsEnabled, Width = data.Width ?? 0.05f, Segments = data.Iterations ?? 2, AngleDegrees = data.AngleThresholdDegrees ?? 30f };
                case "Decimate":
                    return new DecimateModifier { IsEnabled = data.IsEnabled, Ratio = data.Ratio ?? 0.5f };
                case "EdgeSplit":
                    return new EdgeSplitModifier { IsEnabled = data.IsEnabled, AngleThresholdDegrees = data.AngleThresholdDegrees ?? 30f };
                case var type when ParameterizedModifiers.Factories.TryGetValue(type, out var factory):
                    var created = factory();
                    created.IsEnabled = data.IsEnabled;
                    if (data.Parameters is { } values && created is IParameterizedModifier withParams)
                        foreach (var parameter in withParams.Parameters)
                            if (values.TryGetValue(parameter.Label, out var value) && float.IsFinite(value)) parameter.Set(value);
                    return created;
                default:
                    throw new InvalidDataException(
                        $"'{context.SourceName}' has a modifier of unknown type '{data.Type}' - this file may need a newer version of JolieCat3D.");
            }
        }

        // ============================= Constraint =============================

        private static ProjectConstraintData ConvertConstraint(Constraint constraint, IReadOnlyDictionary<Node, int> ids) => constraint switch
        {
            TrackToConstraint trackTo => new ProjectConstraintData
            {
                Type = "TrackTo",
                IsEnabled = trackTo.IsEnabled,
                TargetNodeId = trackTo.Target is { } target && ids.TryGetValue(target, out var id) ? id : null,
                ForwardAxis = trackTo.ForwardAxis.ToString(),
            },
            InverseKinematicsConstraint ik => new ProjectConstraintData
            {
                Type = "IK",
                IsEnabled = ik.IsEnabled,
                TargetNodeId = ik.Target is { } target && ids.TryGetValue(target, out var targetId) ? targetId : null,
                PoleNodeId = ik.PoleTarget is { } pole && ids.TryGetValue(pole, out var poleId) ? poleId : null,
                ChainLength = ik.ChainLength,
                Iterations = ik.Iterations,
            },
            IParameterizedConstraint parameterized when ParameterizedConstraints.TypeName(constraint) is { } typeName => new ProjectConstraintData
            {
                Type = typeName,
                IsEnabled = constraint.IsEnabled,
                TargetNodeId = parameterized.Target is { } pt && ids.TryGetValue(pt, out var ptId) ? ptId : null,
                Parameters = parameterized.Parameters.ToDictionary(p => p.Label, p => p.Get()),
            },
            _ => throw new NotSupportedException(
                $"'{constraint.GetType().Name}' has no Jolie3DProjectSerializer case - cannot save it into a project file."),
        };

        private static Constraint ConvertConstraintData(ProjectConstraintData data, LoadContext context)
        {
            switch (data.Type)
            {
                case "TrackTo":
                    var trackTo = new TrackToConstraint
                    {
                        IsEnabled = data.IsEnabled,
                        ForwardAxis = Enum.TryParse<ConstraintAxis>(data.ForwardAxis, out var axis) ? axis : ConstraintAxis.PlusZ,
                    };
                    context.Fixups.Add(() => trackTo.Target = context.Find(data.TargetNodeId));
                    return trackTo;
                case "IK":
                    var ik = new InverseKinematicsConstraint
                    {
                        IsEnabled = data.IsEnabled,
                        ChainLength = data.ChainLength ?? 2,
                        Iterations = data.Iterations ?? 100,
                    };
                    context.Fixups.Add(() =>
                    {
                        ik.Target = context.Find(data.TargetNodeId);
                        ik.PoleTarget = context.Find(data.PoleNodeId);
                    });
                    return ik;
                case var type when ParameterizedConstraints.Factories.TryGetValue(type, out var factory):
                    var created = factory();
                    created.IsEnabled = data.IsEnabled;
                    if (created is IParameterizedConstraint withParams)
                    {
                        if (data.Parameters is { } values)
                            foreach (var parameter in withParams.Parameters)
                                if (values.TryGetValue(parameter.Label, out var value) && float.IsFinite(value)) parameter.Set(value);
                        if (withParams.UsesTarget) context.Fixups.Add(() => withParams.Target = context.Find(data.TargetNodeId));
                    }
                    return created;
                default:
                    throw new InvalidDataException(
                        $"'{context.SourceName}' has a constraint of unknown type '{data.Type}' - this file may need a newer version of JolieCat3D.");
            }
        }

        // ============================= Helpers =============================

        private static float[] ToArray(Vector3 v) => new[] { v.X, v.Y, v.Z };

        private static Vector3 ToVector3(float[]? a) => a is { Length: >= 3 } ? new Vector3(a[0], a[1], a[2]) : Vector3.Zero;

        private static float[] ToArray(Matrix4x4 m) => new[]
        {
            m.M11, m.M12, m.M13, m.M14,
            m.M21, m.M22, m.M23, m.M24,
            m.M31, m.M32, m.M33, m.M34,
            m.M41, m.M42, m.M43, m.M44,
        };

        private static Matrix4x4 ToMatrix(float[] a) => new(
            a[0], a[1], a[2], a[3],
            a[4], a[5], a[6], a[7],
            a[8], a[9], a[10], a[11],
            a[12], a[13], a[14], a[15]);
    }
}
