using JolieCat3D.Core.Scene;

namespace JolieCat3D.Service
{
    /// <summary>
    /// The one "name a <see cref="Node"/> without holding a live reference to it" scheme
    /// every Service-layer serializer that needs to point at a node from a plain data
    /// file shares - a slash-joined chain of <see cref="Node.Name"/>s from a root node
    /// down to the node itself (e.g. "Rig/Arm/Hand"), the smallest thing that identifies
    /// a node's own position in the tree without inventing a stable ID system no other
    /// part of the scene graph has. Originally <c>Animation.AnimationExporter</c>'s own
    /// private helper, extracted here once <c>Project.Jolie3DProjectSerializer</c> also
    /// needed the exact same scheme (to resolve <see cref="Scene3D.ActiveCamera"/>)
    /// rather than reimplementing (and risking silently diverging from) it a second
    /// time.
    /// </summary>
    public static class NodePathResolver
    {
        /// <summary>This node's own name, then its parent's, and so on up to its root,
        /// slash-joined in root-to-node order.</summary>
        public static string GetPath(Node node)
        {
            ArgumentNullException.ThrowIfNull(node);

            var segments = new List<string>();
            for (var current = node; current is not null; current = current.Parent)
                segments.Add(current.Name);

            segments.Reverse();
            return string.Join('/', segments);
        }

        /// <summary>The inverse of <see cref="GetPath"/>: walks <paramref name="scene"/>'s
        /// own root nodes for a name matching the path's first segment, then descends
        /// through <see cref="Node.Children"/> matching each subsequent segment in turn -
        /// null the moment any segment fails to match (including for a null/empty
        /// <paramref name="path"/>), rather than partially resolving. Where a name
        /// repeats among siblings, the first match wins - a known, disclosed ambiguity
        /// every caller of this method accepts (the same one <c>Interop.JolieProjectReader.ExtractLayerTextureByName</c>'s
        /// own name-based lookup already does), rather than a stable ID system no other
        /// part of the scene graph has.</summary>
        public static Node? FindByPath(Scene3D scene, string? path)
        {
            ArgumentNullException.ThrowIfNull(scene);
            if (string.IsNullOrEmpty(path)) return null;

            var segments = path.Split('/');
            var candidates = scene.RootNodes;
            Node? current = null;

            foreach (var segment in segments)
            {
                current = candidates.FirstOrDefault(n => n.Name == segment);
                if (current is null) return null;
                candidates = current.Children;
            }

            return current;
        }
    }
}
