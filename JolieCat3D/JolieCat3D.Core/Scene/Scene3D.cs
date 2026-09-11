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

        /// <summary>Which <see cref="Node"/> (if any) <c>JolieCat3D.Engine</c>'s renderer
        /// should actually look through - null (the default) means the viewport keeps
        /// using its own free orbit/pan/zoom camera (see <c>Engine.Camera.CameraFraming</c>),
        /// exactly as it always has, rather than snapping to some arbitrary camera the
        /// moment one exists anywhere in the scene. Not required to be a node with
        /// <see cref="Node.Camera"/> actually set (nothing here enforces that), but
        /// setting it to one that isn't leaves the renderer with nothing meaningful to
        /// sync a projection from - <c>JolieCat3D.UI</c> only ever assigns this to a
        /// node it just gave a <see cref="CameraData"/> to.</summary>
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
