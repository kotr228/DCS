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

        public Scene3D(string name = "Scene") => Name = name;

        public void AddRootNode(Node node)
        {
            ArgumentNullException.ThrowIfNull(node);
            _rootNodes.Add(node);
        }

        public void RemoveRootNode(Node node) => _rootNodes.Remove(node);

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
