namespace JolieCat3D.Core.Geometry
{
    /// <summary>Which projection <see cref="UVProjector.Apply"/> (re)maps a mesh's own
    /// vertex UVs from - the "basic UV unwrapping" options a custom or edited mesh
    /// (one that has no authored UVs of its own, or whose UVs no longer make sense
    /// after Extrude/Subdivide reshaped it) needs so a texture maps onto it sensibly
    /// rather than however its stale/absent UVs happen to read.</summary>
    public enum UVProjectionMode
    {
        /// <summary>Projects straight down the Y axis onto the mesh's own XZ bounds -
        /// simple and cheap, but stretches badly on faces that aren't roughly
        /// horizontal (a wall-like vertical face reads as a single sliver of the
        /// texture). Best for terrain-like or mostly-flat meshes.</summary>
        Planar,

        /// <summary>Per-vertex triplanar/"cube map" projection: each vertex projects
        /// onto whichever of the 3 axis planes its own normal points most toward - the
        /// standard low-stretch unwrap for arbitrary or boxy geometry, and what most
        /// modeling tools mean by "Box" or "Cube" projection.</summary>
        Box,

        /// <summary>Standard equirectangular projection relative to the mesh's own
        /// bounds center - U from the azimuth angle around Y, V from the polar angle
        /// from top to bottom. The natural choice for sphere-like meshes; converges
        /// (many vertices sharing similar UVs) at the top/bottom poles the same way a
        /// world map does at the Arctic/Antarctic.</summary>
        Spherical,
    }
}
