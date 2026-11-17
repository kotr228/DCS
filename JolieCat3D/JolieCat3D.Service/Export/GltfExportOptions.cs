using JolieCat3D.Core.Scene;

namespace JolieCat3D.Service.Export
{
    /// <summary>glTF export choices (Blender's export panel, game-pipeline subset).</summary>
    public sealed class GltfExportOptions
    {
        /// <summary>Only these objects (with their children); null = the whole scene.</summary>
        public IReadOnlyCollection<Node>? SelectedRoots { get; set; }

        /// <summary>Include viewport-hidden objects (e.g. generated LODs and UCX_ colliders).</summary>
        public bool IncludeHidden { get; set; } = true;

        /// <summary>Write the actions as glTF animations.</summary>
        public bool IncludeAnimation { get; set; } = true;
    }
}
