using System.Numerics;
using JolieCat3D.Core.Geometry;
using JolieCat3D.Core.Numerics;
using JolieCat3D.Core.Scene;
using JolieCat3D.Service.Animation;
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
    /// Node animations (translation/rotation/scale) are imported by
    /// <see cref="ImportWithAnimation"/>, one action per glTF animation. Skins become a
    /// <see cref="SkinBinding"/> with the joints turned into bones (rest pose from the
    /// inverse bind matrices) and per-vertex JOINTS_0/WEIGHTS_0. Morph targets become shape keys
    /// (names from <c>extras.targetNames</c>), and weights animations become shape key curves.
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

        public static CoreScene Import(string filePath) => ImportWithAnimation(filePath).Scene;

        /// <summary>
        /// Like <see cref="Import"/>, plus every glTF animation as its own
        /// <see cref="AnimationAction"/> (node translation/rotation/scale channels). The
        /// timeline is null when the file has no animations.
        /// </summary>
        public static (CoreScene Scene, AnimationTimeline? Timeline) ImportWithAnimation(string filePath)
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

            var nodeMap = new Dictionary<GltfNode, CoreNode>();
            foreach (var gltfNode in rootGltfNodes)
                scene.AddRootNode(BuildNode(gltfNode, materialCache, extractedImagePaths, textureDirectory, nodeMap));

            BuildSkins(model, nodeMap);
            return (scene, BuildTimeline(model, nodeMap));
        }

        // ============================= Skins =============================

        /// <summary>
        /// For every skinned node: its joints become bones (unless already bones), the node
        /// (and its extra-primitive "_Part" children) gets a <see cref="SkinBinding"/> whose
        /// bone order matches the skin's joint order, so JOINTS_0 indices map 1:1.
        ///
        /// glTF skins a vertex as <c>v * IBM * jointWorld</c> (ignoring the mesh node's own
        /// transform). <see cref="Core.Skinning.SkinningEvaluator"/> computes
        /// <c>v * meshWorld * inverse(RestPose) * boneWorld</c>, so
        /// <c>RestPose = inverse(IBM) * meshWorld</c> makes both identical.
        /// </summary>
        private static void BuildSkins(ModelRoot model, IReadOnlyDictionary<GltfNode, CoreNode> nodeMap)
        {
            var configured = new HashSet<CoreNode>();
            foreach (var gltfNode in model.LogicalNodes)
            {
                if (gltfNode.Skin is not { JointsCount: > 0 } skin || !nodeMap.TryGetValue(gltfNode, out var meshNode)) continue;

                var meshWorld = meshNode.GetWorldTransform();
                var binding = new SkinBinding();
                var joints = skin.Joints;
                var inverseBinds = skin.InverseBindMatrices;
                for (var j = 0; j < joints.Count; j++)
                {
                    if (!nodeMap.TryGetValue(joints[j], out var boneNode)) { binding.Bones.Add(meshNode); continue; }
                    binding.Bones.Add(boneNode);
                    if (!configured.Add(boneNode)) continue;

                    var inverseBind = j < inverseBinds.Count ? inverseBinds[j] : Matrix4x4.Identity;
                    var rest = Matrix4x4.Invert(inverseBind, out var bind) ? bind * meshWorld : boneNode.GetWorldTransform();
                    boneNode.Bone ??= new BoneData { Head = Vector3.Zero, Tail = GuessTail(joints[j], joints) };
                    boneNode.Bone.RestPoseWorldMatrix = rest;
                    if (boneNode.Mesh is null) boneNode.Mesh = boneNode.Bone.GenerateMesh();
                }

                // Armature: the declared skeleton root, else the parent of the top joint.
                var armatureGltf = skin.Skeleton ?? joints.FirstOrDefault(j => j.VisualParent is null || !joints.Contains(j.VisualParent))?.VisualParent;
                if (armatureGltf is not null && nodeMap.TryGetValue(armatureGltf, out var armature) && armature.Bone is null)
                {
                    binding.Armature = armature;
                    if (armature.Mesh is null && armature.Camera is null && armature.Light is null) armature.Armature ??= new ArmatureData();
                }

                meshNode.SkinBinding = binding;
                foreach (var child in meshNode.Children)
                    if (child.Mesh is not null && child.Bone is null && child.Name.StartsWith(meshNode.Mesh?.Name ?? meshNode.Name, StringComparison.Ordinal) && child.Name.Contains("_Part", StringComparison.Ordinal))
                        child.SkinBinding = binding.Clone();
            }

            // A bone's visual tail: toward its only child joint, else a short stub.
            static Vector3 GuessTail(GltfNode joint, IReadOnlyList<GltfNode> joints)
            {
                var childJoints = joint.VisualChildren.Where(joints.Contains).ToList();
                if (childJoints.Count == 1)
                {
                    var offset = childJoints[0].LocalTransform.Translation;
                    if (offset.LengthSquared() > 1e-10f) return offset;
                }
                var parentOffset = joint.VisualParent is { } parent && joints.Contains(parent) ? joint.LocalTransform.Translation.Length() : 0f;
                return new Vector3(0, parentOffset > 1e-5f ? parentOffset * 0.5f : 0.1f, 0);
            }
        }

        // ============================= Animation =============================

        /// <summary>
        /// One action per glTF animation. For every animated node, the union of its
        /// channels' key times is sampled with SharpGLTF's own curve samplers (so LINEAR,
        /// STEP and CUBICSPLINE all evaluate exactly as the file defines them) and stored
        /// as full TRS keyframes; un-animated components keep the node's rest value.
        /// </summary>
        private static AnimationTimeline? BuildTimeline(ModelRoot model, IReadOnlyDictionary<GltfNode, CoreNode> nodeMap)
        {
            if (model.LogicalAnimations.Count == 0) return null;

            var timeline = new AnimationTimeline();
            var first = true;
            var maxDuration = 0f;
            foreach (var animation in model.LogicalAnimations)
            {
                var name = string.IsNullOrWhiteSpace(animation.Name) ? $"Animation{animation.LogicalIndex}" : animation.Name;
                AnimationAction action;
                if (first) { action = timeline.Actions[0]; action.Name = name; first = false; }
                else action = timeline.AddAction(name);

                foreach (var group in animation.Channels.Where(c => c.TargetNode is not null).GroupBy(c => c.TargetNode))
                {
                    if (!nodeMap.TryGetValue(group.Key, out var node)) continue;

                    SharpGLTF.Animations.ICurveSampler<Vector3>? translation = null, scale = null;
                    SharpGLTF.Animations.ICurveSampler<Quaternion>? rotation = null;
                    SharpGLTF.Animations.ICurveSampler<float[]>? weights = null;
                    var weightTimes = new SortedSet<float>();
                    var times = new SortedSet<float>();
                    var stepped = false;
                    foreach (var channel in group)
                    {
                        switch (channel.TargetNodePath)
                        {
                            case PropertyPath.translation when channel.GetTranslationSampler() is { } s:
                                translation = s.CreateCurveSampler();
                                AddTimes(s, times);
                                stepped |= s.InterpolationMode == AnimationInterpolationMode.STEP;
                                break;
                            case PropertyPath.rotation when channel.GetRotationSampler() is { } s:
                                rotation = s.CreateCurveSampler();
                                AddTimes(s, times);
                                stepped |= s.InterpolationMode == AnimationInterpolationMode.STEP;
                                break;
                            case PropertyPath.scale when channel.GetScaleSampler() is { } s:
                                scale = s.CreateCurveSampler();
                                AddTimes(s, times);
                                stepped |= s.InterpolationMode == AnimationInterpolationMode.STEP;
                                break;
                            case PropertyPath.weights when channel.GetMorphSampler() is { } s:
                                weights = s.CreateCurveSampler();
                                AddTimes(s, weightTimes);
                                break;
                        }
                    }
                    if (times.Count == 0 && weightTimes.Count == 0) continue;

                    var needsLookFix = group.Key.Mesh is null && (group.Key.Camera is not null || group.Key.PunctualLight is not null);
                    var track = new AnimationTrack(node);
                    var mode = stepped ? InterpolationMode.Constant : InterpolationMode.Linear;
                    foreach (var t in times)
                    {
                        var position = translation?.GetPoint(t) ?? node.LocalPosition;
                        var rot = rotation is null ? node.LocalRotation : Quaternion.Normalize(rotation.GetPoint(t)) * (needsLookFix ? LookDirectionFix : Quaternion.Identity);
                        var size = scale?.GetPoint(t) ?? node.LocalScale;
                        track.AddKeyframe(t, position, rot, size, mode);
                        maxDuration = MathF.Max(maxDuration, t);
                    }

                    // Morph weights -> one "shape:Name" curve per shape key of the node's mesh.
                    if (weights is not null && node.Mesh is { ShapeKeys.Count: > 0 } morphMesh)
                    {
                        foreach (var t in weightTimes)
                        {
                            var w = weights.GetPoint(t);
                            for (var k = 0; k < morphMesh.ShapeKeys.Count && k < w.Length; k++)
                                track.PropertyCurve(AnimationTrack.ShapeKeyPrefix + morphMesh.ShapeKeys[k].Name).Insert(t, w[k], InterpolationMode.Linear);
                            maxDuration = MathF.Max(maxDuration, t);
                        }
                    }
                    action.SetTrack(track);
                }
            }

            if (maxDuration > timeline.Duration) timeline.TotalFrames = (int)Math.Ceiling(maxDuration * timeline.FrameRate);
            return timeline;

            static void AddTimes<T>(IAnimationSampler<T> sampler, SortedSet<float> times)
            {
                if (sampler.InterpolationMode == AnimationInterpolationMode.CUBICSPLINE)
                    foreach (var k in sampler.GetCubicKeys()) times.Add(k.Key);
                else
                    foreach (var k in sampler.GetLinearKeys()) times.Add(k.Key);
            }
        }

        // ============================= Node graph =============================

        private static CoreNode BuildNode(
            GltfNode gltfNode, Dictionary<GltfMaterial, CoreMaterial> materialCache,
            Dictionary<Image, string> extractedImagePaths, string textureDirectory,
            Dictionary<GltfNode, CoreNode> nodeMap)
        {
            var node = new CoreNode(gltfNode.Name ?? "Node");
            nodeMap[gltfNode] = node;

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
                var morphNames = ReadTargetNames(gltfMesh);
                var morphWeights = gltfNode.MorphWeights is { Count: > 0 } nw ? nw : gltfMesh.MorphWeights;
                node.Mesh = BuildMesh(gltfMesh.Primitives[0], gltfMesh.Name ?? gltfNode.Name ?? "Mesh", materialCache, extractedImagePaths, textureDirectory, morphNames, morphWeights);

                for (var i = 1; i < gltfMesh.Primitives.Count; i++)
                {
                    var partName = $"{gltfMesh.Name ?? gltfNode.Name ?? "Mesh"}_Part{i + 1}";
                    node.AddChild(new CoreNode(partName) { Mesh = BuildMesh(gltfMesh.Primitives[i], partName, materialCache, extractedImagePaths, textureDirectory, morphNames, morphWeights) });
                }
            }

            if (gltfNode.Camera is { } camera)
                node.Camera = BuildCamera(camera);

            if (gltfNode.PunctualLight is { } light)
                node.Light = BuildLight(light);

            foreach (var child in gltfNode.VisualChildren)
                node.AddChild(BuildNode(child, materialCache, extractedImagePaths, textureDirectory, nodeMap));

            return node;
        }

        // ============================= Mesh / Material =============================

        private static CoreMesh BuildMesh(
            MeshPrimitive primitive, string name,
            Dictionary<GltfMaterial, CoreMaterial> materialCache,
            Dictionary<Image, string> extractedImagePaths, string textureDirectory,
            IReadOnlyList<string>? morphNames = null, IReadOnlyList<float>? morphWeights = null)
        {
            var mesh = new CoreMesh(name) { Material = GetOrCreateMaterial(primitive.Material, materialCache, extractedImagePaths, textureDirectory) };

            var positions = primitive.GetVertexAccessor("POSITION")?.AsVector3Array();
            if (positions is null) return mesh; // No POSITION at all - nothing this primitive can contribute.

            var normals = primitive.GetVertexAccessor("NORMAL")?.AsVector3Array();
            var uvs = primitive.GetVertexAccessor("TEXCOORD_0")?.AsVector2Array();
            // Alpha defaults to 1 for a COLOR_0 accessor storing only RGB (no 4th
            // component) - matching glTF's own spec default for a missing alpha there.
            var colors = primitive.GetVertexAccessor("COLOR_0")?.AsColorArray(1f);
            var jointIndices = primitive.GetVertexAccessor("JOINTS_0")?.AsVector4Array();
            var jointWeights = primitive.GetVertexAccessor("WEIGHTS_0")?.AsVector4Array();
            var hasSkin = jointIndices is { Count: > 0 } && jointWeights is { Count: > 0 };

            for (var i = 0; i < positions.Count; i++)
            {
                var normal = normals is { Count: > 0 } ? normals[i] : Vector3.Zero;
                var uv = uvs is { Count: > 0 } ? uvs[i] : Vector2.Zero;
                // Vertex's own constructor already maps an omitted/default Color4 to
                // White (see its own remarks) - a Color4.Transparent-black placeholder
                // would read as "hide this vertex", not "no color data supplied".
                var color = colors is { Count: > 0 } ? new Color4(colors[i]) : default;
                if (hasSkin)
                {
                    var ji = jointIndices![i];
                    mesh.AddVertex(new Vertex(positions[i], normal, uv, color,
                        new BoneIndices((int)ji.X, (int)ji.Y, (int)ji.Z, (int)ji.W), jointWeights![i]));
                }
                else
                {
                    mesh.AddVertex(new Vertex(positions[i], normal, uv, color));
                }
            }

            // GetTriangleIndices() already triangulates regardless of the primitive's own
            // DrawPrimitiveType (TRIANGLES/TRIANGLE_STRIP/TRIANGLE_FAN all normalize to
            // the same (A,B,C) triples here) - no separate draw-mode branch needed.
            foreach (var (a, b, c) in primitive.GetTriangleIndices())
                mesh.AddTriangle(a, b, c);

            if (normals is null) mesh.RecalculateNormals();

            // Morph targets -> shape keys (absolute positions = base + POSITION delta).
            for (var t = 0; t < primitive.MorphTargetsCount; t++)
            {
                if (mesh.ShapeBasis is null) ShapeKeys.AddBasis(mesh);
                var deltas = primitive.GetMorphTargetAccessors(t).TryGetValue("POSITION", out var accessor) ? accessor.AsVector3Array() : null;
                var keyPositions = new Vector3[mesh.Vertices.Count];
                for (var i = 0; i < keyPositions.Length; i++)
                    keyPositions[i] = mesh.Vertices[i].Position + (deltas is not null && i < deltas.Count ? deltas[i] : Vector3.Zero);
                var keyName = morphNames is not null && t < morphNames.Count && !string.IsNullOrWhiteSpace(morphNames[t]) ? morphNames[t] : $"Key {t + 1}";
                mesh.ShapeKeys.Add(new ShapeKey(ShapeKeys.UniqueName(mesh, keyName), keyPositions)
                {
                    Value = morphWeights is not null && t < morphWeights.Count ? morphWeights[t] : 0f,
                });
            }
            return mesh;
        }

        /// <summary>The de-facto standard <c>mesh.extras.targetNames</c> array, if present.</summary>
        private static IReadOnlyList<string>? ReadTargetNames(SharpGLTF.Schema2.Mesh mesh)
        {
            try
            {
                if (mesh.Extras is System.Text.Json.Nodes.JsonObject extras && extras["targetNames"] is System.Text.Json.Nodes.JsonArray names)
                    return names.Select(n => n?.GetValue<string>() ?? string.Empty).ToList();
            }
            catch (InvalidOperationException) { }
            catch (FormatException) { }
            return null;
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

            if (gltfMaterial.FindChannel("Occlusion") is { } occlusion &&
                TryExtractImage(occlusion.Texture, "Occlusion", extractedImagePaths, textureDirectory) is { } occlusionPath)
                material.OcclusionTexturePath = occlusionPath;

            material.DoubleSided = gltfMaterial.DoubleSided;
            if (gltfMaterial.Alpha == AlphaMode.MASK)
            {
                material.AlphaMode = Core.Materials.MaterialAlphaMode.Clip;
                material.AlphaCutoff = gltfMaterial.AlphaCutoff;
            }
            else if (gltfMaterial.Alpha == AlphaMode.BLEND)
            {
                material.AlphaMode = Core.Materials.MaterialAlphaMode.Blend;
            }

            if (gltfMaterial.FindChannel("Emissive") is { } emissive)
            {
                var color = emissive.Color;
                material.EmissiveColor = new Color4(color.X, color.Y, color.Z);
                try { material.EmissiveStrength = emissive.GetFactor("EmissiveStrength"); }
                catch (ArgumentException) { material.EmissiveStrength = 1f; }
                if (TryExtractImage(emissive.Texture, "Emissive", extractedImagePaths, textureDirectory) is { } emissivePath)
                {
                    material.EmissiveTexturePath = emissivePath;
                    // A texture with a black factor still means "no emission" in glTF.
                }
            }

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
