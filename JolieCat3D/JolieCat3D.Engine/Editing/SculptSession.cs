using System.Windows;
using HelixToolkit.Wpf;
using System.Numerics;
using JolieCat3D.Core.Geometry;
using JolieCat3D.Core.Modifiers;
using JolieCat3D.Core.Sculpting;
using JolieCat3D.Core.Skinning;
using CoreNode = JolieCat3D.Core.Scene.Node;
using CoreRay = JolieCat3D.Core.Geometry.Ray;
using Vector3 = System.Numerics.Vector3;

namespace JolieCat3D.Engine.Editing
{
    /// <summary>
    /// Sculpt Mode's own per-target state and brush entry point - the same
    /// "world hit point + radius, one tick per mouse-move sample, mutate the RAW
    /// <see cref="Core.Scene.Node.Mesh"/> directly" shape <see cref="WeightPaintSession"/>/
    /// <see cref="VertexPaintSession"/> already established, extended with an actual
    /// stroke lifecycle (<see cref="BeginStroke"/>/<see cref="UpdateStroke"/>/
    /// <see cref="EndStroke"/>) because Grab (unlike Draw/Smooth, which simply
    /// re-evaluate "what's under the brush" fresh every tick) needs to remember the
    /// WHOLE drag's own fixed affected-set, original positions, and drag plane from
    /// the moment it starts - see <see cref="SculptBrush.ApplyGrab"/>'s own remarks.
    /// </summary>
    public sealed class SculptSession
    {
        public CoreNode? Target { get; private set; }

        public SculptBrushMode Mode { get; set; } = SculptBrushMode.Draw;

        public float BrushRadius { get; set; } = 0.3f;

        /// <summary>What each brush tick scales its own effect by - a real world-unit
        /// displacement magnitude for Draw, a 0-1 relax blend factor for Smooth, and a
        /// 0-1 multiplier on the raw drag delta for Grab (see each of
        /// <see cref="SculptBrush"/>'s own methods for their exact own units) - one
        /// slider, three different meanings, the same latitude the task's own brush
        /// description already gives it.</summary>
        public float Strength { get; set; } = 0.5f;

        /// <summary>Draw only - Shift-held pushes a dent instead of a bump. <c>JolieCat3D.UI</c>'s
        /// own mouse handler sets this directly from the live Shift-key state before
        /// every <see cref="BeginStroke"/>/<see cref="UpdateStroke"/> call, exactly the
        /// task's own "outward (or inward if holding Shift)" wording.</summary>
        public bool Invert { get; set; }

        private Dictionary<int, (Vector3 LocalPosition, float Falloff)>? _grabOriginalLocalPositions;
        private Vector3 _grabOriginWorldPoint;
        private Vector3 _grabPlaneNormal;
        private bool _strokeActive;

        /// <summary>Switches onto <paramref name="node"/> (ending any in-progress
        /// stroke first - the same re-target reset <see cref="WeightPaintSession.Attach"/>
        /// already does for its own bone index) - null exits Sculpt Mode.</summary>
        public void Attach(CoreNode? node)
        {
            Target = node;
            EndStroke();
        }

        /// <summary>Casts a ray from <paramref name="position"/> against
        /// <see cref="Target"/>'s own CURRENT (modifier- and skinning-evaluated)
        /// geometry, in world space - the same shape <see cref="WeightPaintSession.RaycastWorldHitPoint"/>
        /// already uses, exposed here too so <c>JolieCat3D.UI</c> can preview the
        /// brush's own position (e.g. a radius ring cursor) without starting a
        /// stroke.</summary>
        public Vector3? RaycastWorldHitPoint(HelixViewport3D viewport, Point position) =>
            TryRaycast(viewport, position, out _, out var worldHitPoint) ? worldHitPoint : null;

        /// <summary>Starts a new stroke at <paramref name="position"/> - for Draw/Smooth
        /// this is simply their own first tick (see <see cref="UpdateStroke"/>); for
        /// Grab this instead CAPTURES the whole drag's own fixed state up front: every
        /// affected vertex's own original local position and falloff (via
        /// <see cref="SculptBrush.FindAffected"/>, evaluated ONCE, never again for the
        /// rest of the drag), plus a FIXED plane - through the initial hit point,
        /// facing back toward the camera along the ray's own reversed direction - that
        /// every subsequent <see cref="UpdateStroke"/> call intersects a FRESH ray
        /// against (rather than re-raycasting the mesh itself, which would be unstable
        /// once the mesh starts deforming mid-drag).</summary>
        public void BeginStroke(HelixViewport3D viewport, Point position)
        {
            if (Target?.Mesh is not { } mesh) return;
            if (!TryRaycast(viewport, position, out var ray, out var worldHitPoint)) return;

            _strokeActive = true;

            if (Mode == SculptBrushMode.Grab)
            {
                var worldTransform = Target.GetWorldTransform();
                var affected = SculptBrush.FindAffected(mesh, worldTransform, BrushRadius, worldHitPoint);
                _grabOriginalLocalPositions = affected.Count == 0
                    ? null
                    : affected.ToDictionary(a => a.Index, a => (mesh.Vertices[a.Index].Position, a.Falloff));
                _grabOriginWorldPoint = worldHitPoint;
                _grabPlaneNormal = -ray.Direction;
            }
            else
            {
                ApplyDrawOrSmooth(mesh, worldHitPoint);
            }
        }

        /// <summary>One more tick of the SAME stroke <see cref="BeginStroke"/> started -
        /// Draw/Smooth re-raycast fresh (the brush itself doesn't move relative to the
        /// mesh, only the mouse does, so "what's under it now" is recomputed every
        /// time); Grab instead intersects this tick's OWN ray against the fixed plane
        /// <see cref="BeginStroke"/> already established, derives the cumulative
        /// world-space delta from the drag's own original hit point, and hands that
        /// straight to <see cref="SculptBrush.ApplyGrab"/> along with the SAME captured
        /// original positions - never the mesh's own already-moved current state. A
        /// no-op if no stroke is active (no matching <see cref="BeginStroke"/> call) or
        /// <see cref="Target"/> has no mesh.</summary>
        public void UpdateStroke(HelixViewport3D viewport, Point position)
        {
            if (!_strokeActive) return;
            if (Target?.Mesh is not { } mesh) return;

            if (Mode == SculptBrushMode.Grab)
            {
                if (_grabOriginalLocalPositions is null) return;

                var ray = BuildRay(viewport, position);
                if (RayIntersection.IntersectPlane(ray, _grabOriginWorldPoint, _grabPlaneNormal) is not { } distance) return;

                var currentWorldPoint = ray.Origin + ray.Direction * distance;
                var worldDelta = currentWorldPoint - _grabOriginWorldPoint;
                SculptBrush.ApplyGrab(mesh, Target.GetWorldTransform(), _grabOriginalLocalPositions, worldDelta, Strength);
            }
            else
            {
                if (!TryRaycast(viewport, position, out _, out var worldHitPoint)) return;
                ApplyDrawOrSmooth(mesh, worldHitPoint);
            }
        }

        /// <summary>Ends the current stroke (a no-op if none is active) - releases
        /// Grab's own captured original positions, so the NEXT <see cref="BeginStroke"/>
        /// (whatever mode it turns out to be) starts completely fresh.</summary>
        public void EndStroke()
        {
            _strokeActive = false;
            _grabOriginalLocalPositions = null;
        }

        private void ApplyDrawOrSmooth(Core.Geometry.Mesh mesh, Vector3 worldHitPoint)
        {
            var worldTransform = Target!.GetWorldTransform();
            switch (Mode)
            {
                case SculptBrushMode.Draw:
                    SculptBrush.ApplyDraw(mesh, worldTransform, worldHitPoint, BrushRadius, Strength, Invert);
                    break;
                case SculptBrushMode.Smooth:
                    SculptBrush.ApplySmooth(mesh, worldTransform, worldHitPoint, BrushRadius, Strength);
                    break;
            }
        }

        private static CoreRay BuildRay(HelixViewport3D viewport, Point position)
        {
            var ray3D = Viewport3DHelper.Point2DtoRay3D(viewport.Viewport, position);
            return new CoreRay(
                new Vector3((float)ray3D.Origin.X, (float)ray3D.Origin.Y, (float)ray3D.Origin.Z),
                new Vector3((float)ray3D.Direction.X, (float)ray3D.Direction.Y, (float)ray3D.Direction.Z));
        }

        private bool TryRaycast(HelixViewport3D viewport, Point position, out CoreRay ray, out Vector3 worldHitPoint)
        {
            ArgumentNullException.ThrowIfNull(viewport);
            ray = default;
            worldHitPoint = default;
            if (Target?.Mesh is not { } mesh) return false;

            ray = BuildRay(viewport, position);

            var worldTransform = Target.GetWorldTransform();
            var evaluatedMesh = ModifierStack.Evaluate(mesh, Target.Modifiers, Target);
            var skinnedMesh = Target.SkinBinding is { } binding
                ? SkinningEvaluator.Deform(evaluatedMesh, worldTransform, binding)
                : evaluatedMesh;

            float? nearestDistance = null;

            foreach (var face in skinnedMesh.GetRenderFaces())
            {
                var a = Vector3.Transform(skinnedMesh.Vertices[face.A].Position, worldTransform);
                var b = Vector3.Transform(skinnedMesh.Vertices[face.B].Position, worldTransform);
                var c = Vector3.Transform(skinnedMesh.Vertices[face.C].Position, worldTransform);

                if (RayIntersection.IntersectTriangle(ray, a, b, c) is not { } distance) continue;
                if (nearestDistance is not null && distance >= nearestDistance) continue;

                nearestDistance = distance;
                worldHitPoint = ray.Origin + ray.Direction * distance;
            }

            return nearestDistance is not null;
        }
    }
}
