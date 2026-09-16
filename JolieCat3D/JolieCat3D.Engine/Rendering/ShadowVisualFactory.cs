using System.Numerics;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using JolieCat3D.Core.Modifiers;
using CoreLightType = JolieCat3D.Core.Scene.LightType;
using CoreMesh = JolieCat3D.Core.Geometry.Mesh;
using CoreNode = JolieCat3D.Core.Scene.Node;
using CoreScene = JolieCat3D.Core.Scene.Scene3D;

namespace JolieCat3D.Engine.Rendering
{
    /// <summary>
    /// Builds a shadow visual for Directional/Spot <see cref="Core.Scene.LightData"/>
    /// nodes - as close to "real-time Shadow Mapping" as this project's actual rendering
    /// pipeline can get. WPF's <see cref="Model3D"/> pipeline is fixed-function
    /// Direct3D9 media integration with no programmable vertex/pixel shader hook and no
    /// depth-buffer access of any kind (the same structural platform limitation
    /// <see cref="Core.Materials.Material"/>'s own remarks already disclose for normal
    /// mapping/environment reflections) - there is no way to build an actual shadow MAP
    /// (a depth texture rendered from the light's own point of view, sampled back while
    /// shading every other surface) in it at all. What this class builds instead is a
    /// planar PROJECTED shadow: each shadow-casting mesh's own silhouette, flattened
    /// onto the scene's ground plane (Y=0 - this project's own established "ground"
    /// convention, see <c>Scene3DRenderer</c>'s own <c>GridLinesVisual3D</c>) along each
    /// light's own direction, rendered as a dark, unlit, semi-transparent mesh - the
    /// same honest technique every fixed-function 3D engine used for "shadows" before
    /// real shadow mapping existed, recomputed fresh every render so it tracks a moving
    /// object or an animated light in real time. A REAL, disclosed limitation follows
    /// from the technique itself, not from a shortcut taken here: a shadow only ever
    /// lands on the ground plane - there is no way to receive one onto arbitrary OTHER
    /// mesh geometry without an actual depth-buffer-based shadow map.
    /// </summary>
    public static class ShadowVisualFactory
    {
        private static readonly Material ShadowMaterial = CreateShadowMaterial();

        // A hair above the ground plane itself - avoids z-fighting against
        // Scene3DRenderer's own GridLinesVisual3D, drawn exactly at Y=0.
        private const float GroundOffset = 0.001f;

        private static Material CreateShadowMaterial()
        {
            // EmissiveMaterial, not DiffuseMaterial: a shadow should darken whatever's
            // beneath it by a CONSTANT amount regardless of the scene's own lighting -
            // a lit DiffuseMaterial shadow blob would itself brighten under direct
            // light, visibly contradicting the very thing it's meant to represent.
            var brush = new SolidColorBrush(Color.FromArgb(0x60, 0x00, 0x00, 0x00));
            brush.Freeze();
            var material = new EmissiveMaterial(brush);
            material.Freeze();
            return material;
        }

        /// <summary>Every Directional/Spot light node's own projected shadow, for every
        /// OTHER meshed node in <paramref name="scene"/>, combined into one
        /// <see cref="Model3D"/> - null if there are no shadow-casting lights, no
        /// meshed nodes at all, or nothing actually projects (see
        /// <see cref="BuildDirectionalShadow"/>/<see cref="BuildSpotShadow"/>'s own
        /// remarks on when a given light/mesh pair produces nothing).</summary>
        public static Model3D? CreateShadows(CoreScene scene)
        {
            ArgumentNullException.ThrowIfNull(scene);

            var allNodes = scene.Traverse().ToList();
            var lights = allNodes.Where(n => n.Light is { Type: CoreLightType.Directional or CoreLightType.Spot }).ToList();
            if (lights.Count == 0) return null;

            var casters = allNodes.Where(n => n.Mesh is { Vertices.Count: > 0 }).ToList();
            if (casters.Count == 0) return null;

            var group = new Model3DGroup();

            foreach (var lightNode in lights)
            {
                var light = lightNode.Light!;
                foreach (var caster in casters)
                {
                    if (ReferenceEquals(caster, lightNode)) continue;

                    var geometry = light.Type == CoreLightType.Directional
                        ? BuildDirectionalShadow(caster, lightNode)
                        : BuildSpotShadow(caster, lightNode);
                    if (geometry is null) continue;

                    group.Children.Add(new GeometryModel3D(geometry, ShadowMaterial));
                }
            }

            if (group.Children.Count == 0) return null;
            group.Freeze();
            return group;
        }

        /// <summary>Projects <paramref name="caster"/>'s own evaluated mesh onto the
        /// ground plane along <paramref name="lightNode"/>'s own world-forward
        /// direction (the same direction <see cref="Lighting.SceneLightingFactory"/>
        /// already hands a <see cref="DirectionalLight"/> as its own
        /// <see cref="DirectionalLight.Direction"/> - the direction the light itself
        /// travels). Null for a light traveling (near-)horizontally (Y component near
        /// zero - no well-defined ground intersection at all) - see
        /// <see cref="BuildFlattenedGeometry"/>'s own remarks for the rest of the
        /// per-vertex projection contract.</summary>
        private static MeshGeometry3D? BuildDirectionalShadow(CoreNode caster, CoreNode lightNode)
        {
            var forward = lightNode.GetWorldForward();
            if (MathF.Abs(forward.Y) < 1e-4f) return null;

            var mesh = ModifierStack.Evaluate(caster.Mesh!, caster.Modifiers, caster);
            var world = caster.GetWorldTransform();

            Vector3? Project(Vector3 localVertex)
            {
                var worldVertex = Vector3.Transform(localVertex, world);
                var t = -worldVertex.Y / forward.Y;
                // t <= 0 means continuing along the light's OWN travel direction moves
                // AWAY from the ground, not toward it (the light is traveling upward
                // relative to this vertex, or the vertex is already at/below Y=0) - no
                // physically sensible shadow point for this one vertex.
                if (t <= 0f) return null;

                var projected = worldVertex + forward * t;
                return new Vector3(projected.X, GroundOffset, projected.Z);
            }

            return BuildFlattenedGeometry(mesh, Project);
        }

        /// <summary>Projects <paramref name="caster"/>'s own evaluated mesh onto the
        /// ground plane from <paramref name="lightNode"/>'s own world POSITION (a Spot
        /// light, unlike Directional, radiates from a point - the same
        /// <see cref="Core.Scene.Node.GetWorldPosition"/> <see cref="Lighting.SceneLightingFactory"/>
        /// already hands a <see cref="SpotLight"/>). The projected shadow's own
        /// silhouette is not clipped to the spot's own cone angle - a real, disclosed
        /// simplification (this technique has no per-pixel falloff of any kind to clip
        /// against in the first place), not a claim that light outside the cone is
        /// actually casting it. Null for a vertex at (or extremely near) the light's own
        /// height, or where the projection would fall BEHIND the vertex rather than
        /// beyond it (the light is below the vertex, or level with it) - see
        /// <see cref="BuildFlattenedGeometry"/>'s own remarks for the rest of the
        /// per-vertex projection contract.</summary>
        private static MeshGeometry3D? BuildSpotShadow(CoreNode caster, CoreNode lightNode)
        {
            var lightPosition = lightNode.GetWorldPosition();

            var mesh = ModifierStack.Evaluate(caster.Mesh!, caster.Modifiers, caster);
            var world = caster.GetWorldTransform();

            Vector3? Project(Vector3 localVertex)
            {
                var worldVertex = Vector3.Transform(localVertex, world);
                var toVertex = worldVertex - lightPosition;
                if (MathF.Abs(toVertex.Y) < 1e-4f) return null;

                var s = -lightPosition.Y / toVertex.Y;
                // s must land BEYOND the vertex itself (s > 1, since s = 1 is the
                // vertex's own position along this ray) - otherwise the "shadow" would
                // fall between the light and the object, or behind the light
                // altogether, neither of which is a real shadow.
                if (s <= 1f) return null;

                var projected = lightPosition + toVertex * s;
                return new Vector3(projected.X, GroundOffset, projected.Z);
            }

            return BuildFlattenedGeometry(mesh, Project);
        }

        /// <summary>Builds a flattened <see cref="MeshGeometry3D"/> from
        /// <paramref name="mesh"/>'s own triangles, replacing each vertex's position
        /// with whatever <paramref name="project"/> (given that vertex's own LOCAL
        /// position) returns - a face is INCLUDED only if all 3 of its own vertices
        /// projected successfully; one that didn't (see <see cref="BuildDirectionalShadow"/>/
        /// <see cref="BuildSpotShadow"/>'s own remarks on when that happens) is simply
        /// dropped from the shadow entirely, rather than guessing at a position for it.
        /// Null if NO face projected at all (nothing to draw). A flat upward normal on
        /// every vertex - a ground-projected shadow decal has no meaningful surface
        /// curvature of its own to shade, and <see cref="ShadowMaterial"/> is an
        /// <see cref="EmissiveMaterial"/> anyway (unaffected by normals/lighting either
        /// way).</summary>
        private static MeshGeometry3D? BuildFlattenedGeometry(CoreMesh mesh, Func<Vector3, Vector3?> project)
        {
            var projected = new Vector3?[mesh.Vertices.Count];
            for (var i = 0; i < mesh.Vertices.Count; i++)
                projected[i] = project(mesh.Vertices[i].Position);

            var geometry = new MeshGeometry3D();
            var indexRemap = new Dictionary<int, int>();

            int GetOrAddVertex(int sourceIndex)
            {
                if (indexRemap.TryGetValue(sourceIndex, out var existingIndex)) return existingIndex;

                var position = projected[sourceIndex]!.Value;
                geometry.Positions.Add(new Point3D(position.X, position.Y, position.Z));
                geometry.Normals.Add(new Vector3D(0, 1, 0));

                var newIndex = geometry.Positions.Count - 1;
                indexRemap[sourceIndex] = newIndex;
                return newIndex;
            }

            foreach (var face in mesh.GetRenderFaces())
            {
                if (projected[face.A] is null || projected[face.B] is null || projected[face.C] is null) continue;

                geometry.TriangleIndices.Add(GetOrAddVertex(face.A));
                geometry.TriangleIndices.Add(GetOrAddVertex(face.B));
                geometry.TriangleIndices.Add(GetOrAddVertex(face.C));
            }

            if (geometry.TriangleIndices.Count == 0) return null;

            geometry.Freeze();
            return geometry;
        }
    }
}
