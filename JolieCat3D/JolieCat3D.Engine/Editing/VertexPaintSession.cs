using System.Windows;
using HelixToolkit.Wpf;
using JolieCat3D.Core.Geometry;
using JolieCat3D.Core.Materials;
using JolieCat3D.Core.Modifiers;
using JolieCat3D.Core.Numerics;
using JolieCat3D.Core.Skinning;
using CoreNode = JolieCat3D.Core.Scene.Node;
using CoreRay = JolieCat3D.Core.Geometry.Ray;
using Vector3 = System.Numerics.Vector3;

namespace JolieCat3D.Engine.Editing
{
    /// <summary>
    /// Vertex Paint mode's own per-target state and brush entry point - the same
    /// "world hit point + radius, one tick per mouse-move sample" shape
    /// <see cref="WeightPaintSession"/> already established, simpler here since a
    /// vertex color paints onto ANY meshed node directly (no armature/skin binding
    /// prerequisite the way Weight Paint's own <see cref="Attach"/> target needs).
    /// </summary>
    public sealed class VertexPaintSession
    {
        public CoreNode? Target { get; private set; }

        public Color4 TargetColor { get; set; } = Color4.Red;

        public float BrushRadius { get; set; } = 0.3f;

        public float Strength { get; set; } = 0.5f;

        public void Attach(CoreNode? node) => Target = node;

        /// <summary>Casts a ray from <paramref name="position"/> against
        /// <see cref="Target"/>'s own CURRENT (modifier- and skinning-evaluated - the
        /// exact same "raycast against what's actually on screen right now" shape
        /// <see cref="WeightPaintSession.RaycastWorldHitPoint"/> already uses)
        /// geometry, in world space.</summary>
        public Vector3? RaycastWorldHitPoint(HelixViewport3D viewport, Point position)
        {
            ArgumentNullException.ThrowIfNull(viewport);
            if (Target?.Mesh is not { } mesh) return null;

            var ray3D = Viewport3DHelper.Point2DtoRay3D(viewport.Viewport, position);
            var ray = new CoreRay(
                new Vector3((float)ray3D.Origin.X, (float)ray3D.Origin.Y, (float)ray3D.Origin.Z),
                new Vector3((float)ray3D.Direction.X, (float)ray3D.Direction.Y, (float)ray3D.Direction.Z));

            var worldTransform = Target.GetWorldTransform();
            var evaluatedMesh = ModifierStack.Evaluate(mesh, Target.Modifiers, Target);
            var skinnedMesh = Target.SkinBinding is { } binding
                ? SkinningEvaluator.Deform(evaluatedMesh, worldTransform, binding)
                : evaluatedMesh;

            float? nearestDistance = null;
            var nearestPoint = Vector3.Zero;

            foreach (var face in skinnedMesh.GetRenderFaces())
            {
                var a = Vector3.Transform(skinnedMesh.Vertices[face.A].Position, worldTransform);
                var b = Vector3.Transform(skinnedMesh.Vertices[face.B].Position, worldTransform);
                var c = Vector3.Transform(skinnedMesh.Vertices[face.C].Position, worldTransform);

                if (RayIntersection.IntersectTriangle(ray, a, b, c) is not { } distance) continue;
                if (nearestDistance is not null && distance >= nearestDistance) continue;

                nearestDistance = distance;
                nearestPoint = ray.Origin + ray.Direction * distance;
            }

            return nearestDistance is null ? null : nearestPoint;
        }

        /// <summary>Applies one brush tick at <paramref name="worldHitPoint"/> - a
        /// no-op if <see cref="Target"/> has no mesh. Paints the RAW <see cref="Core.Scene.Node.Mesh"/>
        /// directly, exactly like <see cref="WeightPaintSession.PaintAt"/> does for
        /// bone weights.</summary>
        public void PaintAt(Vector3 worldHitPoint)
        {
            if (Target?.Mesh is not { } mesh) return;
            VertexPaintBrush.Apply(mesh, Target.GetWorldTransform(), worldHitPoint, BrushRadius, TargetColor, Strength);
        }
    }
}
