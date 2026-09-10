namespace JolieCat3D.Engine.Rendering
{
    /// <summary>Which viewport shading style <see cref="Scene3DRenderer"/> currently
    /// renders the scene in - one at a time, the same single-mode-toolbar shape
    /// <c>Gizmos.GizmoMode</c>/<c>Editing.ComponentType</c> already use. Each mode is a
    /// genuinely different rendering path (see <see cref="Scene3DRenderer.Render"/> and
    /// <c>Geometry.MaterialFactory.Create</c>'s own remarks), not a cosmetic relabeling
    /// of the same look - the real differences WPF's fixed-function pipeline can
    /// actually produce.</summary>
    public enum ShadingMode
    {
        /// <summary>No filled geometry at all - just every mesh's own edges (see
        /// <see cref="WireframeVisualFactory"/>), the same wireframe overlay shape
        /// <c>Editing.ComponentMarkerVisualFactory</c>'s edge markers already use, but
        /// scene-wide. WPF's own click-based hit-testing has nothing to hit in this
        /// mode (there is no <c>GeometryModel3D</c> to click) - object selection still
        /// works from the Scene Outliner, just not by clicking the (invisible) mesh
        /// itself.</summary>
        Wireframe,

        /// <summary>Every mesh filled with the same neutral, textureless gray,
        /// regardless of its own <see cref="Core.Materials.Material"/> - a modeler's
        /// plain-shape view (matching Blender's own "Solid" viewport shading intent),
        /// for looking at form/topology without a material's color or texture
        /// distracting from it.</summary>
        Solid,

        /// <summary>Each mesh's own real diffuse color/texture, but with no specular
        /// highlight layer at all - a flat, "unlit albedo" look for judging a material's
        /// actual colors/textures without scene lighting's own highlights competing
        /// with them.</summary>
        Material,

        /// <summary>The full-quality look this project has always defaulted to: each
        /// mesh's own real diffuse color/texture AND specular highlights (with
        /// <see cref="Core.Materials.Material.Roughness"/>/<see cref="Core.Materials.Material.Metallic"/>
        /// factored in - see <c>Geometry.MaterialFactory</c>'s own remarks).</summary>
        Rendered,
    }
}
