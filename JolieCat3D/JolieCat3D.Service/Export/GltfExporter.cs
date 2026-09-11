using System.Numerics;
using JolieCat3D.Core.Geometry;
using JolieCat3D.Core.Scene;
using JolieCat3D.Service.Animation;
using SharpGLTF.Animations;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;
using SharpGLTF.Memory;
using SharpGLTF.Scenes;
using CoreMaterial = JolieCat3D.Core.Materials.Material;
using CoreMesh = JolieCat3D.Core.Geometry.Mesh;
using CoreNode = JolieCat3D.Core.Scene.Node;
using CoreScene = JolieCat3D.Core.Scene.Scene3D;

namespace JolieCat3D.Service.Export
{
    /// <summary>
    /// Writes a whole <see cref="CoreScene"/> (plus, optionally, an <see cref="AnimationTimeline"/>)
    /// to a modern <c>.glb</c>/<c>.gltf</c> file via <c>SharpGLTF.Toolkit</c> - the
    /// "ready for a modern game engine" counterpart to <see cref="Wavefront.ObjExporter"/>/
    /// <see cref="Stl.StlExporter"/>, which carry neither a real node hierarchy, PBR
    /// materials, nor animation at all. Unlike <see cref="Wavefront.ObjExporter"/> (which
    /// bakes every node's geometry into world space, since OBJ has no scene-graph concept
    /// of its own), this preserves the ACTUAL <see cref="Node"/> parent/child hierarchy and
    /// each mesh's own LOCAL-space vertex data - glTF has a real node graph, so there is no
    /// need to flatten anything.
    /// </summary>
    public static class GltfExporter
    {
        /// <summary>The single glTF animation "track name" every animated node's
        /// Translation/Rotation/Scale curves are grouped under - <c>Service.Animation.AnimationTimeline</c>
        /// itself has exactly one global timeline (no separate named clips/takes of its
        /// own), so there is nothing more meaningful to name each per-node curve set after;
        /// SharpGLTF groups every <see cref="NodeBuilder.UseTranslation(string)"/>/
        /// <see cref="NodeBuilder.UseRotation(string)"/>/<see cref="NodeBuilder.UseScale(string)"/>
        /// call sharing this same string into ONE glTF animation, not one per node.</summary>
        private const string AnimationTrackName = "JolieCat3D";

        /// <summary>How many extra linear samples a BEZIER-eased segment (see
        /// <see cref="InterpolationMode.Bezier"/>) is subdivided into when exported - see
        /// <see cref="AddCurve{T}"/>'s own remarks on why glTF needs a dense linear
        /// polyline rather than a direct interpolation-mode translation here.</summary>
        private const int BezierSamplesPerSegment = 16;

        /// <summary>A plain, disclosed unit-conversion constant: <see cref="LightData.Intensity"/>
        /// is a small, unitless brightness multiplier (1 = "unchanged"; see its own
        /// remarks), while glTF's <c>KHR_lights_punctual</c> intensity is a genuine
        /// photometric unit (lux for directional, candela for point/spot) where a
        /// "reasonable, visible" light commonly sits in the hundreds-to-thousands range -
        /// this project has no real photometric model anywhere else either (the same
        /// "no direct equivalent, so approximate the simplest sane way" reasoning
        /// <see cref="Engine.Geometry.MaterialFactory.ComputeSpecular"/> already applies to
        /// Roughness/Metallic), so a single fixed scale factor is used for every light
        /// type rather than pretending to model the real physical difference between lux
        /// and candela.</summary>
        private const float LightIntensityToGltfScale = 1000f;

        /// <summary>
        /// Exports <paramref name="scene"/> (and, if given, every keyframe in
        /// <paramref name="timeline"/>) to <paramref name="filePath"/> - <c>.glb</c>
        /// (single self-contained binary file, textures embedded) unless the path's own
        /// extension is <c>.gltf</c>, in which case SharpGLTF's own separate-files JSON
        /// form is written instead. <paramref name="timeline"/> is optional (null exports
        /// the scene's static pose only, no animation at all) since not every caller
        /// necessarily has one in hand.
        /// </summary>
        public static void Export(CoreScene scene, AnimationTimeline? timeline, string filePath)
        {
            ArgumentNullException.ThrowIfNull(scene);
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

            var sceneBuilder = new SceneBuilder(scene.Name);
            var materialCache = new Dictionary<CoreMaterial, MaterialBuilder>();
            var imageCache = new Dictionary<string, ImageBuilder>();
            var nodeBuilders = new Dictionary<CoreNode, NodeBuilder>();

            foreach (var root in scene.RootNodes)
                BuildNode(root, parent: null, sceneBuilder, nodeBuilders, materialCache, imageCache);

            if (timeline is not null)
                foreach (var track in timeline.Tracks)
                    if (nodeBuilders.TryGetValue(track.Target, out var builder))
                        AddAnimation(builder, track);

            var model = sceneBuilder.ToGltf2();

            var fullPath = Path.GetFullPath(filePath);
            if (string.Equals(Path.GetExtension(fullPath), ".gltf", StringComparison.OrdinalIgnoreCase))
                model.SaveGLTF(fullPath);
            else
                model.SaveGLB(fullPath);
        }

        // ============================= Node graph =============================

        /// <summary>Builds <paramref name="node"/>'s own <see cref="NodeBuilder"/> (parented
        /// under <paramref name="parent"/>'s, or a fresh root if null), recording it into
        /// <paramref name="nodeBuilders"/> (so a later <see cref="AnimationTrack"/> pass can
        /// find it again by its original <see cref="CoreNode"/>), attaches whatever
        /// mesh/camera/light it carries, and recurses over every child - the same
        /// depth-first walk <see cref="Node.Traverse"/> itself uses, just rebuilding the
        /// SAME parent/child shape one level at a time instead of flattening it. Every
        /// node built is also registered via <see cref="SceneBuilder.AddNode"/>
        /// unconditionally, regardless of whether it also carries a mesh/camera/light or
        /// has any children of its own - a bare empty pivot with nothing attached and
        /// nothing under it would otherwise never be referenced by anything at all and
        /// silently vanish from the exported hierarchy.</summary>
        private static void BuildNode(
            CoreNode node, NodeBuilder? parent, SceneBuilder sceneBuilder,
            Dictionary<CoreNode, NodeBuilder> nodeBuilders,
            Dictionary<CoreMaterial, MaterialBuilder> materialCache,
            Dictionary<string, ImageBuilder> imageCache)
        {
            var builder = parent is null ? new NodeBuilder(node.Name) : parent.CreateNode(node.Name);
            builder.WithLocalTranslation(node.LocalPosition);
            builder.WithLocalRotation(node.LocalRotation);
            builder.WithLocalScale(node.LocalScale);

            nodeBuilders[node] = builder;
            sceneBuilder.AddNode(builder);

            if (node.Mesh is { } mesh && mesh.Vertices.Count > 0)
                sceneBuilder.AddRigidMesh(BuildMesh(mesh, materialCache, imageCache), builder);

            if (node.Camera is { } camera)
                sceneBuilder.AddCamera(BuildCamera(camera), CreateLookDirectionFix(builder, "$CameraLookFix"));

            if (node.Light is { } light)
                sceneBuilder.AddLight(BuildLight(light), CreateLookDirectionFix(builder, "$LightLookFix"));

            foreach (var child in node.Children)
                BuildNode(child, builder, sceneBuilder, nodeBuilders, materialCache, imageCache);
        }

        /// <summary>glTF's own camera/light convention points local -Z "forward" (see
        /// <see cref="SharpGLTF.Scenes.CameraBuilder.LocalDirection"/>/<see cref="SharpGLTF.Scenes.LightBuilder.LocalDirection"/>,
        /// both confirmed empirically before relying on this), while THIS project's own
        /// <see cref="Node.GetWorldForward"/> convention points local +Z - the exact
        /// opposite axis. Rather than flip <paramref name="parent"/>'s own transform (which
        /// would also incorrectly flip any MESH attached to that same node, and any
        /// keyframed animation driving it), this adds one small, otherwise-invisible CHILD
        /// node rotated 180 degrees around Y (flips forward without touching up/right) and
        /// hands THAT to <see cref="SceneBuilder.AddCamera(SharpGLTF.Scenes.CameraBuilder,SharpGLTF.Scenes.NodeBuilder)"/>/
        /// <see cref="SceneBuilder.AddLight(SharpGLTF.Scenes.LightBuilder,SharpGLTF.Scenes.NodeBuilder)"/>
        /// instead - <paramref name="parent"/> itself (and everything else attached to or
        /// parented under it) keeps this project's own convention untouched.</summary>
        private static NodeBuilder CreateLookDirectionFix(NodeBuilder parent, string name) =>
            parent.CreateNode(name).WithLocalRotation(Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI));

        // ============================= Mesh / Material =============================

        private static IMeshBuilder<MaterialBuilder> BuildMesh(
            CoreMesh mesh, Dictionary<CoreMaterial, MaterialBuilder> materialCache, Dictionary<string, ImageBuilder> imageCache)
        {
            var materialBuilder = GetOrCreateMaterial(mesh.Material, materialCache, imageCache);
            var meshBuilder = new MeshBuilder<MaterialBuilder, VertexPositionNormal, VertexTexture1, VertexEmpty>(mesh.Name);
            var primitive = meshBuilder.UsePrimitive(materialBuilder, 3);

            // A DiffuseTexturePath sub-rectangle (see Material.DiffuseTextureOffset/Scale's
            // own remarks on the "one shared atlas, many named sub-rects" sprite-sheet
            // case) has no equivalent in a plain MaterialBuilder without reaching for a
            // KHR_texture_transform extension this project has never exercised - baking
            // the same offset/scale straight into every exported vertex's own UV instead
            // is always correct with zero extension risk, and a no-op for the overwhelming
            // common case (offset (0,0), scale (1,1) - the whole image) since it leaves
            // the UV completely unchanged.
            var uvOffset = mesh.Material?.DiffuseTextureOffset ?? Vector2.Zero;
            var uvScale = mesh.Material?.DiffuseTextureScale ?? Vector2.One;

            var vertexBuilders = new VertexBuilder<VertexPositionNormal, VertexTexture1, VertexEmpty>[mesh.Vertices.Count];
            for (var i = 0; i < mesh.Vertices.Count; i++)
            {
                var vertex = mesh.Vertices[i];
                var uv = uvOffset + vertex.UV * uvScale;
                var geometry = new VertexPositionNormal(vertex.Position.X, vertex.Position.Y, vertex.Position.Z, vertex.Normal.X, vertex.Normal.Y, vertex.Normal.Z);
                vertexBuilders[i] = new VertexBuilder<VertexPositionNormal, VertexTexture1, VertexEmpty>(geometry, new VertexTexture1(uv));
            }

            foreach (var face in mesh.GetRenderFaces())
                primitive.AddTriangle(vertexBuilders[face.A], vertexBuilders[face.B], vertexBuilders[face.C]);

            return meshBuilder;
        }

        private static MaterialBuilder GetOrCreateMaterial(
            CoreMaterial? material, Dictionary<CoreMaterial, MaterialBuilder> cache, Dictionary<string, ImageBuilder> imageCache)
        {
            material ??= CoreMaterial.CreateDefault();
            if (cache.TryGetValue(material, out var existing)) return existing;

            var builder = new MaterialBuilder(material.Name).WithMetallicRoughnessShader();
            var opacity = Math.Clamp(material.Opacity, 0f, 1f);

            // Diffuse/albedo - a texture path present means "sampled instead of, not
            // blended with, DiffuseColor" (the same convention MaterialFactory's own
            // CreateDiffuseBrush already follows for the live viewport), so the exported
            // baseColorFactor is left at white (only opacity carried through) rather than
            // additionally tinting the texture by DiffuseColor too.
            if (TryLoadImage(material.DiffuseTexturePath, material.Name + "_BaseColor", imageCache) is { } baseColorImage)
                builder.WithBaseColor(baseColorImage, new Vector4(1f, 1f, 1f, opacity));
            else
                builder.WithBaseColor(new Vector4(Clamp01(material.DiffuseColor.R), Clamp01(material.DiffuseColor.G), Clamp01(material.DiffuseColor.B), opacity));

            if (opacity < 1f) builder.WithAlpha(AlphaMode.BLEND, 0.5f);

            if (TryLoadImage(material.MetallicRoughnessTexturePath, material.Name + "_ORM", imageCache) is { } ormImage)
                builder.WithMetallicRoughness(ormImage, material.Metallic, material.Roughness);
            else
                builder.WithMetallicRoughness(material.Metallic, material.Roughness);

            if (TryLoadImage(material.NormalTexturePath, material.Name + "_Normal", imageCache) is { } normalImage)
                builder.WithNormal(normalImage, 1f);

            cache[material] = builder;
            return builder;
        }

        private static float Clamp01(float value) => Math.Clamp(value, 0f, 1f);

        /// <summary>Loads <paramref name="path"/> as an <see cref="ImageBuilder"/> - null
        /// (no image at all, the same "missing/unreadable texture degrades visibly rather
        /// than crashing" tolerance <c>MaterialFactory.CreateDiffuseBrush</c> already
        /// applies) for a null/empty path or one that doesn't resolve to a real file.
        /// Reads the WHOLE file with <see cref="File.ReadAllBytes"/> up front - which opens,
        /// reads, and closes the file itself in one call - rather than <see cref="MemoryImage"/>'s
        /// own string-path constructor, so this export can never be left holding a file
        /// lock on a source texture afterward, satisfying the exact same
        /// "release file locks properly" concern the STL/OBJ exporters already meet by
        /// only ever holding their own output <see cref="StreamWriter"/>, never a source
        /// file, open across their own write. Cached per resolved path within a single
        /// <see cref="Export"/> call (never across calls - <paramref name="imageCache"/> is
        /// always a fresh, call-scoped dictionary) purely so the SAME texture referenced by
        /// several materials/channels isn't re-read/re-embedded more than once.</summary>
        private static ImageBuilder? TryLoadImage(string? path, string name, Dictionary<string, ImageBuilder> imageCache)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;

            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(path);
                if (!File.Exists(fullPath)) return null;
            }
            catch (Exception)
            {
                return null;
            }

            if (imageCache.TryGetValue(fullPath, out var cached)) return cached;

            try
            {
                var bytes = File.ReadAllBytes(fullPath);
                var image = ImageBuilder.From(new MemoryImage(bytes), name);
                imageCache[fullPath] = image;
                return image;
            }
            catch (Exception)
            {
                return null;
            }
        }

        // ============================= Camera / Light =============================

        private static CameraBuilder BuildCamera(CameraData camera) => camera.ProjectionMode == CameraProjectionMode.Orthographic
            ? new CameraBuilder.Orthographic(camera.OrthographicWidth / 2f, camera.OrthographicWidth / 2f, camera.NearPlaneDistance, camera.FarPlaneDistance)
            : new CameraBuilder.Perspective(null, camera.FieldOfView * MathF.PI / 180f, camera.NearPlaneDistance, camera.FarPlaneDistance);

        private static LightBuilder BuildLight(LightData light)
        {
            LightBuilder builder = light.Type switch
            {
                LightType.Point => new LightBuilder.Point { Range = light.Range },
                // SpotAngle is the FULL cone angle in degrees (see LightData's own
                // remarks); glTF's own OuterConeAngle is the HALF-angle, in radians, and
                // must stay strictly inside (0, PI/2] - this project has no separate
                // inner/outer penumbra of its own (just one angle), so InnerConeAngle is
                // left at 0 (a hard-edged cone), a disclosed simplification rather than
                // inventing a penumbra value nothing in this app's own model provides.
                LightType.Spot => new LightBuilder.Spot
                {
                    Range = light.Range,
                    InnerConeAngle = 0f,
                    OuterConeAngle = Math.Clamp(light.SpotAngle * MathF.PI / 360f, 0.001f, MathF.PI / 2f - 0.001f),
                },
                _ => new LightBuilder.Directional(),
            };

            builder.Color = new Vector3(light.Color.R, light.Color.G, light.Color.B);
            builder.Intensity = light.Intensity * LightIntensityToGltfScale;
            return builder;
        }

        // ============================= Animation =============================

        /// <summary>Adds <paramref name="track"/>'s own Translation/Rotation/Scale curves
        /// onto <paramref name="builder"/> - a no-op if it has no keyframes at all.</summary>
        private static void AddAnimation(NodeBuilder builder, AnimationTrack track)
        {
            var keyframes = track.Keyframes;
            if (keyframes.Count == 0) return;

            AddCurve(builder.UseTranslation(AnimationTrackName), keyframes, k => k.Position, t => track.Evaluate(t)?.Position);
            AddCurve(builder.UseRotation(AnimationTrackName), keyframes, k => k.Rotation, t => track.Evaluate(t)?.Rotation);
            AddCurve(builder.UseScale(AnimationTrackName), keyframes, k => k.Scale, t => track.Evaluate(t)?.Scale);
        }

        /// <summary>
        /// Walks <paramref name="keyframes"/> pairwise, adding one glTF curve point per
        /// LINEAR-leaving keyframe directly (<paramref name="selectValue"/> reads that exact
        /// recorded value - bit-for-bit what <see cref="AnimationTrack.Evaluate"/> itself
        /// returns for that same time), but densely SAMPLING a BEZIER-leaving segment
        /// instead (via <paramref name="evaluateAt"/> - the SAME already-verified
        /// <see cref="AnimationTrack.Evaluate"/> this project's own timeline scrubber
        /// preview uses, not a re-derivation of its easing math here) at
        /// <see cref="BezierSamplesPerSegment"/> evenly-spaced points across the segment.
        ///
        /// This project's own <see cref="InterpolationMode.Bezier"/> re-maps the raw time
        /// fraction through <see cref="CubicBezierEasing"/> before lerping/slerping - a
        /// completely different curve SHAPE from glTF's own <c>CUBICSPLINE</c>
        /// interpolation (which needs explicit in/out TANGENT vectors per keyframe, a
        /// Hermite curve, not a re-timed lerp). Emitting a construct that LOOKS like a
        /// cubic spline but eases along a different curve than this app's own preview
        /// would be a worse, silent mismatch than a dense straight-line (LINEAR) polyline
        /// that visibly reproduces the exact same eased motion shape a glTF viewer will
        /// show - so every point this method ever adds is <c>isLinear: true</c>, regardless
        /// of which of this project's own interpolation modes produced it.
        /// </summary>
        private static void AddCurve<T>(
            CurveBuilder<T> curve, IReadOnlyList<Keyframe> keyframes,
            Func<Keyframe, T> selectValue, Func<double, T?> evaluateAt)
            where T : struct
        {
            curve.SetPoint((float)keyframes[0].Time, selectValue(keyframes[0]), true);

            for (var i = 0; i < keyframes.Count - 1; i++)
            {
                var before = keyframes[i];
                var after = keyframes[i + 1];

                if (before.Interpolation == InterpolationMode.Linear)
                {
                    curve.SetPoint((float)after.Time, selectValue(after), true);
                    continue;
                }

                for (var sample = 1; sample <= BezierSamplesPerSegment; sample++)
                {
                    var t = before.Time + (after.Time - before.Time) * sample / (double)BezierSamplesPerSegment;
                    if (evaluateAt(t) is not { } value) continue;
                    curve.SetPoint((float)t, value, true);
                }
            }
        }
    }
}
