using System.Numerics;
using JolieCat3D.Core.Geometry;

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
    }
}
