using System.Windows;
using HelixToolkit.Wpf;
using JolieCat3D.Core.Geometry;
using JolieCat3D.Core.Modifiers;
using JolieCat3D.Core.Skinning;
using CoreNode = JolieCat3D.Core.Scene.Node;
using CoreRay = JolieCat3D.Core.Geometry.Ray;
using Vector3 = System.Numerics.Vector3;

namespace JolieCat3D.Engine.Editing
{
    /// <summary>
    /// Weight Paint mode's own per-target state and brush entry point - the same role
    /// <see cref="MeshEditSession"/> plays for vertex/edge/face Edit Mode, just for
    /// painting <see cref="Core.Scene.SkinBinding"/> weights instead of editing mesh
    /// topology. <see cref="Target"/> must already carry a <see cref="Core.Scene.SkinBinding"/>
    /// (see <see cref="Core.Skinning.SkinBindingFactory"/>'s own "Bind to Armature" step) -
    /// there is nothing to paint weights FOR otherwise, so <see cref="PaintAt"/> is
    /// simply a no-op without one, the same "unconfigured, does nothing rather than
    /// throwing" latitude every other optional-data-driven feature in this project
    /// already gives.
    /// </summary>
    public sealed class WeightPaintSession
    {
        public CoreNode? Target { get; private set; }

        /// <summary>Which of <see cref="Target"/>'s own <see cref="Core.Scene.SkinBinding.Bones"/>
        /// entries the brush currently paints - an index into that list (see
        /// <see cref="Core.Scene.SkinBinding"/>'s own remarks on why it's a LOCAL index,
        /// not a scene-wide one), not a <see cref="CoreNode"/> reference itself, so
        /// <c>JolieCat3D.UI</c> can bind it directly to a combo box's own
        /// <c>SelectedIndex</c>.</summary>
        public int ActiveBoneIndex { get; set; }

        public float BrushRadius { get; set; } = 0.3f;

        /// <summary>What the brush pushes <see cref="ActiveBoneIndex"/>'s own weight
        /// TOWARD - 1 to paint its influence in, 0 to erase it (see
        /// <see cref="WeightPaintBrush.Apply"/>'s own remarks).</summary>
        public float TargetWeight { get; set; } = 1f;

        public float Strength { get; set; } = 0.5f;

        /// <summary>Switches onto <paramref name="node"/> (resetting
        /// <see cref="ActiveBoneIndex"/> to 0 - the same "selection/target reset" <see cref="MeshEditSession.Attach"/>
        /// already does when re-targeting) - null exits Weight Paint mode.</summary>
        public void Attach(CoreNode? node)
        {
            Target = node;
            ActiveBoneIndex = 0;
        }

        /// <summary>Casts a ray from <paramref name="position"/> (in <paramref name="viewport"/>'s
        /// own coordinates) against <see cref="Target"/>'s own CURRENT (modifier- and
        /// skinning-evaluated, i.e. exactly what's on screen right now) geometry, in
        /// world space - the nearest triangle intersection, or null if the ray misses
        /// the mesh (or there is no <see cref="Target"/> at all). This is what
        /// <c>JolieCat3D.UI</c>'s own mouse-down/drag handler feeds straight into
        /// <see cref="PaintAt"/>.</summary>
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

        /// <summary>Applies one brush tick at <paramref name="worldHitPoint"/> - a no-op
        /// if <see cref="Target"/> has no mesh, no <see cref="Core.Scene.SkinBinding"/>,
        /// or <see cref="ActiveBoneIndex"/> doesn't (any longer) point at a real entry
        /// in it. Paints the RAW <see cref="Core.Scene.Node.Mesh"/> directly (the same
        /// mesh Edit Mode's own vertex drag mutates) - a weight edit is authoring-time
        /// data, evaluated fresh by <see cref="SkinningEvaluator"/> at render time
        /// exactly like everything else about this mesh.</summary>
        public void PaintAt(Vector3 worldHitPoint)
        {
            if (Target?.Mesh is not { } mesh) return;
            if (Target.SkinBinding is not { } binding) return;
            if (ActiveBoneIndex < 0 || ActiveBoneIndex >= binding.Bones.Count) return;

            WeightPaintBrush.Apply(mesh, Target.GetWorldTransform(), worldHitPoint, BrushRadius, ActiveBoneIndex, TargetWeight, Strength);
        }
    }
}
