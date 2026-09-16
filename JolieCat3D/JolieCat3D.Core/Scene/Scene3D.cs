using System.Numerics;

namespace JolieCat3D.Core.Scene
{
    /// <summary>
    /// A named collection of root-level <see cref="Node"/>s - the top of the scene graph
    /// <c>JolieCat3D.Engine</c>'s renderer walks to build the whole viewport's content in
    /// one call, rather than every caller separately tracking a loose list of root nodes
    /// itself. A plain container, same spirit as <see cref="Geometry.Mesh"/>: no
    /// rendering-API dependency at all.
    /// </summary>
    public sealed class Scene3D
    {
        private readonly List<Node> _rootNodes = new();

        public string Name { get; set; }

        public IReadOnlyList<Node> RootNodes => _rootNodes;

        /// <summary>Which <see cref="Node"/> (if any) is designated "the" scene camera -
        /// null (the default) means no node is. Setting this does NOT, on its own, change
        /// what the viewport is currently looking through: <c>JolieCat3D.Engine</c>'s
        /// renderer keeps using its own free orbit/pan/zoom camera (see
        /// <c>Engine.Camera.CameraFraming</c>) regardless, exactly as it always has,
        /// unless a separate, explicit "View > Active Camera" toggle
        /// (<c>Engine.Rendering.Scene3DRenderer.EnterActiveCameraView</c>/
        /// <c>IsPilotingActiveCamera</c>) is turned on - see that method's own remarks for
        /// why locking the viewport is a deliberate, user-driven action rather than an
        /// automatic side effect of this property being non-null. Not required to be a
        /// node with <see cref="Node.Camera"/> actually set (nothing here enforces that),
        /// but setting it to one that isn't leaves the renderer with nothing meaningful to
        /// sync a projection from - <c>JolieCat3D.UI</c> only ever assigns this to a node
        /// it just gave a <see cref="CameraData"/> to.</summary>
        public Node? ActiveCamera { get; set; }

        /// <summary>The scene-wide skybox/Image-Based Lighting setting - null (the
        /// default, matching every scene authored before this existed) means no skybox
        /// and no environment tint at all, exactly as this project has always rendered.
        /// See <see cref="EnvironmentSettings"/>'s own remarks.</summary>
        public EnvironmentSettings? Environment { get; set; }

        public Scene3D(string name = "Scene") => Name = name;

        public void AddRootNode(Node node)
        {
            ArgumentNullException.ThrowIfNull(node);
            _rootNodes.Add(node);
        }

        /// <summary>Removes <paramref name="node"/> from <see cref="RootNodes"/> -
        /// clearing <see cref="ActiveCamera"/> too if it was the one removed, so a
        /// destroyed camera node is never left dangling as "the" active camera
        /// reference (which would otherwise still resolve - nothing here nulls out a
        /// removed node's own fields - but no longer be reachable from
        /// <see cref="Traverse"/>/<see cref="RootNodes"/> at all).</summary>
        public void RemoveRootNode(Node node)
        {
            _rootNodes.Remove(node);
            if (ActiveCamera == node) ActiveCamera = null;
        }

        /// <summary>Every node in the scene, root nodes and every descendant, depth-first
        /// per root in <see cref="RootNodes"/> order.</summary>
        public IEnumerable<Node> Traverse() => _rootNodes.SelectMany(root => root.Traverse());

        /// <summary>Moves <paramref name="node"/> to become a child of
        /// <paramref name="newParent"/> - or a new root of this scene, for a null
        /// <paramref name="newParent"/> - the Scene Outliner's own drag-and-drop
        /// reparenting (drag onto another node) and un-parenting (drop into empty
        /// space) operation. Unlike <see cref="Node.AddChild"/> alone (which leaves
        /// <see cref="Node.LocalPosition"/>/<see cref="Node.LocalRotation"/>/
        /// <see cref="Node.LocalScale"/> completely untouched, so the object's WORLD
        /// transform would visibly jump the moment its parent chain changes), this
        /// recomputes those three from <paramref name="node"/>'s own CURRENT world
        /// transform (captured before anything changes) re-expressed in
        /// <paramref name="newParent"/>'s own local space (or world space directly, with
        /// no parent) - <see cref="Node.GetWorldTransform"/> reads back out exactly the
        /// same afterward, the crucial "reparenting never moves the object on screen"
        /// guarantee a Scene Outliner drag has to uphold.
        ///
        /// Returns false, changing nothing at all, for: <paramref name="newParent"/>
        /// being <paramref name="node"/> itself or one of its own descendants (the
        /// structural cycle no tree can represent - <see cref="Node.Traverse"/> already
        /// includes <paramref name="node"/> itself, so this one check covers both); or
        /// a parent chain whose combined scale/rotation is too degenerate for
        /// <see cref="Matrix4x4.Decompose"/> to recover a Position/Rotation/Scale triple
        /// from at all (an extreme, disclosed edge case - a non-uniformly-scaled,
        /// rotated ancestor chain can in principle introduce shear no TRS decomposition
        /// represents losslessly, the same accepted limitation
        /// <c>Engine.Gizmos.TransformGizmo.ApplyTranslate</c>'s own parent-conversion
        /// already carries) - refusing outright rather than silently leaving the node
        /// LOOKING like it moved.</summary>
        public bool Reparent(Node node, Node? newParent)
        {
            ArgumentNullException.ThrowIfNull(node);
            if (newParent is not null && node.Traverse().Contains(newParent)) return false;

            var worldTransform = node.GetWorldTransform();

            var newParentWorld = newParent?.GetWorldTransform() ?? Matrix4x4.Identity;
            if (!Matrix4x4.Invert(newParentWorld, out var newParentWorldToLocal)) return false;
            var newLocalMatrix = worldTransform * newParentWorldToLocal;
            if (!Matrix4x4.Decompose(newLocalMatrix, out var scale, out var rotation, out var translation)) return false;

            // Detach from wherever node currently sits - either this scene's own root
            // list, or its current parent's children (Node.RemoveChild) - before
            // attaching it at its new spot; Node.AddChild below would otherwise ALSO try
            // to detach it from a non-null Parent itself, but never from _rootNodes (a
            // root node's own Parent is already null, so AddChild's own detach step
            // wouldn't find or remove it from here on its own).
            if (node.Parent is null) _rootNodes.Remove(node);
            else node.Parent.RemoveChild(node);

            if (newParent is null) _rootNodes.Add(node);
            else newParent.AddChild(node);

            node.LocalPosition = translation;
            node.LocalRotation = rotation;
            node.LocalScale = scale;
            return true;
        }

        /// <summary>The axis-aligned world-space bounding box of every meshed node's
        /// world-transformed geometry - <c>(Vector3.Zero, Vector3.Zero)</c> for an empty
        /// scene or one with no mesh anywhere in it. <c>JolieCat3D.Engine</c>'s camera
        /// helper uses this to frame the whole scene automatically.</summary>
        public (Vector3 Min, Vector3 Max) GetBounds()
        {
            var hasAny = false;
            var min = Vector3.Zero;
            var max = Vector3.Zero;

            foreach (var node in Traverse())
            {
                if (node.Mesh is not { } mesh || mesh.Vertices.Count == 0) continue;

                var world = node.GetWorldTransform();
                foreach (var vertex in mesh.Vertices)
                {
                    var worldPosition = Vector3.Transform(vertex.Position, world);

                    if (!hasAny)
                    {
                        min = max = worldPosition;
                        hasAny = true;
                        continue;
                    }

                    min = Vector3.Min(min, worldPosition);
                    max = Vector3.Max(max, worldPosition);
                }
            }

            return (min, max);
        }
    }
}
