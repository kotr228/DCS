using System.Numerics;
using JolieCat3D.Core.Geometry;
using JolieCat3D.Core.Scene;

namespace JolieCat3D.Core.Modifiers
{
    /// <summary>
    /// Constructive Solid Geometry (Boolean) modifier: combines <see cref="Modifier.Apply"/>'s
    /// input mesh with <see cref="Target"/>'s own current mesh via <see cref="CsgSolid.Combine"/>
    /// - the standard modeling-tool "Boolean" modifier (Union/Difference/Intersection
    /// against another object in the scene), the third non-destructive modifier type this
    /// project ships alongside <see cref="MirrorModifier"/>/<see cref="SubdivisionSurfaceModifier"/>.
    ///
    /// Unlike those two, this modifier's own output depends on something OUTSIDE its own
    /// node's mesh entirely - <see cref="Target"/> lives elsewhere in the scene, with its
    /// own independent transform - so <see cref="Apply"/> needs the <c>owner</c> parameter
    /// <see cref="Modifier.Apply"/> otherwise leaves unused: <see cref="Target"/>'s own mesh
    /// is authored in ITS local space, but this modifier's <paramref name="input"/> is
    /// already in the OWNER node's local space (whatever earlier modifiers in the same
    /// stack already produced) - combining the two directly, with no coordinate conversion
    /// at all, would only ever look right by coincidence (both objects sitting at the exact
    /// same position/rotation/scale). <see cref="Apply"/> re-reads both nodes' CURRENT
    /// <see cref="Node.GetWorldTransform"/> fresh on every call (never cached), so moving,
    /// rotating, or animating either the owner or <see cref="Target"/> updates the Boolean
    /// result in real time exactly the way <c>Engine.Geometry.SceneGraphBuilder</c> already
    /// re-evaluates the whole modifier stack on every render.
    /// </summary>
    public sealed class BooleanModifier : Modifier
    {
        public override string Name => "Boolean";

        public BooleanOperation Operation { get; set; } = BooleanOperation.Union;

        /// <summary>The other scene object this modifier combines its own mesh with -
        /// null (the default, and also what a dangling reference to a since-deleted node
        /// degrades to - nothing in this project nulls out a removed node's own
        /// still-live references, the same reasoning <see cref="Scene3D.RemoveRootNode"/>'s
        /// own remarks already disclose for <see cref="Scene3D.ActiveCamera"/>) means
        /// <see cref="Apply"/> is a no-op, returning <c>input</c> completely unchanged -
        /// "not yet configured" rather than an exception, the same tolerant default every
        /// other optional per-node/per-modifier reference in this project already has.</summary>
        public Node? Target { get; set; }

        /// <summary>Combines <paramref name="input"/> with <see cref="Target"/>'s own
        /// current mesh, transformed from its own local space into <paramref name="owner"/>'s
        /// (see this class's own remarks on why that conversion is necessary at all) - a
        /// no-op (returns <paramref name="input"/> unchanged) with no <see cref="Target"/>
        /// set, no mesh on it, or no <paramref name="owner"/> given (this modifier cannot
        /// resolve a relative transform at all without knowing which node it itself lives
        /// on) - never throws for an unconfigured/incomplete setup, matching every other
        /// modifier's own "nothing to do yet" tolerance.</summary>
        public override Mesh Apply(Mesh input, Node? owner = null)
        {
            ArgumentNullException.ThrowIfNull(input);

            if (Target?.Mesh is not { } targetMesh || owner is null) return input;
            if (!Matrix4x4.Invert(owner.GetWorldTransform(), out var ownerWorldToLocal)) return input;

            var targetToOwnerLocal = Target.GetWorldTransform() * ownerWorldToLocal;
            var transformedTarget = TransformMesh(targetMesh, targetToOwnerLocal);

            return CsgSolid.Combine(input, transformedTarget, Operation);
        }

        /// <summary>A copy of <paramref name="mesh"/> with every vertex position/normal
        /// transformed by <paramref name="transform"/> - <see cref="Vector3.Transform(Vector3,Matrix4x4)"/>
        /// for position (the full affine transform, translation included), but
        /// <see cref="Vector3.TransformNormal"/> against the transform's own
        /// INVERSE-TRANSPOSE for normals (the standard "normals don't transform like
        /// positions under a non-uniform scale" rule - the same one
        /// <c>Gizmos.TransformGizmo.ApplyTranslate</c>'s own parent-unwind math already
        /// relies on for a similar reason). The result's own normals are never actually
        /// read back out before <see cref="CsgSolid.Combine"/> recalculates them fresh
        /// on its own final output anyway (see that method's own remarks) - transformed
        /// here regardless so nothing downstream (a future caller, a debug inspection)
        /// finds an obviously-wrong stale normal in the meantime.</summary>
        private static Mesh TransformMesh(Mesh mesh, Matrix4x4 transform)
        {
            var result = new Mesh(mesh.Name) { Material = mesh.Material };

            var hasNormalTransform = Matrix4x4.Invert(transform, out var inverseTransform);
            var normalTransform = Matrix4x4.Transpose(inverseTransform);

            foreach (var vertex in mesh.Vertices)
            {
                var position = Vector3.Transform(vertex.Position, transform);

                var normal = vertex.Normal;
                if (hasNormalTransform)
                {
                    var transformedNormal = Vector3.TransformNormal(vertex.Normal, normalTransform);
                    if (transformedNormal.LengthSquared() > float.Epsilon) normal = Vector3.Normalize(transformedNormal);
                }

                result.AddVertex(vertex.WithPosition(position).WithNormal(normal));
            }

            foreach (var face in mesh.Faces) result.AddFace(face);
            foreach (var polygon in mesh.Polygons) result.AddPolygon(new Polygon(polygon.Indices));

            return result;
        }

        public override Modifier Clone() => new BooleanModifier
        {
            IsEnabled = IsEnabled,
            Operation = Operation,
            Target = Target,
        };
    }
}
