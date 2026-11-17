using System.Numerics;
using System.Runtime.CompilerServices;
using JolieCat3D.Core.Constraints;
using JolieCat3D.Core.Geometry;
using JolieCat3D.Core.Materials;
using JolieCat3D.Core.Modifiers;
using JolieCat3D.Core.Scene;

namespace JolieCat3D.Service.Commands
{
    /// <summary>
    /// A full snapshot of one node's own editable state: name, transform, mesh, its
    /// materials (incl. painted texture pixels), modifiers, constraints, curve, camera,
    /// light, bone, armature and skin. Children are not included.
    ///
    /// Snapshots are immutable once taken and are always cloned again on restore, so a
    /// later in-place edit can never corrupt one. Unchanged meshes and textures are
    /// shared with the previous snapshot instead of copied, which keeps memory low when
    /// e.g. only a light colour changed on a heavy sculpted mesh.
    /// </summary>
    public sealed class NodeState
    {
        private string _name = string.Empty;
        private Vector3 _position;
        private Quaternion _rotation;
        private Vector3 _scale;

        private Mesh? _mesh;
        private Mesh? _liveMesh;
        private ulong _meshHash;

        private readonly List<MaterialSnapshot> _materials = new();
        private readonly List<Modifier> _modifiers = new();
        private readonly List<Constraint> _constraints = new();

        private CurveData? _curve;
        private CameraData? _camera;
        private LightData? _light;
        private BoneData? _bone;
        private ArmatureData? _armature;
        private SkinBinding? _skin;

        private sealed record MaterialSnapshot(Material Live, Material Values, TextureBuffer? Texture);

        private string _description = string.Empty;

        private NodeState() { }

        /// <summary>True when both snapshots describe exactly the same node state.</summary>
        public bool ContentEquals(NodeState other) => _description == other._description;

        /// <param name="previous">An earlier snapshot of the same node; unchanged
        /// mesh/texture data is shared with it instead of copied.</param>
        public static NodeState Capture(Node node, NodeState? previous = null)
        {
            ArgumentNullException.ThrowIfNull(node);

            var state = new NodeState
            {
                _name = node.Name,
                _position = node.LocalPosition,
                _rotation = node.LocalRotation,
                _scale = node.LocalScale,
                _curve = node.Curve?.Clone(),
                _camera = node.Camera?.Clone(),
                _light = node.Light?.Clone(),
                _bone = node.Bone?.Clone(),
                _armature = node.Armature?.Clone(),
                _skin = node.SkinBinding?.Clone(),
            };

            if (node.Mesh is { } mesh)
            {
                state._liveMesh = mesh;
                state._meshHash = MeshFingerprint.Compute(mesh);
                state._mesh = previous is { _mesh: not null } && previous._meshHash == state._meshHash
                    ? previous._mesh
                    : mesh.Clone();

                foreach (var material in EnumerateMaterials(mesh))
                {
                    var previousSnapshot = previous?._materials.FirstOrDefault(m => ReferenceEquals(m.Live, material));
                    state._materials.Add(new MaterialSnapshot(material, material.Clone(), SnapshotTexture(material.PaintedTextureBuffer, previousSnapshot?.Texture)));
                }
            }

            foreach (var modifier in node.Modifiers) state._modifiers.Add(modifier.Clone());
            foreach (var constraint in node.Constraints) state._constraints.Add(constraint.Clone());

            try { state._description = Project.Jolie3DProjectSerializer.DescribeNode(node); }
            catch (Exception) { state._description = Guid.NewGuid().ToString(); } // unknown type: treat as changed

            return state;
        }

        /// <summary>Writes this snapshot back onto <paramref name="node"/>.</summary>
        public void RestoreTo(Node node)
        {
            ArgumentNullException.ThrowIfNull(node);

            node.Name = _name;
            node.LocalPosition = _position;
            node.LocalRotation = _rotation;
            node.LocalScale = _scale;

            if (_mesh is null) node.Mesh = null;
            else
            {
                // Put back the very mesh object the node had (so linked duplicates sharing it
                // stay linked) and refill it in place when its contents moved on.
                node.Mesh = _liveMesh ?? node.Mesh ?? _mesh.Clone();
                if (MeshFingerprint.Compute(node.Mesh) != _meshHash) node.Mesh.CopyFrom(_mesh);
            }

            foreach (var snapshot in _materials)
            {
                var liveBuffer = snapshot.Live.PaintedTextureBuffer;
                snapshot.Live.CopyFrom(snapshot.Values);

                if (snapshot.Texture is null)
                {
                    snapshot.Live.PaintedTextureBuffer = null;
                }
                else if (liveBuffer is not null && liveBuffer.Width == snapshot.Texture.Width && liveBuffer.Height == snapshot.Texture.Height)
                {
                    // Keep the same buffer instance - paint sessions hold on to it.
                    liveBuffer.CopyPixelsFrom(snapshot.Texture);
                    snapshot.Live.PaintedTextureBuffer = liveBuffer;
                }
                else
                {
                    snapshot.Live.PaintedTextureBuffer = snapshot.Texture.Clone();
                }
            }

            node.Modifiers.Clear();
            foreach (var modifier in _modifiers) node.Modifiers.Add(modifier.Clone());

            node.Constraints.Clear();
            foreach (var constraint in _constraints) node.Constraints.Add(constraint.Clone());

            node.Curve = _curve?.Clone();
            node.Camera = _camera?.Clone();
            node.Light = _light?.Clone();
            node.Bone = _bone?.Clone();
            node.Armature = _armature?.Clone();
            node.SkinBinding = _skin?.Clone();
        }

        private static IEnumerable<Material> EnumerateMaterials(Mesh mesh)
        {
            var seen = new HashSet<Material>(ReferenceEqualityComparer.Instance);
            if (mesh.Material is { } main && seen.Add(main)) yield return main;
            foreach (var slot in mesh.MaterialSlots)
                if (seen.Add(slot)) yield return slot;
        }

        private static TextureBuffer? SnapshotTexture(TextureBuffer? live, TextureBuffer? previous)
        {
            if (live is null) return null;
            if (previous is not null && previous.ContentEquals(live)) return previous;
            return live.Clone();
        }
    }

    /// <summary>Undo/redo between two <see cref="NodeState"/> snapshots of one node -
    /// the generic command behind every Inspector edit, modifier/constraint change,
    /// paint/sculpt stroke, UV projection and so on.</summary>
    public sealed class NodeStateCommand : IUndoableCommand
    {
        private readonly Node _target;
        private readonly NodeState _before;
        private readonly NodeState _after;
        private readonly Action? _onChanged;

        public string Description { get; }

        public Node Target => _target;

        public NodeStateCommand(Node target, NodeState before, NodeState after, string description, Action? onChanged = null)
        {
            _target = target ?? throw new ArgumentNullException(nameof(target));
            _before = before ?? throw new ArgumentNullException(nameof(before));
            _after = after ?? throw new ArgumentNullException(nameof(after));
            Description = description ?? throw new ArgumentNullException(nameof(description));
            _onChanged = onChanged;
        }

        public void Execute()
        {
            _after.RestoreTo(_target);
            _onChanged?.Invoke();
        }

        public void Undo()
        {
            _before.RestoreTo(_target);
            _onChanged?.Invoke();
        }
    }
}
