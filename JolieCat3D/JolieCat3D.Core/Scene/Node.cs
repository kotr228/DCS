using System.Numerics;
using JolieCat3D.Core.Geometry;
using JolieCat3D.Core.Modifiers;

namespace JolieCat3D.Core.Scene
{
    /// <summary>
    /// One instance of something in 3D space: a local position, rotation, and scale, an
    /// optional <see cref="Mesh"/> to render at that transform, and optional child nodes
    /// whose own transforms compose relative to this one - the standard scene-graph node
    /// ("SceneObject" in the task's own terms). Rotation is a <see cref="Quaternion"/>,
    /// not Euler angles, for the reasons every 3D engine uses one: no gimbal lock, and
    /// composing/interpolating rotations is a single, numerically stable operation rather
    /// than three coupled ones.
    /// </summary>
    public sealed class Node
    {
        private readonly List<Node> _children = new();

        public string Name { get; set; }

        public Vector3 LocalPosition { get; set; } = Vector3.Zero;
        public Quaternion LocalRotation { get; set; } = Quaternion.Identity;
        public Vector3 LocalScale { get; set; } = Vector3.One;

        /// <summary>Optional - a node with no mesh is a pure grouping/pivot (a camera
        /// rig's own pivot point, an empty parent transform, ...).</summary>
        public Mesh? Mesh { get; set; }

        /// <summary>Optional - present only for a node meant to actually be a
        /// renderable/selectable camera (see <see cref="CameraData"/>'s own remarks on
        /// why this is optional DATA, not a subclass). Nothing here stops a node from
        /// having both this AND <see cref="Mesh"/>/<see cref="Light"/> set at once
        /// (nothing enforces "exactly one role per node") - a deliberately unconstrained
        /// design, the same "additive, not mutually exclusive" shape every other optional
        /// per-node concern in this class already has, though <c>JolieCat3D.UI</c>'s own
        /// Properties Inspector only ever creates a node with exactly one role at a
        /// time.</summary>
        public CameraData? Camera { get; set; }

        /// <summary>Optional - present only for a node meant to actually cast light (see
        /// <see cref="LightData"/>'s own remarks). Same "additive data, not a subclass,
        /// not mutually exclusive with the others" shape as <see cref="Camera"/>.</summary>
        public LightData? Light { get; set; }

        /// <summary>This node's non-destructive modifier stack (see
        /// <see cref="Modifier"/>'s own remarks) - applied, in list order, to
        /// <see cref="Mesh"/> at render time only (by <c>JolieCat3D.Engine.Geometry.SceneGraphBuilder</c>,
        /// via <see cref="ModifierStack.Evaluate"/>) to produce what actually gets drawn.
        /// <see cref="Mesh"/> itself is never mutated by any modifier - Edit Mode's own
        /// vertex/face operations (Extrude, Subdivide, a component drag) all still see
        /// and edit the same base geometry regardless of what's in this stack. Empty by
        /// default, in which case the rendered mesh is exactly <see cref="Mesh"/> itself,
        /// unchanged - every node authored before modifiers existed keeps looking exactly
        /// as it always did.</summary>
        public List<Modifier> Modifiers { get; } = new();

        public Node? Parent { get; private set; }

        public IReadOnlyList<Node> Children => _children;

        public Node(string name = "Node") => Name = name;

        public void AddChild(Node child)
        {
            ArgumentNullException.ThrowIfNull(child);
            if (child.Parent is not null) child.Parent._children.Remove(child);

            child.Parent = this;
            _children.Add(child);
        }

        public void RemoveChild(Node child)
        {
            ArgumentNullException.ThrowIfNull(child);
            if (!_children.Remove(child)) return;
            child.Parent = null;
        }

        /// <summary>This node's own Scale * Rotate * Translate transform, relative to its
        /// <see cref="Parent"/> (or to the scene root, if it has none). SRT order - scale
        /// first, then rotate, then translate - is what keeps scale from also stretching
        /// this node's position along its parent's rotated axes.</summary>
        public Matrix4x4 GetLocalTransform() =>
            Matrix4x4.CreateScale(LocalScale) *
            Matrix4x4.CreateFromQuaternion(LocalRotation) *
            Matrix4x4.CreateTranslation(LocalPosition);

        /// <summary>This node's local transform combined with every ancestor's, up to
        /// (and not including) the root - the actual position/rotation/scale this node's
        /// <see cref="Mesh"/> should render with in world space.</summary>
        public Matrix4x4 GetWorldTransform()
        {
            var local = GetLocalTransform();
            return Parent is null ? local : local * Parent.GetWorldTransform();
        }

        /// <summary>This node and every descendant, depth-first - the order
        /// <c>JolieCat3D.Engine</c>'s scene-graph adapter walks to build a
        /// <c>Model3DGroup</c>, and generally useful for anything else that needs to
        /// visit a whole subtree (a name lookup, a bounds calculation, ...).</summary>
        public IEnumerable<Node> Traverse()
        {
            yield return this;
            foreach (var child in _children)
                foreach (var descendant in child.Traverse())
                    yield return descendant;
        }

        /// <summary>This node's world-space rotation, composed with every ancestor's -
        /// world = ParentWorldRotation * LocalRotation (note: the reverse operand order
        /// from <see cref="GetWorldTransform"/>'s own local*parent - <see cref="Quaternion"/>
        /// multiplication in <see cref="System.Numerics"/> applies its right-hand operand
        /// first, the opposite of <see cref="Matrix4x4"/>'s row-vector A*B-applies-A-first
        /// convention; verified empirically before relying on it here). Used by
        /// <c>JolieCat3D.Engine</c>'s rotate gizmo to convert a world-axis drag into the
        /// correct <see cref="LocalRotation"/> change for a node under a rotated parent.</summary>
        public Quaternion GetWorldRotation() =>
            Parent is null ? LocalRotation : Parent.GetWorldRotation() * LocalRotation;

        /// <summary>This node's world-space origin - <see cref="GetWorldTransform"/>
        /// applied to the local origin. Where a selection outline or transform gizmo
        /// should be positioned.</summary>
        public Vector3 GetWorldPosition() => Vector3.Transform(Vector3.Zero, GetWorldTransform());

        /// <summary>This node's own local +Z axis, rotated into world space by
        /// <see cref="GetWorldRotation"/> - the standard "forward" convention a
        /// <see cref="CameraData"/> (its own look direction) or a directional/spot
        /// <see cref="LightData"/> (the direction it casts light toward) both use, so
        /// aiming either is just a matter of rotating this node the same way aiming
        /// anything else in the scene already is - no separate "look direction" field of
        /// its own to keep in sync with <see cref="LocalRotation"/>.</summary>
        public Vector3 GetWorldForward() => Vector3.Transform(Vector3.UnitZ, Matrix4x4.CreateFromQuaternion(GetWorldRotation()));

        /// <summary>The axis-aligned world-space bounding box of this node's own
        /// <see cref="Mesh"/> only - unlike <see cref="Scene3D.GetBounds"/>, descendants
        /// are not included, since this is what a selection-highlight outline (drawn
        /// around exactly the selected object, not its children too) needs.
        /// <c>(GetWorldPosition(), GetWorldPosition())</c> for a node with no mesh (or an
        /// empty one), so the box collapses to a point rather than being reported as
        /// nonexistent. Measured against <see cref="Mesh"/>'s own raw vertices, not
        /// <see cref="Modifiers"/>'s evaluated result - a Mirror/Subdivision Surface
        /// modifier can make the actually-rendered geometry extend beyond (Mirror) or
        /// stay within (a smoothing Subsurf) this box, a known, disclosed simplification
        /// rather than re-evaluating the whole modifier stack just for a selection
        /// outline/camera-framing bounds calculation.</summary>
        public (Vector3 Min, Vector3 Max) GetWorldBounds()
        {
            var world = GetWorldTransform();

            if (Mesh is null || Mesh.Vertices.Count == 0)
            {
                var origin = Vector3.Transform(Vector3.Zero, world);
                return (origin, origin);
            }

            var first = Vector3.Transform(Mesh.Vertices[0].Position, world);
            var min = first;
            var max = first;

            foreach (var vertex in Mesh.Vertices)
            {
                var worldPosition = Vector3.Transform(vertex.Position, world);
                min = Vector3.Min(min, worldPosition);
                max = Vector3.Max(max, worldPosition);
            }

            return (min, max);
        }
    }
}
